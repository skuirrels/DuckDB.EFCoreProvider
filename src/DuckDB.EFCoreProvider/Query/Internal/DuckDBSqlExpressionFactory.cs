using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBSqlExpressionFactory : SqlExpressionFactory
{
    private readonly DuckDBTypeMappingSource _typeMappingSource;
    private readonly RelationalTypeMapping _boolTypeMapping;

    public DuckDBSqlExpressionFactory(SqlExpressionFactoryDependencies dependencies) : base(dependencies)
    {
        _typeMappingSource = (DuckDBTypeMappingSource)dependencies.TypeMappingSource;
        _boolTypeMapping = _typeMappingSource.FindMapping(typeof(bool), dependencies.Model)!;
    }

    public virtual DuckDBAnyExpression Any(SqlExpression item, SqlExpression array)
    {
        return (DuckDBAnyExpression)ApplyDefaultTypeMapping(new DuckDBAnyExpression(item, array, null));
    }

    public virtual SqlExpression Year(SqlExpression expression)
    {
        return Function(
            name: "year",
            arguments: [expression],
            argumentsPropagateNullability: [true],
            nullable: true,
            returnType: typeof(int));
    }

    public virtual SqlExpression Month(SqlExpression expression)
    {
        return Function(
            name: "month",
            arguments: [expression],
            argumentsPropagateNullability: [true],
            nullable: true,
            returnType: typeof(int));
    }

    public virtual SqlExpression Day(SqlExpression expression)
    {
        return Function(
            name: "day",
            arguments: [expression],
            argumentsPropagateNullability: [true],
            nullable: true,
            returnType: typeof(int));
    }

    public virtual SqlExpression Hour(SqlExpression expression)
    {
        return Function(
            name: "hour",
            arguments: [expression],
            argumentsPropagateNullability: [true],
            nullable: true,
            returnType: typeof(int));
    }

    public virtual SqlExpression Minute(SqlExpression expression)
    {
        return Function(
            name: "minute",
            arguments: [expression],
            argumentsPropagateNullability: [true],
            nullable: true,
            returnType: typeof(int));
    }

    public virtual SqlExpression Second(SqlExpression expression)
    {
        return Function(
            name: "second",
            arguments: [expression],
            argumentsPropagateNullability: [true],
            nullable: true,
            returnType: typeof(int));
    }

    public virtual SqlExpression Millisecond(SqlExpression expression)
    {
        return Function(
            name: "millisecond",
            arguments: [expression],
            argumentsPropagateNullability: [true],
            nullable: true,
            returnType: typeof(int));
    }

    public virtual SqlExpression MicrosecondComponent(SqlExpression expression)
    {
        var microsecondTotal = Function(
            name: "microsecond",
            arguments: [expression],
            argumentsPropagateNullability: [true],
            nullable: true,
            returnType: typeof(int));
        return MakeBinary(ExpressionType.Modulo, microsecondTotal, Constant(1000), typeMapping: null)!;
    }

    public virtual SqlExpression AddYears(SqlExpression timestamp, SqlExpression years, Type returnType)
    {
        return DateAdd(timestamp, ToYears(years), returnType);
    }

    public virtual SqlExpression AddMonths(SqlExpression timestamp, SqlExpression months, Type returnType)
    {
        return DateAdd(timestamp, ToMonths(months), returnType);
    }

    public virtual SqlExpression AddDays(SqlExpression timestamp, SqlExpression days, Type returnType)
    {
        return DateAdd(
            timestamp,
            returnType == typeof(DateOnly) ? ToDays(days) : ToFractionalDays(days),
            returnType);
    }

    private SqlExpression DateAdd(SqlExpression timestamp, SqlExpression interval, Type returnType)
    {
        var dateAddReturnType = returnType == typeof(DateOnly) ? typeof(DateTime) : returnType;
        var result = Function(
            name: "date_add",
            arguments: [timestamp, interval],
            argumentsPropagateNullability: [true, true],
            nullable: true,
            returnType: dateAddReturnType);

        // DuckDB returns TIMESTAMP when an interval is added to a DATE. Convert the physical result back to DATE
        // so it remains aligned with the DateOnly type represented by EF Core's SQL tree.
        return returnType == typeof(DateOnly)
            ? Convert(result, returnType, _typeMappingSource.FindMapping(returnType))
            : result;
    }

    private SqlExpression ToFractionalDays(SqlExpression days)
    {
        return new SqlBinaryExpression(
            ExpressionType.Multiply,
            ApplyDefaultTypeMapping(days),
            Fragment("INTERVAL '1 day'"),
            typeof(TimeSpan),
            _typeMappingSource.FindMapping(typeof(TimeSpan)));
    }

    public virtual SqlExpression ToYears(SqlExpression years)
    {
        return Function(
            name: "to_years",
            arguments: [years],
            argumentsPropagateNullability: [true],
            nullable: true,
            returnType: typeof(TimeSpan));
    }

    public virtual SqlExpression ToMonths(SqlExpression months)
    {
        return Function(
            name: "to_months",
            arguments: [months],
            argumentsPropagateNullability: [true],
            nullable: true,
            returnType: typeof(TimeSpan));
    }

    public virtual SqlExpression ToDays(SqlExpression days)
    {
        return Function(
            name: "to_days",
            arguments: [days],
            argumentsPropagateNullability: [true],
            nullable: true,
            returnType: typeof(TimeSpan));
    }

    public virtual SqlExpression DateDiff(string unit, SqlExpression left, SqlExpression right)
    {
        return Convert(
            Function(
                name: "date_diff",
                arguments: [Constant(unit), left, right],
                argumentsPropagateNullability: [true, true, true],
                nullable: true,
                returnType: typeof(int)),
            typeof(double)
        );
    }

    public virtual DuckDBArrayIndexExpression ArrayIndex(
        SqlExpression array,
        SqlExpression index,
        bool nullable,
        RelationalTypeMapping? typeMapping = null)
    {
        if (!array.Type.TryGetElementType(out var elementType))
        {
            throw new ArgumentException("Array expression must be of an array or List<> type", nameof(array));
        }

        return (DuckDBArrayIndexExpression)ApplyTypeMapping(
            new DuckDBArrayIndexExpression(array, index, nullable, elementType, typeMapping: null),
            typeMapping);
    }

    public virtual DuckDBArraySliceExpression ArraySlice(
        SqlExpression array,
        SqlExpression? lowerBound,
        SqlExpression? upperBound,
        bool nullable,
        RelationalTypeMapping? typeMapping = null)
        => (DuckDBArraySliceExpression)ApplyTypeMapping(
            new DuckDBArraySliceExpression(array, lowerBound, upperBound, nullable, array.Type, typeMapping: null),
            typeMapping);

    /// <inheritdoc />
    [return: NotNullIfNotNull("sqlExpression")]
    public override SqlExpression? ApplyTypeMapping(SqlExpression? sqlExpression, RelationalTypeMapping? typeMapping)
    {
        if (sqlExpression is not null && sqlExpression.TypeMapping is null)
        {
            sqlExpression = sqlExpression switch
            {
                SqlBinaryExpression e => ApplyTypeMappingOnSqlBinary(e, typeMapping),

                DuckDBAnyExpression e => ApplyTypeMappingOnAny(e),
                DuckDBArrayIndexExpression e => ApplyTypeMappingOnArrayIndex(e, typeMapping),
                DuckDBArraySliceExpression e => ApplyTypeMappingOnArraySlice(e, typeMapping),
                DuckDBRowValueExpression e => ApplyTypeMappingOnRowValue(e, typeMapping),
                _ => base.ApplyTypeMapping(sqlExpression, typeMapping)
            };

            if (sqlExpression is SqlBinaryExpression
                {
                    OperatorType: ExpressionType.Divide,
                    TypeMapping: { } resultTypeMapping
                } division
                && division.Type.UnwrapNullableType() == typeof(decimal))
            {
                // DuckDB's division operator returns DOUBLE, including for DECIMAL operands. Cast the result to a
                // widened DECIMAL mapping so it materializes correctly without rounding before enclosing arithmetic.
                return Convert(
                    division,
                    division.Type,
                    CreateDecimalDivisionTypeMapping(division, resultTypeMapping));
            }

            return sqlExpression;
        }

        return base.ApplyTypeMapping(sqlExpression, typeMapping);
    }

    private static RelationalTypeMapping CreateDecimalDivisionTypeMapping(
        SqlBinaryExpression division,
        RelationalTypeMapping resultTypeMapping)
    {
        const int maxPrecision = 38;
        const int minimumScale = 6;

        var defaultMapping = DuckDBDecimalTypeMapping.Default;
        var defaultPrecision = defaultMapping.Precision!.Value;
        var defaultScale = defaultMapping.Scale!.Value;
        var leftPrecision = division.Left.TypeMapping?.Precision ?? resultTypeMapping.Precision ?? defaultPrecision;
        var leftScale = division.Left.TypeMapping?.Scale ?? resultTypeMapping.Scale ?? defaultScale;
        var rightPrecision = division.Right.TypeMapping?.Precision ?? resultTypeMapping.Precision ?? defaultPrecision;
        var rightScale = division.Right.TypeMapping?.Scale ?? resultTypeMapping.Scale ?? defaultScale;

        // Division can require more fractional digits than either operand. Retain enough integral digits for the
        // declared operand ranges, then use the remaining DuckDB DECIMAL width for fractional precision.
        var integralDigits = Math.Min(maxPrecision, Math.Max(0, leftPrecision - leftScale + rightScale));
        var desiredScale = Math.Max(minimumScale, leftScale + rightPrecision + 1);
        var scale = Math.Min(desiredScale, maxPrecision - integralDigits);
        var precision = Math.Max(1, integralDigits + scale);

        return new DuckDBDecimalTypeMapping(precision, scale);
    }

    private SqlBinaryExpression ApplyTypeMappingOnSqlBinary(SqlBinaryExpression binary, RelationalTypeMapping? typeMapping)
    {
        if (IsComparison(binary.OperatorType)
            && TryGetRowValueValues(binary.Left, out var leftValues)
            && TryGetRowValueValues(binary.Right, out var rightValues))
        {
            if (leftValues.Count != rightValues.Count)
            {
                throw new ArgumentException("Tuples are not the same length in row value comparison");
            }

            var count = leftValues.Count;
            var updatedLeftValues = new SqlExpression[count];
            var updatedRightValues = new SqlExpression[count];

            for (var i = 0; i < count; i++)
            {
                var updatedElementBinaryExpression = MakeBinary(binary.OperatorType, leftValues[i], rightValues[i], typeMapping: null)!;

                if (updatedElementBinaryExpression is not SqlBinaryExpression
                    {
                        Left: var updatedLeft,
                        Right: var updatedRight,
                        OperatorType: var updatedOperatorType
                    }
                    || updatedOperatorType != binary.OperatorType)
                {
                    throw new UnreachableException("MakeBinary modified binary expression type/operator when doing row value comparison");
                }

                updatedLeftValues[i] = updatedLeft;
                updatedRightValues[i] = updatedRight;
            }

            binary = new SqlBinaryExpression(
                binary.OperatorType,
                new DuckDBRowValueExpression(updatedLeftValues, binary.Left.Type),
                new DuckDBRowValueExpression(updatedRightValues, binary.Right.Type),
                binary.Type,
                binary.TypeMapping);
        }

        return (SqlBinaryExpression)base.ApplyTypeMapping(binary, typeMapping);

        static bool IsComparison(ExpressionType expressionType)
        {
            switch (expressionType)
            {
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.LessThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.LessThanOrEqual:
                    return true;
                default:
                    return false;
            }
        }

        bool TryGetRowValueValues(SqlExpression e, [NotNullWhen(true)] out IReadOnlyList<SqlExpression>? values)
        {
            switch (e)
            {
                case DuckDBRowValueExpression rowValueExpression:
                    values = rowValueExpression.Values;
                    return true;

                case SqlConstantExpression { Value: ITuple constantTuple }:
                    var v = new SqlExpression[constantTuple.Length];

                    for (var i = 0; i < v.Length; i++)
                    {
                        v[i] = Constant(constantTuple[i], typeof(object));
                    }

                    values = v;
                    return true;

                default:
                    values = null;
                    return false;
            }
        }
    }

    private SqlExpression ApplyTypeMappingOnRowValue(
        DuckDBRowValueExpression pgRowValueExpression,
        RelationalTypeMapping? typeMapping)
    {
        var updatedValues = new SqlExpression[pgRowValueExpression.Values.Count];

        for (var i = 0; i < updatedValues.Length; i++)
        {
            updatedValues[i] = ApplyDefaultTypeMapping(pgRowValueExpression.Values[i]);
        }

        return new DuckDBRowValueExpression(updatedValues, pgRowValueExpression.Type, typeMapping);
    }

    private SqlExpression ApplyTypeMappingOnAny(DuckDBAnyExpression duckDbAnyExpression)
    {
        var (item, array) = ApplyTypeMappingsOnItemAndArray(duckDbAnyExpression.Item, duckDbAnyExpression.Array);
        return new DuckDBAnyExpression(item, array, _boolTypeMapping);
    }

    private SqlExpression ApplyTypeMappingOnArrayIndex(
        DuckDBArrayIndexExpression arrayIndexExpression,
        RelationalTypeMapping? typeMapping)
    {
        var (_, array) = typeMapping is not null
            ? ApplyTypeMappingsOnItemAndArray(Constant(null, typeMapping.ClrType, typeMapping), arrayIndexExpression.Array)
            : (null, ApplyDefaultTypeMapping(arrayIndexExpression.Array));

        return new DuckDBArrayIndexExpression(
            array,
            ApplyDefaultTypeMapping(arrayIndexExpression.Index),
            arrayIndexExpression.IsNullable,
            arrayIndexExpression.Type,
            arrayIndexExpression.Array.TypeMapping is DuckDBArrayTypeMapping arrayMapping
                ? arrayMapping.ElementTypeMapping
                : typeMapping
                  ?? Dependencies.TypeMappingSource.FindMapping(arrayIndexExpression.Type, Dependencies.Model));
    }

    private SqlExpression ApplyTypeMappingOnArraySlice(
        DuckDBArraySliceExpression slice,
        RelationalTypeMapping? typeMapping)
    {
        var array = ApplyTypeMapping(slice.Array, typeMapping);

        return new DuckDBArraySliceExpression(
            array,
            slice.LowerBound is null ? null : ApplyDefaultTypeMapping(slice.LowerBound),
            slice.UpperBound is null ? null : ApplyDefaultTypeMapping(slice.UpperBound),
            slice.IsNullable,
            slice.Type,
            array.TypeMapping);
    }

    internal (SqlExpression, SqlExpression) ApplyTypeMappingsOnItemAndArray(SqlExpression itemExpression, SqlExpression arrayExpression)
    {
        var arrayMapping = arrayExpression.TypeMapping;

        var itemMapping =
            itemExpression.TypeMapping
            ?? (itemExpression is SqlUnaryExpression { OperatorType: ExpressionType.Convert } unary && unary.Type == typeof(object)
                ? unary.Operand.TypeMapping
                : null)
            ?? (RelationalTypeMapping?)arrayMapping?.ElementTypeMapping
            ?? Dependencies.TypeMappingSource.FindMapping(itemExpression.Type, Dependencies.Model);

        if (itemMapping is null)
        {
            throw new InvalidOperationException("Couldn't find element type mapping when applying item/array mappings");
        }

        if (arrayMapping is null)
        {
            if (itemMapping.Converter is not null)
            {
                arrayMapping = Dependencies.TypeMappingSource.FindMapping(arrayExpression.Type, Dependencies.Model, itemMapping);
            }
            else
            {
                arrayMapping = arrayExpression.Type.TryGetSequenceType() == typeof(object)
                    ? Dependencies.TypeMappingSource.FindMapping(itemMapping.StoreType + "[]")
                    : Dependencies.TypeMappingSource.FindMapping(arrayExpression.Type, itemMapping.StoreType + "[]");
            }

            if (arrayMapping is null)
            {
                throw new InvalidOperationException("Couldn't find array type mapping when applying item/array mappings");
            }
        }

        return (ApplyTypeMapping(itemExpression, itemMapping), ApplyTypeMapping(arrayExpression, arrayMapping));
    }
}