using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     Removes unrequested STRUCT leaves from EF Core complex-null presence checks.
/// </summary>
internal sealed class DuckDBSelectiveStructProjectionExpressionVisitor(
    ISqlExpressionFactory sqlExpressionFactory)
    : ExpressionVisitor
{
    private readonly ISqlExpressionFactory _sqlExpressionFactory =
        sqlExpressionFactory ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));

    private IReadOnlySet<string> _selectiveRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlySet<int> _presenceProjectionIndices = new HashSet<int>();

    protected override Expression VisitExtension(Expression extensionExpression)
    {
        if (extensionExpression is ShapedQueryExpression shapedQueryExpression)
        {
            var previousPresenceIndices = _presenceProjectionIndices;
            _presenceProjectionIndices = new PresenceProjectionBindingCollector()
                .Collect(shapedQueryExpression.ShaperExpression);
            try
            {
                return shapedQueryExpression.Update(
                    Visit(shapedQueryExpression.QueryExpression),
                    Visit(shapedQueryExpression.ShaperExpression));
            }
            finally
            {
                _presenceProjectionIndices = previousPresenceIndices;
            }
        }

        if (extensionExpression is not SelectExpression selectExpression)
        {
            return base.VisitExtension(extensionExpression);
        }

        var previousRoots = _selectiveRoots;
        _selectiveRoots = previousRoots
            .Concat(CollectDirectSelectiveRoots(selectExpression.Tables))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            var visited = (SelectExpression)base.VisitExtension(selectExpression);
            return RewritePresenceChecks(visited);
        }
        finally
        {
            _selectiveRoots = previousRoots;
        }
    }

    private SelectExpression RewritePresenceChecks(SelectExpression selectExpression)
    {
        var requestedFields = CollectRequestedFields(selectExpression.Projection);
        if (requestedFields.Count == 0)
        {
            return selectExpression;
        }

        var changed = false;
        var projections = new List<ProjectionExpression>(selectExpression.Projection.Count);
        foreach (var projection in selectExpression.Projection)
        {
            if (TryRewritePresenceCheck(projection.Expression, requestedFields, out var rewritten)
                && rewritten != projection.Expression)
            {
                projections.Add(projection.Update(
                    rewritten
                    ?? CreateFallbackPresenceCheck(projection.Expression, requestedFields)
                    ?? projection.Expression));
                changed = true;
            }
            else
            {
                projections.Add(projection);
            }
        }

        return changed
            ? selectExpression.Update(
                selectExpression.Tables,
                selectExpression.Predicate,
                selectExpression.GroupBy,
                selectExpression.Having,
                projections,
                selectExpression.Orderings,
                selectExpression.Offset,
                selectExpression.Limit)
            : selectExpression;
    }

    private IReadOnlyDictionary<string, IReadOnlySet<DuckDBStructFieldExpression>> CollectRequestedFields(
        IReadOnlyList<ProjectionExpression> projections)
    {
        var requestedFields = new Dictionary<string, HashSet<DuckDBStructFieldExpression>>(
            StringComparer.OrdinalIgnoreCase);

        for (var projectionIndex = 0; projectionIndex < projections.Count; projectionIndex++)
        {
            var projection = projections[projectionIndex];
            var fields = new StructFieldCollector().Collect(projection.Expression);
            if (_presenceProjectionIndices.Contains(projectionIndex)
                && IsNullPresenceExpression(projection.Expression))
            {
                continue;
            }

            foreach (var field in fields)
            {
                var rootKey = GetRootKey(field);
                if (!_selectiveRoots.Contains(rootKey))
                {
                    continue;
                }

                if (!requestedFields.TryGetValue(rootKey, out var rootFields))
                {
                    rootFields = [];
                    requestedFields.Add(rootKey, rootFields);
                }

                rootFields.Add(field);
            }
        }

        return requestedFields.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<DuckDBStructFieldExpression>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private SqlExpression? CreateFallbackPresenceCheck(
        SqlExpression expression,
        IReadOnlyDictionary<string, IReadOnlySet<DuckDBStructFieldExpression>> requestedFields)
    {
        var field = new StructFieldCollector()
            .Collect(expression)
            .SelectMany(candidate => requestedFields.TryGetValue(GetRootKey(candidate), out var fields)
                ? fields
                : Enumerable.Empty<DuckDBStructFieldExpression>())
            .FirstOrDefault();

        if (field is null)
        {
            return null;
        }

        var nullConstant = _sqlExpressionFactory.Constant(null, field.Type, field.TypeMapping);
        return FindNullCheckOperator(expression) == ExpressionType.Equal
            ? _sqlExpressionFactory.Equal(field, nullConstant)
            : _sqlExpressionFactory.NotEqual(field, nullConstant);
    }

    private bool TryRewritePresenceCheck(
        SqlExpression expression,
        IReadOnlyDictionary<string, IReadOnlySet<DuckDBStructFieldExpression>> requestedFields,
        out SqlExpression? rewritten)
    {
        if (TryGetNullCheckField(expression, out var field))
        {
            var rootKey = GetRootKey(field);
            if (!_selectiveRoots.Contains(rootKey)
                || !requestedFields.TryGetValue(rootKey, out var rootFields))
            {
                rewritten = null;
                return false;
            }

            rewritten = rootFields.Any(candidate => GetFieldKey(candidate) == GetFieldKey(field))
                ? expression
                : null;
            return true;
        }

        if (expression is not SqlBinaryExpression
            {
                OperatorType: ExpressionType.AndAlso or ExpressionType.OrElse
            } binary
            || !TryRewritePresenceCheck(binary.Left, requestedFields, out var left)
            || !TryRewritePresenceCheck(binary.Right, requestedFields, out var right))
        {
            rewritten = null;
            return false;
        }

        rewritten = left is null
            ? right
            : right is null
                ? left
                : binary.OperatorType == ExpressionType.AndAlso
                    ? _sqlExpressionFactory.AndAlso(left, right)
                    : _sqlExpressionFactory.OrElse(left, right);
        return true;
    }

    private bool IsNullPresenceExpression(SqlExpression expression)
    {
        if (TryGetNullCheckField(expression, out var field))
        {
            return _selectiveRoots.Contains(GetRootKey(field));
        }

        return expression is SqlBinaryExpression
        {
            OperatorType: ExpressionType.AndAlso or ExpressionType.OrElse
        } binary
            && IsNullPresenceExpression(binary.Left)
            && IsNullPresenceExpression(binary.Right);
    }

    /// <summary>
    ///     Finds the operator of the first leaf null-check in a struct null-presence
    ///     expression. Presence checks are uniform, so the first leaf is representative.
    /// </summary>
    private static ExpressionType? FindNullCheckOperator(SqlExpression expression)
    {
        if (expression is SqlUnaryExpression
            {
                OperatorType: ExpressionType.Equal or ExpressionType.NotEqual,
                Operand: DuckDBStructFieldExpression
            } unary)
        {
            return unary.OperatorType;
        }

        if (expression is SqlBinaryExpression
            {
                OperatorType: ExpressionType.Equal or ExpressionType.NotEqual
            } binary
            && (binary.Left is DuckDBStructFieldExpression
                && binary.Right is SqlConstantExpression { Value: null }
                || binary.Right is DuckDBStructFieldExpression
                && binary.Left is SqlConstantExpression { Value: null }))
        {
            return binary.OperatorType;
        }

        return expression is SqlBinaryExpression
            {
                OperatorType: ExpressionType.AndAlso or ExpressionType.OrElse
            } nested
                ? FindNullCheckOperator(nested.Left) ?? FindNullCheckOperator(nested.Right)
                : null;
    }

    private static bool TryGetNullCheckField(
        SqlExpression expression,
        out DuckDBStructFieldExpression field)
    {
        if (expression is SqlUnaryExpression
            {
                OperatorType: ExpressionType.Equal or ExpressionType.NotEqual,
                Operand: DuckDBStructFieldExpression unaryField
            })
        {
            field = unaryField;
            return true;
        }

        if (expression is SqlBinaryExpression
            {
                OperatorType: ExpressionType.Equal or ExpressionType.NotEqual
            } binary)
        {
            if (binary.Left is DuckDBStructFieldExpression leftField
                && binary.Right is SqlConstantExpression { Value: null })
            {
                field = leftField;
                return true;
            }

            if (binary.Right is DuckDBStructFieldExpression rightField
                && binary.Left is SqlConstantExpression { Value: null })
            {
                field = rightField;
                return true;
            }
        }

        field = null!;
        return false;
    }

    private static IReadOnlySet<string> CollectDirectSelectiveRoots(
        IReadOnlyList<TableExpressionBase> tables)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            if (table is JoinExpressionBase join)
            {
                roots.UnionWith(CollectDirectSelectiveRoots([join.Table]));
                continue;
            }

            if (table is not ITableBasedExpression { Table: not null } tableBased
                || table.Alias is null)
            {
                continue;
            }

            foreach (var metadata in tableBased.Table.EntityTypeMappings
                         .Select(mapping => mapping.TypeBase)
                         .OfType<IEntityType>()
                         .Select(entityType => new
                         {
                             Metadata = entityType.GetStructMetadata(),
                             Mappings = entityType.GetComplexProperties()
                                 .Select(complexProperty => complexProperty.GetStructMapping())
                                 .Where(mapping => mapping is not null)
                                 .Cast<DuckDBStructMapping>()
                         }))
            {
                var mappings = (metadata.Metadata?.Roots ?? [])
                    .Concat(metadata.Mappings)
                    .Where(mapping => mapping.SelectiveProjection);
                foreach (var root in mappings)
                {
                    roots.Add(GetRootKey(table.Alias, root.StructColumnName));
                }
            }
        }

        return roots;
    }

    private static string GetRootKey(DuckDBStructFieldExpression field)
        => GetRootKey(field.TableAlias, field.StructColumnName);

    private static string GetRootKey(string tableAlias, string structColumnName)
        => $"{tableAlias}\0{structColumnName}";

    private static string GetFieldKey(DuckDBStructFieldExpression field)
        => $"{GetRootKey(field)}\0{string.Join("\0", field.FieldPath)}";

    private sealed class StructFieldCollector : ExpressionVisitor
    {
        private readonly HashSet<DuckDBStructFieldExpression> _fields = [];

        public IReadOnlySet<DuckDBStructFieldExpression> Collect(SqlExpression expression)
        {
            _fields.Clear();
            Visit(expression);
            return _fields;
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is DuckDBStructFieldExpression field)
            {
                _fields.Add(field);
            }

            return base.VisitExtension(node);
        }
    }

    private sealed class PresenceProjectionBindingCollector : ExpressionVisitor
    {
        private readonly HashSet<int> _indices = [];

        public IReadOnlySet<int> Collect(Expression expression)
        {
            _indices.Clear();
            Visit(expression);
            return _indices;
        }

        protected override Expression VisitConditional(ConditionalExpression node)
        {
            new ProjectionBindingCollector(_indices).Visit(node.Test);
            return base.VisitConditional(node);
        }
    }

    private sealed class ProjectionBindingCollector(HashSet<int> indices) : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            if (node is ProjectionBindingExpression { Index: int index })
            {
                indices.Add(index);
            }

            return base.VisitExtension(node);
        }
    }
}