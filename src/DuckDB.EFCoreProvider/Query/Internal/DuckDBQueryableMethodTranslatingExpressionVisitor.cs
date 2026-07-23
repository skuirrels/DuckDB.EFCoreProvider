using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public partial class DuckDBQueryableMethodTranslatingExpressionVisitor : RelationalQueryableMethodTranslatingExpressionVisitor
{
    /// <summary>
    ///     The column name produced by <c>json_each</c> for element keys.
    /// </summary>
    public const string JsonEachKeyColumnName = "key";

    /// <summary>
    ///     The column name produced by <c>json_each</c> for element values.
    /// </summary>
    public const string JsonEachValueColumnName = "value";

    private readonly RelationalQueryCompilationContext _queryCompilationContext;
    private readonly DuckDBTypeMappingSource _typeMappingSource;
    private readonly DuckDBSqlExpressionFactory _sqlExpressionFactory;
    private readonly SqlAliasManager _sqlAliasManager;
    private RelationalTypeMapping? _ordinalityTypeMapping;

    public DuckDBQueryableMethodTranslatingExpressionVisitor(
        QueryableMethodTranslatingExpressionVisitorDependencies dependencies,
        RelationalQueryableMethodTranslatingExpressionVisitorDependencies relationalDependencies,
        RelationalQueryCompilationContext queryCompilationContext)
        : base(dependencies, relationalDependencies, queryCompilationContext)
    {
        _queryCompilationContext = queryCompilationContext;
        _typeMappingSource = (DuckDBTypeMappingSource)relationalDependencies.TypeMappingSource;
        _sqlExpressionFactory = (DuckDBSqlExpressionFactory)relationalDependencies.SqlExpressionFactory;
        _sqlAliasManager = queryCompilationContext.SqlAliasManager;
    }

    protected DuckDBQueryableMethodTranslatingExpressionVisitor(DuckDBQueryableMethodTranslatingExpressionVisitor parentVisitor)
        : base(parentVisitor)
    {
        _queryCompilationContext = parentVisitor._queryCompilationContext;
        _typeMappingSource = parentVisitor._typeMappingSource;
        _sqlExpressionFactory = parentVisitor._sqlExpressionFactory;
        _sqlAliasManager = parentVisitor._sqlAliasManager;
    }

    /// <inheritdoc />
    protected override QueryableMethodTranslatingExpressionVisitor CreateSubqueryVisitor()
    {
        return new DuckDBQueryableMethodTranslatingExpressionVisitor(this);
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression TranslateDistinct(ShapedQueryExpression source)
    {
        if (source.TryExtractArray(out var array, out var projectedColumn, ignoreOrderings: true) ||
            (source.TryConvertToArray(out array, ignoreOrderings: true) && (projectedColumn = null) == null))
        {
            var elementClrType = array.Type.GetSequenceType();
            var isElementNullable = elementClrType.IsNullableType();

            if (!isElementNullable)
            {
                var listDistinct = _sqlExpressionFactory.Function(
                    "list_distinct",
                    [array],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    array.Type,
                    array.TypeMapping);

                var tableAlias = ((SelectExpression)source.QueryExpression).Tables[0].Alias!;
                var selectExpression = new SelectExpression(
                    [new DuckDBUnnestExpression(tableAlias, listDistinct, "value")],
                    new ColumnExpression("value", tableAlias, projectedColumn!.Type, projectedColumn.TypeMapping, projectedColumn.IsNullable),
                    [GenerateOrdinalityIdentifier(tableAlias)],
                    _queryCompilationContext.SqlAliasManager);

                Expression shaperExpression = new ProjectionBindingExpression(
                    selectExpression, new ProjectionMember(), source.ShaperExpression.Type.MakeNullable());

                if (source.ShaperExpression.Type != shaperExpression.Type)
                {
                    shaperExpression = Expression.Convert(shaperExpression, source.ShaperExpression.Type);
                }

                return new ShapedQueryExpression(selectExpression, shaperExpression);
            }
        }

        return base.TranslateDistinct(source);
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslatePrimitiveCollection(SqlExpression sqlExpression, IProperty? property, string tableAlias)
    {
        var elementClrType = sqlExpression.Type.GetSequenceType();
        var elementTypeMapping = (RelationalTypeMapping?)sqlExpression.TypeMapping?.ElementTypeMapping;

        var isElementNullable = property?.GetElementType() is null
            ? elementClrType.IsNullableType()
            : property.GetElementType()!.IsNullable;

        SelectExpression selectExpression;

#pragma warning disable EF1001 // SelectExpression constructors are pubternal
        switch (sqlExpression.TypeMapping)
        {
            case DuckDBArrayTypeMapping or null:
                {
                    var (ordinalityColumn, ordinalityComparer) = GenerateOrdinalityIdentifier(tableAlias);
                    selectExpression = new SelectExpression(
                        [new DuckDBUnnestExpression(tableAlias, sqlExpression, "value")],
                        new ColumnExpression(
                            "value",
                            tableAlias,
                            elementClrType.UnwrapNullableType(),
                            elementTypeMapping,
                            isElementNullable),
                        identifier: [(ordinalityColumn, ordinalityComparer)],
                        _queryCompilationContext.SqlAliasManager);

                    selectExpression.AppendOrdering(new OrderingExpression(ordinalityColumn, ascending: true));
                    break;
                }

            case DuckDBJsonTypeMapping:
                {
                    var keyColumnTypeMapping = _typeMappingSource.FindMapping(typeof(int))!;
                    var jsonEachExpression = new DuckDBJsonEachExpression(tableAlias, sqlExpression);
                    selectExpression = new SelectExpression(
                        [jsonEachExpression],
                        new ColumnExpression(
                            JsonEachValueColumnName,
                            tableAlias,
                            elementClrType.UnwrapNullableType(),
                            elementTypeMapping,
                            isElementNullable),
                        identifier:
                        [
                            (new ColumnExpression(JsonEachKeyColumnName, tableAlias, typeof(int), keyColumnTypeMapping, nullable: false),
                                keyColumnTypeMapping.Comparer)
                        ],
                        _sqlAliasManager);
                    break;
                }

            default:
                throw new UnreachableException();
        }
#pragma warning restore EF1001 // SelectExpression constructors are pubternal

        Expression shaperExpression = new ProjectionBindingExpression(
            selectExpression, new ProjectionMember(), elementClrType.MakeNullable());

        if (elementClrType != shaperExpression.Type)
        {
            Debug.Assert(
                elementClrType.MakeNullable() == shaperExpression.Type,
                "expression.Type must be nullable of targetType");

            shaperExpression = Expression.Convert(shaperExpression, elementClrType);
        }

        return new ShapedQueryExpression(selectExpression, shaperExpression);
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateAny(ShapedQueryExpression source, LambdaExpression? predicate)
    {
        if (source.QueryExpression is SelectExpression { Tables: [{ Alias: var tableAlias }] })
        {
            if (source.TryExtractArray(out var array, ignoreOrderings: true)
                || source.TryConvertToArray(out array, ignoreOrderings: true))
            {
                // Pattern match: x.Array.Any()
                // Translation: array_length(x.array) > 0 instead of EXISTS (SELECT 1 FROM FROM unnest(x.Array))
                if (predicate is null)
                {
                    return BuildSimplifiedShapedQuery(
                        source,
                        _sqlExpressionFactory.GreaterThan(
                            _sqlExpressionFactory.Function(
                                "array_length",
                                [array],
                                nullable: true,
                                argumentsPropagateNullability: [true],
                                typeof(int)),
                            _sqlExpressionFactory.Constant(0)));
                }
            }
        }

        return base.TranslateAny(source, predicate);
    }

    protected override ShapedQueryExpression? TranslateContains(ShapedQueryExpression source, Expression item)
    {
        if (source.TryExtractArray(out var array, ignoreOrderings: true)
            && TranslateExpression(item, applyDefaultTypeMapping: false) is { } translatedItem)
        {
            var elementClrType = array.Type.GetSequenceType();
            var isElementNullable = elementClrType.IsNullableType();
            var isItemNullable = item.Type.IsNullableType();

            // DuckDB's array_contains() does not support searching for NULL elements.
            // It is safe to use when the array element type is non-nullable (can never contain NULL)
            // or when the searched item is non-nullable (can never be NULL itself).
            if (!isElementNullable || !isItemNullable)
            {
                var elementTypeMapping = (array.TypeMapping as DuckDBArrayTypeMapping)?.ElementTypeMapping;
                translatedItem = _sqlExpressionFactory.ApplyTypeMapping(translatedItem, elementTypeMapping);

                return BuildSimplifiedShapedQuery(
                    source,
                    _sqlExpressionFactory.Function(
                        "array_contains",
                        [array, translatedItem],
                        nullable: true,
                        argumentsPropagateNullability: [true, true],
                        typeof(bool)));
            }
        }

        return base.TranslateContains(source, item);
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateElementAtOrDefault(ShapedQueryExpression source, Expression index, bool returnDefault)
    {
        if (!returnDefault && TranslateExpression(index) is { } translatedIndex)
        {
            if (source.TryExtractArray(out var array, out var projectedColumn))
            {
                return BuildArrayIndexQuery(source, array, translatedIndex, projectedColumn.IsNullable);
            }

            if (source.TryConvertToArray(out array))
            {
                return BuildArrayIndexQuery(source, array, translatedIndex, nullable: true);
            }
        }

        return base.TranslateElementAtOrDefault(source, index, returnDefault);
    }

    private ShapedQueryExpression BuildArrayIndexQuery(
        ShapedQueryExpression source,
        SqlExpression array,
        SqlExpression index,
        bool nullable)
        => source.UpdateQueryExpression(
            new SelectExpression(
                _sqlExpressionFactory.ArrayIndex(array, GenerateOneBasedIndexExpression(index), nullable),
                ((RelationalQueryCompilationContext)QueryCompilationContext).SqlAliasManager));

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateSkip(ShapedQueryExpression source, Expression count)
    {
        if (source.TryExtractArray(out var array, out var projectedColumn)
            && TranslateExpression(count) is { } translatedCount)
        {
#pragma warning disable EF1001 // SelectExpression constructors are currently internal
            var tableAlias = ((SelectExpression)source.QueryExpression).Tables[0].Alias!;
            var selectExpression = new SelectExpression(
                [
                    new DuckDBUnnestExpression(
                        tableAlias,
                        _sqlExpressionFactory.ArraySlice(
                            array,
                            lowerBound: GenerateOneBasedIndexExpression(translatedCount),
                            upperBound: null,
                            projectedColumn.IsNullable),
                        "value"),
                ],
                new ColumnExpression("value", tableAlias, projectedColumn.Type, projectedColumn.TypeMapping, projectedColumn.IsNullable),
                identifier: [GenerateOrdinalityIdentifier(tableAlias)],
                _queryCompilationContext.SqlAliasManager);
#pragma warning restore EF1001

            // Note: this can be simplified to use UpdateQueryExpression once the public API arrives upstream
            // (tracked by dotnet/efcore#31511).
            Expression shaperExpression = new ProjectionBindingExpression(
                selectExpression, new ProjectionMember(), source.ShaperExpression.Type.MakeNullable());

            if (source.ShaperExpression.Type != shaperExpression.Type)
            {
                Debug.Assert(
                    source.ShaperExpression.Type.MakeNullable() == shaperExpression.Type,
                    "expression.Type must be nullable of targetType");

                shaperExpression = Expression.Convert(shaperExpression, source.ShaperExpression.Type);
            }

            return new ShapedQueryExpression(selectExpression, shaperExpression);
        }

        return base.TranslateSkip(source, count);
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateTake(ShapedQueryExpression source, Expression count)
    {
        if (source.TryExtractArray(out var array, out var projectedColumn)
            && TranslateExpression(count) is { } translatedCount)
        {
            DuckDBArraySliceExpression sliceExpression;

            if (array is DuckDBArraySliceExpression existingSliceExpression)
            {
                if (existingSliceExpression is
                    {
                        LowerBound: SqlConstantExpression { Value: int lowerBoundValue } lowerBound,
                        UpperBound: null
                    })
                {
                    sliceExpression = existingSliceExpression.Update(
                        existingSliceExpression.Array,
                        existingSliceExpression.LowerBound,
                        translatedCount is SqlConstantExpression { Value: int takeCount }
                            ? _sqlExpressionFactory.Constant(lowerBoundValue + takeCount - 1, lowerBound.TypeMapping)
                            : _sqlExpressionFactory.Subtract(
                                _sqlExpressionFactory.Add(lowerBound, translatedCount),
                                _sqlExpressionFactory.Constant(1, lowerBound.TypeMapping)));
                }
                else
                {
                    return base.TranslateTake(source, count);
                }
            }
            else
            {
                sliceExpression = _sqlExpressionFactory.ArraySlice(
                    array,
                    lowerBound: null,
                    upperBound: translatedCount,
                    projectedColumn.IsNullable);
            }

#pragma warning disable EF1001 // SelectExpression constructors are currently internal
            var tableAlias = ((SelectExpression)source.QueryExpression).Tables[0].Alias!;
            var selectExpression = new SelectExpression(
                [new DuckDBUnnestExpression(tableAlias, sliceExpression, "value")],
                new ColumnExpression("value", tableAlias, projectedColumn.Type, projectedColumn.TypeMapping, projectedColumn.IsNullable),
                [GenerateOrdinalityIdentifier(tableAlias)],
                ((RelationalQueryCompilationContext)QueryCompilationContext).SqlAliasManager);
#pragma warning restore EF1001 // Internal EF Core API usage.

            Expression shaperExpression = new ProjectionBindingExpression(
                selectExpression, new ProjectionMember(), source.ShaperExpression.Type.MakeNullable());

            if (source.ShaperExpression.Type != shaperExpression.Type)
            {
                Debug.Assert(
                    source.ShaperExpression.Type.MakeNullable() == shaperExpression.Type,
                    "expression.Type must be nullable of targetType");

                shaperExpression = Expression.Convert(shaperExpression, source.ShaperExpression.Type);
            }

            return new ShapedQueryExpression(selectExpression, shaperExpression);
        }

        return base.TranslateTake(source, count);
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateWhere(ShapedQueryExpression source, LambdaExpression predicate)
    {
        // Simplify x.Array.Where(i => i != 3) => array_remove(x.Array, 3) instead of subquery
        if (predicate.Body is BinaryExpression
            {
                NodeType: ExpressionType.NotEqual,
                Left: var left,
                Right: var right
            }
            && (left == predicate.Parameters[0] ? right : right == predicate.Parameters[0] ? left : null) is Expression itemToFilterOut
            && source.TryExtractArray(out var array, out var projectedColumn)
            && TranslateExpression(itemToFilterOut) is SqlExpression translatedItemToFilterOut)
        {
            var simplifiedTranslation = _sqlExpressionFactory.Function(
                "array_remove",
                [array, translatedItemToFilterOut],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                array.Type,
                array.TypeMapping);

#pragma warning disable EF1001 // SelectExpression constructors are currently internal
            var tableAlias = ((SelectExpression)source.QueryExpression).Tables[0].Alias!;
            var selectExpression = new SelectExpression(
                [new DuckDBUnnestExpression(tableAlias, simplifiedTranslation, "value")],
                new ColumnExpression("value", tableAlias, projectedColumn.Type, projectedColumn.TypeMapping, projectedColumn.IsNullable),
                [GenerateOrdinalityIdentifier(tableAlias)],
                _queryCompilationContext.SqlAliasManager);
#pragma warning restore EF1001 // Internal EF Core API usage.

            // Note: this can be simplified to use UpdateQueryExpression once the public API arrives upstream
            // (tracked by dotnet/efcore#31511).
            Expression shaperExpression = new ProjectionBindingExpression(
                selectExpression, new ProjectionMember(), source.ShaperExpression.Type.MakeNullable());

            if (source.ShaperExpression.Type != shaperExpression.Type)
            {
                Debug.Assert(
                    source.ShaperExpression.Type.MakeNullable() == shaperExpression.Type,
                    "expression.Type must be nullable of targetType");

                shaperExpression = Expression.Convert(shaperExpression, source.ShaperExpression.Type);
            }

            return new ShapedQueryExpression(selectExpression, shaperExpression);
        }

        return base.TranslateWhere(source, predicate);
    }

    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateCount(ShapedQueryExpression source, LambdaExpression? predicate)
    {
        if (predicate is null)
        {
            // Simplify x.Array.Count() => array_length(x.Array) instead of SELECT COUNT(*) FROM unnest(x.Array)
            if (source.TryExtractArray(out var array, ignoreOrderings: true))
            {
                var translation = _sqlExpressionFactory.Function(
                    "array_length",
                    [array],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    typeof(int));

#pragma warning disable EF1001
                // SelectExpression constructors are currently internal
                return source.Update(
                    new SelectExpression(translation, _queryCompilationContext.SqlAliasManager),
                    Expression.Convert(
                        new ProjectionBindingExpression(source.QueryExpression, new ProjectionMember(), typeof(int?)),
                        typeof(int)));
#pragma warning restore EF1001
            }

            if (source.QueryExpression is SelectExpression
                {
                    Tables: [DuckDBJsonEachExpression { JsonExpression: var jsonArray }],
                    Predicate: null,
                    GroupBy: [],
                    Having: null,
                    IsDistinct: false,
                    Limit: null,
                    Offset: null
                })
            {
                var translation = _sqlExpressionFactory.Function(
                    "json_array_length",
                    [jsonArray],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    typeof(int));

#pragma warning disable EF1001
                return source.UpdateQueryExpression(new SelectExpression(translation, _sqlAliasManager));
#pragma warning restore EF1001
            }
        }

        return base.TranslateCount(source, predicate);
    }

    /// <inheritdoc />
    protected override bool IsNaturallyOrdered(SelectExpression selectExpression)
        => IsNaturallyOrderedUnnest(selectExpression) || IsNaturallyOrderedJsonEach(selectExpression);

    /// <inheritdoc />
    protected override Expression VisitExtension(Expression extensionExpression)
    {
        switch (extensionExpression)
        {
            case DuckDBArrayAppendExpression appendExpression:
                if (Visit(appendExpression.Source) is ShapedQueryExpression appendSource)
                {
                    return TranslateAppend(appendSource, appendExpression.Value) ?? QueryCompilationContext.NotTranslatedExpression;
                }

                return QueryCompilationContext.NotTranslatedExpression;

            case DuckDBArrayPrependExpression prependExpression:
                if (Visit(prependExpression.Source) is ShapedQueryExpression prependSource)
                {
                    return TranslatePrepend(prependSource, prependExpression.Value) ?? QueryCompilationContext.NotTranslatedExpression;
                }

                return QueryCompilationContext.NotTranslatedExpression;
        }

        return base.VisitExtension(extensionExpression);
    }

    /// <inheritdoc />
    protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
    {
        if (methodCallExpression.Method.DeclaringType == typeof(Queryable))
        {
            if (methodCallExpression.Method.Name == nameof(Queryable.Append))
            {
                if (Visit(methodCallExpression.Arguments[0]) is ShapedQueryExpression appendSource)
                {
                    return TranslateAppend(appendSource, methodCallExpression.Arguments[1])
                        ?? base.VisitMethodCall(methodCallExpression);
                }
            }

            if (methodCallExpression.Method.Name == nameof(Queryable.Prepend))
            {
                if (Visit(methodCallExpression.Arguments[0]) is ShapedQueryExpression prependSource)
                {
                    return TranslatePrepend(prependSource, methodCallExpression.Arguments[1])
                        ?? base.VisitMethodCall(methodCallExpression);
                }
            }
        }

        return base.VisitMethodCall(methodCallExpression);
    }

    protected virtual ShapedQueryExpression? TranslateAppend(ShapedQueryExpression source, Expression item)
        => TranslateArrayPush(source, item, "array_push_back");

    protected virtual ShapedQueryExpression? TranslatePrepend(ShapedQueryExpression source, Expression item)
        => TranslateArrayPush(source, item, "array_push_front");

    private ShapedQueryExpression? TranslateArrayPush(ShapedQueryExpression source, Expression item, string functionName)
    {
        if (!source.TryExtractArray(out var array, out var projectedColumn))
        {
            return null;
        }

        if (TranslateExpression(item) is not SqlExpression translatedItem)
        {
            return null;
        }

        (translatedItem, array) = _sqlExpressionFactory.ApplyTypeMappingsOnItemAndArray(translatedItem, array);

        var resultArray = _sqlExpressionFactory.Function(
            functionName,
            [array, translatedItem],
            nullable: true,
            argumentsPropagateNullability: [true, true],
            array.Type,
            array.TypeMapping);

#pragma warning disable EF1001 // SelectExpression constructors are currently internal
        var tableAlias = ((SelectExpression)source.QueryExpression).Tables[0].Alias!;
        var selectExpression = new SelectExpression(
            [new DuckDBUnnestExpression(tableAlias, resultArray, "value")],
            new ColumnExpression("value", tableAlias, projectedColumn.Type, projectedColumn.TypeMapping, projectedColumn.IsNullable),
            [GenerateOrdinalityIdentifier(tableAlias)],
            _queryCompilationContext.SqlAliasManager);
#pragma warning restore EF1001

        Expression shaperExpression = new ProjectionBindingExpression(
            selectExpression, new ProjectionMember(), source.ShaperExpression.Type.MakeNullable());

        if (source.ShaperExpression.Type != shaperExpression.Type)
        {
            Debug.Assert(
                source.ShaperExpression.Type.MakeNullable() == shaperExpression.Type,
                "expression.Type must be nullable of targetType");

            shaperExpression = Expression.Convert(shaperExpression, source.ShaperExpression.Type);
        }

        return new ShapedQueryExpression(selectExpression, shaperExpression);
    }

    private static bool IsNaturallyOrderedUnnest(SelectExpression selectExpression)
    {
        if (selectExpression.Tables is not [DuckDBUnnestExpression unnest, ..])
            return false;

        return selectExpression.Orderings is []
               || selectExpression.Orderings is [{ Expression: ColumnExpression { Name: "ordinality" } orderingColumn }]
               && orderingColumn.TableAlias == unnest.Alias;
    }

    private static bool IsNaturallyOrderedJsonEach(SelectExpression selectExpression)
        => selectExpression is
        {
            Tables: [var mainTable, ..],
            Orderings: [{ Expression: ColumnExpression { Name: JsonEachKeyColumnName } orderingColumn, IsAscending: true }]
        }
           && orderingColumn.TableAlias == mainTable.Alias
           && IsJsonEachOrWrappedJsonEach(selectExpression, orderingColumn);

    private static bool IsJsonEachOrWrappedJsonEach(SelectExpression selectExpression, ColumnExpression orderingColumn)
    {
        var table = selectExpression.Tables.FirstOrDefault(t => t.Alias == orderingColumn.TableAlias)?.UnwrapJoin();
        return table is DuckDBJsonEachExpression
               || table is SelectExpression subquery
               && subquery.Projection.FirstOrDefault(p => p.Alias == JsonEachKeyColumnName)?.Expression is ColumnExpression projectedColumn
               && IsJsonEachOrWrappedJsonEach(subquery, projectedColumn);
    }

    private (ColumnExpression, ValueComparer) GenerateOrdinalityIdentifier(string tableAlias)
    {
        _ordinalityTypeMapping ??= RelationalDependencies.TypeMappingSource.FindMapping("INT")!;
        return (new ColumnExpression("ordinality", tableAlias, typeof(int), _ordinalityTypeMapping, nullable: false),
            _ordinalityTypeMapping.Comparer);
    }

    private SqlExpression GenerateOneBasedIndexExpression(SqlExpression expression)
        => expression is SqlConstantExpression constant
            ? _sqlExpressionFactory.Constant(Convert.ToInt32(constant.Value) + 1, constant.TypeMapping)
            : _sqlExpressionFactory.Add(expression, _sqlExpressionFactory.Constant(1));

#pragma warning disable EF1001 // SelectExpression constructors are currently internal
    private ShapedQueryExpression BuildSimplifiedShapedQuery(ShapedQueryExpression source, SqlExpression translation)
        => source.Update(
            new SelectExpression(translation, _queryCompilationContext.SqlAliasManager),
            Expression.Convert(
                new ProjectionBindingExpression(translation, new ProjectionMember(), typeof(bool?)), typeof(bool)));
#pragma warning restore EF1001

    private sealed class FakeMemberInfo(string name) : MemberInfo
    {
        public override string Name { get; } = name;

        public override object[] GetCustomAttributes(bool inherit)
            => throw new NotSupportedException("FakeMemberInfo carries only a name for projection binding; no other member is available.");

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            => throw new NotSupportedException("FakeMemberInfo carries only a name for projection binding; no other member is available.");

        public override bool IsDefined(Type attributeType, bool inherit)
            => throw new NotSupportedException("FakeMemberInfo carries only a name for projection binding; no other member is available.");

        public override Type? DeclaringType
            => throw new NotSupportedException("FakeMemberInfo carries only a name for projection binding; no other member is available.");

        public override MemberTypes MemberType
            => throw new NotSupportedException("FakeMemberInfo carries only a name for projection binding; no other member is available.");

        public override Type? ReflectedType
            => throw new NotSupportedException("FakeMemberInfo carries only a name for projection binding; no other member is available.");
    }
}