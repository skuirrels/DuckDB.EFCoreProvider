using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
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

        var rewrite = CollectReplacements(shapedQueryExpression.ShaperExpression, selectExpression);
        if (rewrite.Replacements.Count == 0)
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
                rewrite.Replacements.TryGetValue(i, out var replacement)
                    ? new ProjectionExpression(replacement, projection.Alias)
                    : (ProjectionExpression)Visit(projection)!);
        }

        visitedProjections.AddRange(
            rewrite.Roots.Select(
                (root, index) => new ProjectionExpression(root, $"struct_root_{selectExpression.Projection.Count + index}")));

        var updatedSelect = selectExpression.Update(
            selectExpression.Tables.Select(t => (TableExpressionBase)Visit(t)!).ToList(),
            (SqlExpression?)Visit(selectExpression.Predicate)!,
            selectExpression.GroupBy.Select(g => (SqlExpression)Visit(g)!).ToList(),
            (SqlExpression?)Visit(selectExpression.Having)!,
            visitedProjections,
            selectExpression.Orderings.Select(o => (OrderingExpression)Visit(o)!).ToList(),
            (SqlExpression?)Visit(selectExpression.Offset)!,
            (SqlExpression?)Visit(selectExpression.Limit)!);

        return shapedQueryExpression.Update(
            updatedSelect,
            new ProjectionBindingRewritingVisitor(updatedSelect)
                .Visit(shapedQueryExpression.ShaperExpression)!);
    }

    private static StructProjectionRewrite CollectReplacements(
        Expression shaperExpression,
        SelectExpression selectExpression)
    {
        var groups = new List<StructProjectionGroup>();
        var bindingFinder = new ProjectionBindingFindingVisitor();
        bindingFinder.Visit(shaperExpression);

        foreach (var binding in bindingFinder.Bindings)
        {
            if (binding.ProjectionMember is null
                && (binding.Index is not { } projectionIndex
                    || projectionIndex < 0
                    || projectionIndex >= selectExpression.Projection.Count))
            {
                continue;
            }

            var resolved = selectExpression.GetProjection(binding);

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

                var leafTypeMapping = leafProperty.GetRelationalTypeMapping();
                var group = groups.FirstOrDefault(candidate => AreSameStructSource(candidate.Source, structField.Source));
                if (group is null)
                {
                    group = new StructProjectionGroup(structField.Source);
                    groups.Add(group);
                }

                group.Leaves.Add(
                    new StructProjectionLeaf(
                        index,
                        structField.FieldPath,
                        leafTypeMapping,
                        new DuckDBStructKeyTypeMapping(leafTypeMapping, structField.FieldPath)));
            }
        }

        var replacements = new Dictionary<int, SqlExpression>();
        var roots = new List<SqlExpression>();
        foreach (var group in groups)
        {
            roots.Add(new DuckDBWholeStructExpression(
                group.Source,
                [],
                new DuckDBStructKeyTypeMapping(
                    "STRUCT",
                    typeof(Dictionary<string, object>),
                    [])));

            foreach (var leaf in group.Leaves)
            {
                replacements[leaf.Index] = new DuckDBWholeStructExpression(
                    group.Source,
                    leaf.FieldPath,
                    leaf.ScalarTypeMapping,
                    suppressSource: true,
                    extractionTypeMapping: leaf.ExtractionTypeMapping);
            }
        }

        return new StructProjectionRewrite(replacements, roots);
    }

    private sealed record StructProjectionRewrite(
        Dictionary<int, SqlExpression> Replacements,
        IReadOnlyList<SqlExpression> Roots);

    private static bool AreSameStructSource(SqlExpression left, SqlExpression right)
        => left is ColumnExpression leftColumn
            && right is ColumnExpression rightColumn
            && string.Equals(leftColumn.TableAlias, rightColumn.TableAlias, StringComparison.Ordinal)
            && string.Equals(leftColumn.Name, rightColumn.Name, StringComparison.Ordinal)
            || left.Equals(right);

    private sealed class StructProjectionGroup(SqlExpression source)
    {
        public SqlExpression Source { get; } = source;

        public List<StructProjectionLeaf> Leaves { get; } = [];
    }

    private sealed record StructProjectionLeaf(
        int Index,
        IReadOnlyList<string> FieldPath,
        RelationalTypeMapping ScalarTypeMapping,
        DuckDBStructKeyTypeMapping ExtractionTypeMapping);

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

    private sealed class ProjectionBindingRewritingVisitor(
        SelectExpression updatedSelect) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node)
        {
            return node switch
            {
                ShapedQueryExpression => node,
                ProjectionBindingExpression binding => binding.ProjectionMember is { } projectionMember
                    ? new ProjectionBindingExpression(updatedSelect, projectionMember, binding.Type)
                    : new ProjectionBindingExpression(updatedSelect, binding.Index!.Value, binding.Type),
                _ => base.Visit(node)
            };
        }
    }
}
