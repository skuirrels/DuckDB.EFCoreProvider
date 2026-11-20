using DuckDB.EFCoreProvider.Query.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.ExpressionTranslators.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBDateTimeMethodTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo AddYears = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddYears), [typeof(int)])!;
    private static readonly MethodInfo AddMonths = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMonths), [typeof(int)])!;
    private static readonly MethodInfo AddDays = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddDays), [typeof(double)])!;
    private static readonly MethodInfo AddHours = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddHours), [typeof(double)])!;
    private static readonly MethodInfo AddMinutes = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMinutes), [typeof(double)])!;
    private static readonly MethodInfo AddSeconds = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddSeconds), [typeof(double)])!;
    private static readonly MethodInfo AddMilliseconds = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMilliseconds), [typeof(double)])!;
    private static readonly MethodInfo AddMicroseconds = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddMicroseconds), [typeof(double)])!;
    private static readonly MethodInfo AddTicks = typeof(DateTime).GetRuntimeMethod(nameof(DateTime.AddTicks), [typeof(long)])!;

    private readonly DuckDBSqlExpressionFactory _sqlExpressionFactory;

    public DuckDBDateTimeMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = (DuckDBSqlExpressionFactory)sqlExpressionFactory;
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method == AddYears)
        {
            return _sqlExpressionFactory.AddYears(instance!, arguments[0], typeof(DateTime));
        }

        if (method == AddMonths)
        {
            return _sqlExpressionFactory.AddMonths(instance!, arguments[0], typeof(DateTime));
        }

        if (method == AddDays)
        {
            return _sqlExpressionFactory.AddDays(instance!, arguments[0], typeof(DateTime));
        }

        if (method == AddHours)
        {
            return _sqlExpressionFactory.AddHours(instance!, arguments[0], typeof(DateTime));
        }

        if (method == AddMinutes)
        {
            return _sqlExpressionFactory.AddMinutes(instance!, arguments[0], typeof(DateTime));
        }

        if (method == AddSeconds)
        {
            return _sqlExpressionFactory.AddSeconds(instance!, arguments[0], typeof(DateTime));
        }

        if (method == AddMilliseconds)
        {
            return _sqlExpressionFactory.AddMilliseconds(instance!, arguments[0], typeof(DateTime));
        }

        if (method == AddMicroseconds)
        {
            return _sqlExpressionFactory.AddMicroseconds(instance!, arguments[0], typeof(DateTime));
        }

        if (method == AddTicks)
        {
            return _sqlExpressionFactory.AddTicks(instance!, arguments[0], typeof(DateTime));
        }

        return null;
    }
}