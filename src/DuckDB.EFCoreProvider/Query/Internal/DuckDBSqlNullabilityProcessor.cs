using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBSqlNullabilityProcessor : SqlNullabilityProcessor
{
    private readonly DuckDBSqlExpressionFactory _sqlExpressionFactory;

    public DuckDBSqlNullabilityProcessor(
        RelationalParameterBasedSqlProcessorDependencies dependencies,
        RelationalParameterBasedSqlProcessorParameters parameters)
        : base(dependencies, parameters)
    {
        _sqlExpressionFactory = (DuckDBSqlExpressionFactory)dependencies.SqlExpressionFactory;
    }

    /// <inheritdoc />
    protected override Expression VisitExtension(Expression node)
        => node switch
        {
            DuckDBUnnestExpression unnestExpression => VisitUnnest(unnestExpression),
            _ => base.VisitExtension(node)
        };

    protected override bool IsCollectionTable(TableExpressionBase table, [NotNullWhen(true)] out Expression? collection)
    {
        switch (table)
        {
            case TableValuedFunctionExpression { Name: "json_each", Schema: null, IsBuiltIn: true, Arguments: [var jsonArgument] }:
                collection = jsonArgument;
                return true;

            case DuckDBUnnestExpression unnest:
                collection = unnest.Array;
                return true;

            case TableValuedFunctionExpression { Name: "unnest", Schema: null, IsBuiltIn: true, Arguments: [var arrayArgument] }:
                collection = arrayArgument;
                return true;
        }

        return base.IsCollectionTable(table, out collection);
    }

    /// <inheritdoc />
    protected override TableExpressionBase UpdateParameterCollection(
        TableExpressionBase table,
        SqlParameterExpression newCollectionParameter)
        => table switch
        {
            TableValuedFunctionExpression { Name: "json_each", Schema: null, IsBuiltIn: true, Arguments: [SqlParameterExpression] } jsonEach
                => jsonEach.Update([newCollectionParameter]),
            DuckDBUnnestExpression { Arguments: [SqlParameterExpression] } unnest
                => unnest.Update(newCollectionParameter),
            TableValuedFunctionExpression { Name: "unnest", Schema: null, IsBuiltIn: true, Arguments: [SqlParameterExpression] } unnest
                => unnest.Update([newCollectionParameter]),
            _ => base.UpdateParameterCollection(table, newCollectionParameter)
        };

    /// <inheritdoc />
    protected override SqlExpression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression, bool allowOptimizedExpansion, out bool nullable)
    {
        return sqlBinaryExpression switch
        {
            {
                    OperatorType: ExpressionType.Equal or ExpressionType.NotEqual,
                    Left: DuckDBRowValueExpression leftRowValue,
                    Right: DuckDBRowValueExpression rightRowValue
                }
                => VisitRowValueComparison(sqlBinaryExpression.OperatorType, leftRowValue, rightRowValue, out nullable),

            _ => base.VisitSqlBinary(sqlBinaryExpression, allowOptimizedExpansion, out nullable)
        };
        
        SqlExpression VisitRowValueComparison(
            ExpressionType operatorType,
            DuckDBRowValueExpression leftRowValue,
            DuckDBRowValueExpression rightRowValue,
            out bool nullable)
        {
            Debug.Assert(leftRowValue.Values.Count == rightRowValue.Values.Count, "left.Values.Count == right.Values.Count");
            var count = leftRowValue.Values.Count;

            SqlExpression? expandedExpression = null;
            List<SqlExpression>? visitedLeftValues = null;
            List<SqlExpression>? visitedRightValues = null;

            for (var i = 0; i < count; i++)
            {
                var leftValue = leftRowValue.Values[i];
                var rightValue = rightRowValue.Values[i];
                var visitedLeftValue = Visit(leftRowValue.Values[i], out var leftNullable);
                var visitedRightValue = Visit(rightRowValue.Values[i], out var rightNullable);

                if (!leftNullable && !rightNullable
                    || allowOptimizedExpansion && operatorType is ExpressionType.Equal && (!leftNullable || !rightNullable))
                {
                    if (visitedLeftValue != leftValue && visitedLeftValues is null)
                    {
                        visitedLeftValues = SliceToList(leftRowValue.Values, count, i);
                    }

                    visitedLeftValues?.Add(visitedLeftValue);

                    if (visitedRightValue != rightValue && visitedRightValues is null)
                    {
                        visitedRightValues = SliceToList(rightRowValue.Values, count, i);
                    }

                    visitedRightValues?.Add(visitedRightValue);

                    continue;
                }

                var valueBinaryExpression = Visit(
                    _sqlExpressionFactory.MakeBinary(
                        operatorType, visitedLeftValue, visitedRightValue, typeMapping: null, existingExpression: sqlBinaryExpression)!,
                    allowOptimizedExpansion,
                    out _);

                if (expandedExpression is null)
                {
                    visitedLeftValues = SliceToList(leftRowValue.Values, count, i);
                    visitedRightValues = SliceToList(rightRowValue.Values, count, i);

                    expandedExpression = valueBinaryExpression;
                }
                else
                {
                    expandedExpression = operatorType switch
                    {
                        ExpressionType.Equal => _sqlExpressionFactory.AndAlso(expandedExpression, valueBinaryExpression),
                        ExpressionType.NotEqual => _sqlExpressionFactory.OrElse(expandedExpression, valueBinaryExpression),
                        _ => throw new UnreachableException()
                    };
                }
            }

            // Known limitation: the nullability of an expanded row-value comparison is conservatively treated
            // as non-nullable rather than derived from the operands. Computing it precisely is tracked by #3250.
            nullable = false;

            if (expandedExpression is null)
            {
                return visitedLeftValues is null && visitedRightValues is null
                    ? sqlBinaryExpression
                    : _sqlExpressionFactory.MakeBinary(
                        operatorType,
                        visitedLeftValues is null
                            ? leftRowValue
                            : new DuckDBRowValueExpression(visitedLeftValues, leftRowValue.Type, leftRowValue.TypeMapping),
                        visitedRightValues is null
                            ? rightRowValue
                            : new DuckDBRowValueExpression(visitedRightValues, leftRowValue.Type, leftRowValue.TypeMapping),
                        typeMapping: null,
                        existingExpression: sqlBinaryExpression)!;
            }

            Debug.Assert(visitedLeftValues is not null, "visitedLeftValues is not null");
            Debug.Assert(visitedRightValues is not null, "visitedRightValues is not null");

            if (visitedLeftValues.Count is 0)
            {
                return expandedExpression;
            }

            var unexpandedExpression = visitedLeftValues.Count is 1
                ? _sqlExpressionFactory.MakeBinary(operatorType, visitedLeftValues[0], visitedRightValues[0], typeMapping: null)!
                : _sqlExpressionFactory.MakeBinary(
                    operatorType,
                    new DuckDBRowValueExpression(visitedLeftValues, leftRowValue.Type, leftRowValue.TypeMapping),
                    new DuckDBRowValueExpression(visitedRightValues, rightRowValue.Type, rightRowValue.TypeMapping),
                    typeMapping: null)!;

            return _sqlExpressionFactory.MakeBinary(
                operatorType: operatorType switch
                {
                    ExpressionType.Equal => ExpressionType.AndAlso,
                    ExpressionType.NotEqual => ExpressionType.OrElse,
                    _ => throw new UnreachableException()
                },
                unexpandedExpression,
                expandedExpression,
                typeMapping: null)!;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static List<SqlExpression> SliceToList(IReadOnlyList<SqlExpression> source, int capacity, int count)
            {
                var list = new List<SqlExpression>(capacity);

                for (var i = 0; i < count; i++)
                {
                    list.Add(source[i]);
                }

                return list;
            }
        }
    }

    /// <inheritdoc />
    protected override SqlExpression VisitCustomSqlExpression(SqlExpression sqlExpression, bool allowOptimizedExpansion, out bool nullable)
    {
        return sqlExpression switch
        {
            DuckDBAnyExpression e => VisitAny(e, allowOptimizedExpansion, out nullable),
            DuckDBBinaryExpression e => VisitBinary(e, allowOptimizedExpansion, out nullable),
            DuckDBArrayIndexExpression e => VisitArrayIndex(e, allowOptimizedExpansion, out nullable),
            DuckDBArraySliceExpression e => VisitArraySlice(e, allowOptimizedExpansion, out nullable),
            DuckDBRowValueExpression e => VisitRowValueExpression(e, allowOptimizedExpansion, out nullable),
            _ => base.VisitCustomSqlExpression(sqlExpression, allowOptimizedExpansion, out nullable)
        };
    }

    private DuckDBUnnestExpression VisitUnnest(DuckDBUnnestExpression unnestExpression)
    {
        var array = Visit(unnestExpression.Array, out _);

        return array == unnestExpression.Array
            ? unnestExpression
            : unnestExpression.Update(array!);
    }

    protected virtual SqlExpression VisitAny(DuckDBAnyExpression anyExpression, bool allowOptimizedExpansion, out bool nullable)
    {
        ArgumentNullException.ThrowIfNull(anyExpression);

        var item = Visit(anyExpression.Item, out var itemNullable);
        var array = Visit(anyExpression.Array, out var entireArrayNullable);

        SqlExpression updated = anyExpression.Update(item, array);

        if (UseRelationalNulls)
        {
            nullable = false;
            return updated;
        }

        nullable = false;

        if (!allowOptimizedExpansion)
        {
            updated = _sqlExpressionFactory.And(updated, _sqlExpressionFactory.IsNotNull(updated));
        }

        if (!itemNullable)
        {
            return updated;
        }

        return _sqlExpressionFactory.OrElse(
            updated,
            _sqlExpressionFactory.AndAlso(
                _sqlExpressionFactory.IsNull(item),
                _sqlExpressionFactory.IsNotNull(
                    _sqlExpressionFactory.Function(
                        "array_position",
                        [array, _sqlExpressionFactory.Constant(null, item.Type, item.TypeMapping)],
                        nullable: true,
                        argumentsPropagateNullability: [false, false],
                        typeof(int)))));
    }

    protected virtual SqlExpression VisitBinary(DuckDBBinaryExpression binaryExpression, bool allowOptimizedExpansion, out bool nullable)
    {
        var leftExpression = Visit(binaryExpression.Left, allowOptimizedExpansion, out var leftNullable);
        var rightExpression = Visit(binaryExpression.Right, allowOptimizedExpansion, out var rightNullable);

        var updated = binaryExpression.Update(leftExpression, rightExpression);

        if (UseRelationalNulls)
        {
            nullable = false;
            return updated;
        }

        nullable = leftNullable || rightNullable;
        return updated;
    }

    protected virtual SqlExpression VisitArrayIndex(
        DuckDBArrayIndexExpression arrayIndexExpression,
        bool allowOptimizedExpansion,
        out bool nullable)
    {
        var array = Visit(arrayIndexExpression.Array, allowOptimizedExpansion, out var arrayNullable);
        var index = Visit(arrayIndexExpression.Index, allowOptimizedExpansion, out var indexNullable);

        nullable = arrayNullable || indexNullable || arrayIndexExpression.IsNullable;

        return arrayIndexExpression.Update(array, index);
    }

    protected virtual SqlExpression VisitArraySlice(
        DuckDBArraySliceExpression arraySliceExpression,
        bool allowOptimizedExpansion,
        out bool nullable)
    {
        ArgumentNullException.ThrowIfNull(arraySliceExpression);

        var array = Visit(arraySliceExpression.Array, allowOptimizedExpansion, out var arrayNullable);
        var lowerBound = Visit(arraySliceExpression.LowerBound, allowOptimizedExpansion, out var lowerBoundNullable);
        var upperBound = Visit(arraySliceExpression.UpperBound, allowOptimizedExpansion, out var upperBoundNullable);

        nullable = arrayNullable || lowerBoundNullable || upperBoundNullable || arraySliceExpression.IsNullable;

        return arraySliceExpression.Update(array, lowerBound, upperBound);
    }

    protected virtual SqlExpression VisitRowValueExpression(
        DuckDBRowValueExpression rowValueExpression,
        bool allowOptimizedExpansion,
        out bool nullable)
    {
        SqlExpression[]? newValues = null;

        for (var i = 0; i < rowValueExpression.Values.Count; i++)
        {
            var value = rowValueExpression.Values[i];

            // Note that we disallow optimised expansion, since the null vs. false distinction does matter inside the row's values
            var newValue = Visit(value, allowOptimizedExpansion: false, out _);
            if (newValue != value && newValues is null)
            {
                newValues = new SqlExpression[rowValueExpression.Values.Count];
                for (var j = 0; j < i; j++)
                {
                    newValues[j] = rowValueExpression.Values[j];
                }
            }

            if (newValues is not null)
            {
                newValues[i] = newValue;
            }
        }

        // The row value expression itself can never be null
        nullable = false;

        return rowValueExpression.Update(newValues ?? rowValueExpression.Values);
    }
}
