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

        var predicate = RewritePresenceCheckExpression(selectExpression.Predicate, requestedFields);
        var having = RewritePresenceCheckExpression(selectExpression.Having, requestedFields);
        changed = predicate != selectExpression.Predicate || having != selectExpression.Having;

        var projections = new List<ProjectionExpression>(selectExpression.Projection.Count);
        foreach (var projection in selectExpression.Projection)
        {
            var rewritten = RewritePresenceCheckExpression(projection.Expression, requestedFields);
            if (rewritten != projection.Expression)
            {
                projections.Add(projection.Update(rewritten!));
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
                predicate,
                selectExpression.GroupBy,
                having,
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

    /// <summary>
    ///     Rewrites EF-generated null-presence checks nested anywhere within a SQL expression -
    ///     in the predicate, having clause and projections - so that they only reference fields that
    ///     are actually projected. Because EF Core narrows a complex property null check to a single
    ///     representative field during translation, the check must be rebuilt from every requested
    ///     (projected) field of the struct root.
    /// </summary>
    /// <remarks>
    ///     Only <see cref="DuckDBStructPresenceCheckExpression" /> markers - which EF translation
    ///     emits exclusively for whole-complex null comparisons - are rewritten. User leaf-null
    ///     filters such as <c>entity.Complex.Leaf == null</c> never carry the marker and are left
    ///     untouched, so their semantics are always preserved.
    /// </remarks>
    private SqlExpression? RewritePresenceCheckExpression(
        SqlExpression? expression,
        IReadOnlyDictionary<string, IReadOnlySet<DuckDBStructFieldExpression>> requestedFields)
    {
        if (expression is null)
        {
            return null;
        }

        // The marker can be nested at arbitrary depth - inside the CASE produced by a conditional,
        // a COALESCE, a function argument and so on - so the whole tree is visited recursively and
        // rebuilt only when at least one marker was actually rewritten.
        var presenceCheckRewriter = new PresenceCheckRewritingExpressionVisitor(this, requestedFields);
        return (SqlExpression?)presenceCheckRewriter.Visit(expression);
    }

    /// <summary>
    ///     Rebuilds a single EF-generated presence check so it only references projected struct
    ///     fields, or returns the original comparison unchanged when it already is valid.
    /// </summary>
    private SqlExpression RewritePresenceMarker(
        DuckDBStructPresenceCheckExpression presenceCheck,
        IReadOnlyDictionary<string, IReadOnlySet<DuckDBStructFieldExpression>> requestedFields)
    {
        var fields = new StructFieldCollector().Collect(presenceCheck.CheckedExpression);
        if (fields.Count == 0)
        {
            // Not a STRUCT-mapped complex type (for example JSON or table splitting). The
            // EF-generated comparison already targets the physical column and is valid as-is.
            return presenceCheck.CheckedExpression;
        }

        var rootKey = GetRootKey(fields.First());
        if (!_selectiveRoots.Contains(rootKey)
            || !requestedFields.TryGetValue(rootKey, out var rootFields)
            || rootFields.Count == 0
            || fields.All(field => rootFields.Any(candidate => GetFieldKey(candidate) == GetFieldKey(field))))
        {
            // Every field the EF-generated check references is projected (or the struct is not
            // selectively projected), so the original comparison is already valid.
            return presenceCheck.CheckedExpression;
        }

        return CreatePresenceCheck(presenceCheck.OperatorType, rootFields);
    }

    /// <summary>
    ///     Builds a null-presence check over every given struct field. A complex property is null
    ///     only when all of its projected members are null, so an IS NULL check (Equal) combines the
    ///     per-field checks with AND and an IS NOT NULL check (NotEqual) combines them with OR.
    /// </summary>
    private SqlExpression CreatePresenceCheck(
        ExpressionType nullCheckOperator,
        IReadOnlySet<DuckDBStructFieldExpression> fields)
    {
        SqlExpression? presenceCheck = null;
        foreach (var field in fields)
        {
            var nullConstant = _sqlExpressionFactory.Constant(null, field.Type, field.TypeMapping);
            var fieldCheck = nullCheckOperator == ExpressionType.Equal
                ? _sqlExpressionFactory.Equal(field, nullConstant)
                : _sqlExpressionFactory.NotEqual(field, nullConstant);

            presenceCheck = presenceCheck is null
                ? fieldCheck
                : nullCheckOperator == ExpressionType.Equal
                    ? _sqlExpressionFactory.AndAlso(presenceCheck, fieldCheck)
                    : _sqlExpressionFactory.OrElse(presenceCheck, fieldCheck);
        }

        return presenceCheck!;
    }

    private bool IsNullPresenceExpression(SqlExpression expression)
    {
        if (expression is DuckDBStructPresenceCheckExpression)
        {
            return true;
        }

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

    /// <summary>
    ///     Recursively rewrites <see cref="DuckDBStructPresenceCheckExpression" /> markers found
    ///     anywhere in a SQL expression tree. Unlike a root-only rewrite, this visitor descends into
    ///     every child expression, so markers nested inside a CASE produced by a conditional, a
    ///     COALESCE, a function argument or any other parent node are rewritten just like markers at
    ///     the root. The tree is rebuilt only when at least one marker was actually rewritten.
    /// </summary>
    private sealed class PresenceCheckRewritingExpressionVisitor(
        DuckDBSelectiveStructProjectionExpressionVisitor owner,
        IReadOnlyDictionary<string, IReadOnlySet<DuckDBStructFieldExpression>> requestedFields)
        : ExpressionVisitor
    {
        protected override Expression VisitExtension(Expression node)
        {
            if (node is DuckDBStructPresenceCheckExpression presenceCheck)
            {
                return owner.RewritePresenceMarker(presenceCheck, requestedFields);
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