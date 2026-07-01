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
public class DuckDBTimeSpanMemberTranslator : IMemberTranslator
{
    private static readonly MemberInfo Hour = typeof(TimeSpan).GetRuntimeProperty(nameof(TimeSpan.Hours))!;
    private static readonly MemberInfo Minute = typeof(TimeSpan).GetRuntimeProperty(nameof(TimeSpan.Minutes))!;
    private static readonly MemberInfo Second = typeof(TimeSpan).GetRuntimeProperty(nameof(TimeSpan.Seconds))!;
    private static readonly MemberInfo Microseconds = typeof(TimeSpan).GetRuntimeProperty(nameof(TimeSpan.Microseconds))!;
    
    private readonly DuckDBSqlExpressionFactory _sqlExpressionFactory;

    public DuckDBTimeSpanMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = (DuckDBSqlExpressionFactory)sqlExpressionFactory;
    }

    public SqlExpression? Translate(SqlExpression? instance, MemberInfo member, Type returnType, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (member == Hour)
        {
            return _sqlExpressionFactory.Hour(instance!);
        }

        if (member == Minute)
        {
            return _sqlExpressionFactory.Minute(instance!);
        }

        if (member == Second)
        {
            return _sqlExpressionFactory.Second(instance!);
        }

        if (member == Microseconds)
        {
            return _sqlExpressionFactory.MicrosecondComponent(instance!);
        }

        return null;
    }
}
