using DuckDB.EFCoreProvider.Extensions.DbFunctionsExtensions;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Query.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Query.ExpressionTranslators.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBRowValueTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo GreaterThan =
        typeof(DuckDBDbFunctionsExtensions).GetRuntimeMethod(
            nameof(DuckDBDbFunctionsExtensions.GreaterThan),
            [typeof(DbFunctions), typeof(ITuple), typeof(ITuple)])!;

    private static readonly MethodInfo LessThan =
        typeof(DuckDBDbFunctionsExtensions).GetMethods()
            .Single(m => m.Name == nameof(DuckDBDbFunctionsExtensions.LessThan));

    private static readonly MethodInfo GreaterThanOrEqual =
        typeof(DuckDBDbFunctionsExtensions).GetMethods()
            .Single(m => m.Name == nameof(DuckDBDbFunctionsExtensions.GreaterThanOrEqual));

    private static readonly MethodInfo LessThanOrEqual =
        typeof(DuckDBDbFunctionsExtensions).GetMethods()
            .Single(m => m.Name == nameof(DuckDBDbFunctionsExtensions.LessThanOrEqual));

    private static readonly Dictionary<MethodInfo, ExpressionType> ComparisonMethods = new()
    {
        { GreaterThan, ExpressionType.GreaterThan },
        { LessThan, ExpressionType.LessThan },
        { GreaterThanOrEqual, ExpressionType.GreaterThanOrEqual },
        { LessThanOrEqual, ExpressionType.LessThanOrEqual }
    };

    private readonly DuckDBSqlExpressionFactory _sqlExpressionFactory;

    public DuckDBRowValueTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = (DuckDBSqlExpressionFactory)sqlExpressionFactory;
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        // Translate ValueTuple.Create
        if (method.DeclaringType == typeof(ValueTuple) && method is { IsStatic: true, Name: nameof(ValueTuple.Create) })
        {
            return new DuckDBRowValueExpression(arguments, method.ReturnType);
        }

        // Translate EF.Functions.GreaterThan and other comparisons
        if (method.DeclaringType != typeof(DuckDBDbFunctionsExtensions) || !ComparisonMethods.TryGetValue(method, out var expressionType))
        {
            return null;
        }

        var leftCount = arguments[1] is DuckDBRowValueExpression leftRowValue
            ? leftRowValue.Values.Count
            : arguments[1] is SqlConstantExpression { Value : ITuple leftTuple }
                ? (int?)leftTuple.Length
                : null;

        var rightCount = arguments[2] is DuckDBRowValueExpression rightRowValue
            ? rightRowValue.Values.Count
            : arguments[2] is SqlConstantExpression { Value : ITuple rightTuple }
                ? (int?)rightTuple.Length
                : null;

        if (leftCount is null || rightCount is null)
        {
            return null;
        }

        if (leftCount != rightCount)
        {
            throw new ArgumentException("Tuples are not the same length");
        }

        return _sqlExpressionFactory.MakeBinary(expressionType, arguments[1], arguments[2], typeMapping: null);
    }
}
