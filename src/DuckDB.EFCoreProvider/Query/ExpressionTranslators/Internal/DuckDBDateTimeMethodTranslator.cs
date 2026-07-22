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

        return null;
    }
}