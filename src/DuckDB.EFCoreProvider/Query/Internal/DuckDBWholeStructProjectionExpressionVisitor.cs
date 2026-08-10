using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     Rewrites the projection entries of a whole-struct materialization so that every struct
///     leaf is read from the entire STRUCT column (as a client-side dictionary) instead of
///     emitting a per-field <c>struct."field"</c> SQL expression.
/// </summary>
/// <remarks>
///     The rewrite is driven by the materialization shaper: a projection binding that resolves to
///     the shaper's property-index dictionary means the CLR query materializes a whole complex
///     struct object. Each struct leaf in that dictionary is then replaced by a
///     <see cref="DuckDBWholeStructExpression" /> whose
///     <see cref="DuckDB.EFCoreProvider.Storage.Internal.DuckDBStructKeyTypeMapping" /> extracts the
///     field client-side with a presence check, which both avoids the binder error raised for
///     struct members without a backing field and eliminates per-field SQL null checks.
///     Scalar single-field projections do not resolve to such a dictionary and are left untouched.
/// </remarks>
internal sealed class DuckDBWholeStructProjectionExpressionVisitor : ExpressionVisitor
{
    /// <inheritdoc />
    [return: NotNullIfNotNull(nameof(node))]
    public override Expression? Visit(Expression? node)
        => node switch
        {
            ShapedQueryExpression shapedQueryExpression => VisitShapedQuery(shapedQueryExpression),
            _ => base.Visit(node)
        };

    private Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
        if (shapedQueryExpression.QueryExpression is not SelectExpression selectExpression)
        {
            return shapedQueryExpression.Update(
                Visit(shapedQueryExpression.QueryExpression)!,
                Visit(shapedQueryExpression.ShaperExpression)!);
        }

        var rewritePlan = CollectReplacements(shapedQueryExpression.ShaperExpression, selectExpression);
        if (rewritePlan.Replacements.Count == 0)
        {
            return shapedQueryExpression.Update(
                Visit(shapedQueryExpression.QueryExpression)!,
                Visit(shapedQueryExpression.ShaperExpression)!);
        }

        var visitedProjections = new List<ProjectionExpression>(selectExpression.Projection.Count);
        for (var i = 0; i < selectExpression.Projection.Count; i++)
        {
            var projection = selectExpression.Projection[i];
            visitedProjections.Add(
                rewritePlan.Replacements.TryGetValue(i, out var replacement)
                    ? new ProjectionExpression(replacement, projection.Alias)
                    : (ProjectionExpression)Visit(projection)!);
        }

        var updatedSelect = selectExpression.Update(
            selectExpression.Tables
                .Concat(rewritePlan.Tables)
                .Select(t => (TableExpressionBase)Visit(t)!)
                .ToList(),
            (SqlExpression?)Visit(selectExpression.Predicate)!,
            selectExpression.GroupBy.Select(g => (SqlExpression)Visit(g)!).ToList(),
            (SqlExpression?)Visit(selectExpression.Having)!,
            visitedProjections,
            selectExpression.Orderings.Select(o => (OrderingExpression)Visit(o)!).ToList(),
            (SqlExpression?)Visit(selectExpression.Offset)!,
            (SqlExpression?)Visit(selectExpression.Limit)!);

        return shapedQueryExpression.Update(
            updatedSelect,
            Visit(shapedQueryExpression.ShaperExpression)!);
    }

    private static StructProjectionRewritePlan CollectReplacements(
        Expression shaperExpression,
        SelectExpression selectExpression)
    {
        var replacements = new Dictionary<int, SqlExpression>();
        var roots = new List<StructProjectionRoot>();
        var usedAliases = selectExpression.Tables
            .SelectMany(GetTableAliases)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aliasNumber = 0;
        var bindingFinder = new ProjectionBindingFindingVisitor();
        bindingFinder.Visit(shaperExpression);

        foreach (var binding in bindingFinder.Bindings)
        {
            Expression resolved;
            try
            {
                resolved = selectExpression.GetProjection(binding);
            }
            catch
            {
                continue;
            }

            if (resolved is not ConstantExpression { Value: Dictionary<IPropertyBase, int> propertyIndexes })
            {
                continue;
            }

            foreach (var (property, index) in propertyIndexes)
            {
                if (property is not IProperty { DeclaringType: IComplexType } leafProperty
                    || leafProperty.GetStructFieldInfo() is null
                    || index < 0
                    || index >= selectExpression.Projection.Count
                    || selectExpression.Projection[index].Expression is not DuckDBStructFieldExpression structField)
                {
                    continue;
                }

                var root = roots.FirstOrDefault(candidate => candidate.Source.Equals(structField.Source));
                if (root is null)
                {
                    string alias;
                    do
                    {
                        alias = $"__duckdb_struct_{aliasNumber++}";
                    }
                    while (!usedAliases.Add(alias));

                    root = new StructProjectionRoot(
                        structField.Source,
                        alias);
                    roots.Add(root);
                }

                var leafTypeMapping = leafProperty.GetRelationalTypeMapping();
                replacements[index] = new DuckDBWholeStructExpression(
                    new ColumnExpression(
                        root.ColumnName,
                        root.Alias,
                        typeof(object),
                        typeMapping: null,
                        nullable: true),
                    structField.FieldPath,
                    new DuckDBStructKeyTypeMapping(leafTypeMapping, structField.FieldPath));
            }
        }

        foreach (var root in roots)
        {
            root.Table = new CrossApplyExpression(
                new ValuesExpression(
                    root.Alias,
                    [new RowValueExpression([root.Source])],
                    [root.ColumnName]));
        }

        return new StructProjectionRewritePlan(
            replacements,
            roots.Select(root => root.Table!).ToArray());

        static IEnumerable<string> GetTableAliases(TableExpressionBase table)
        {
            if (table.Alias is { } alias)
            {
                yield return alias;
            }

            if (table is JoinExpressionBase join)
            {
                foreach (var nestedAlias in GetTableAliases(join.Table))
                {
                    yield return nestedAlias;
                }
            }
        }
    }

    private sealed class StructProjectionRoot
    {
        public StructProjectionRoot(SqlExpression source, string alias)
        {
            Source = source;
            Alias = alias;
        }

        public SqlExpression Source { get; }

        public string Alias { get; }

        public string ColumnName { get; } = "value";

        public CrossApplyExpression? Table { get; set; }
    }

    private sealed record StructProjectionRewritePlan(
        IReadOnlyDictionary<int, SqlExpression> Replacements,
        IReadOnlyList<TableExpressionBase> Tables);

    private sealed class ProjectionBindingFindingVisitor : ExpressionVisitor
    {
        private readonly List<ProjectionBindingExpression> _bindings = [];

        public IReadOnlyList<ProjectionBindingExpression> Bindings => _bindings;

        public override Expression? Visit(Expression? node)
        {
            switch (node)
            {
                // Projection bindings inside a nested shaped query belong to that subquery's
                // SelectExpression, not the one this visitor resolves against.
                case ShapedQueryExpression:
                    return node;
                case ProjectionBindingExpression binding:
                    _bindings.Add(binding);
                    return node;
                default:
                    return base.Visit(node);
            }
        }
    }
}
