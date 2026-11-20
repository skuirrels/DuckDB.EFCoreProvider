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
public class DuckDBDateTimeOffsetMemberTranslator : IMemberTranslator
{
    private static readonly MemberInfo Year = typeof(DateTimeOffset).GetRuntimeProperty(nameof(DateTimeOffset.Year))!;
    private static readonly MemberInfo Month = typeof(DateTimeOffset).GetRuntimeProperty(nameof(DateTimeOffset.Month))!;
    private static readonly MemberInfo Day = typeof(DateTimeOffset).GetRuntimeProperty(nameof(DateTimeOffset.Day))!;
    private static readonly MemberInfo Hour = typeof(DateTimeOffset).GetRuntimeProperty(nameof(DateTimeOffset.Hour))!;
    private static readonly MemberInfo Minute = typeof(DateTimeOffset).GetRuntimeProperty(nameof(DateTimeOffset.Minute))!;
    private static readonly MemberInfo Second = typeof(DateTimeOffset).GetRuntimeProperty(nameof(DateTimeOffset.Second))!;
    private static readonly MemberInfo DayOfWeek = typeof(DateTimeOffset).GetRuntimeProperty(nameof(DateTimeOffset.DayOfWeek))!;
    private static readonly MemberInfo UtcDateTime = typeof(DateTimeOffset).GetRuntimeProperty(nameof(DateTimeOffset.UtcDateTime))!;
    private static readonly MemberInfo LocalDateTime = typeof(DateTimeOffset).GetRuntimeProperty(nameof(DateTimeOffset.LocalDateTime))!;

    private readonly DuckDBSqlExpressionFactory _sqlExpressionFactory;

    public DuckDBDateTimeOffsetMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = (DuckDBSqlExpressionFactory)sqlExpressionFactory;
    }

    // TIMESTAMPTZ keeps the instant, not its offset, and materialises at +00:00. Read calendar and clock parts from
    // the UTC TIMESTAMP rather than in the session TimeZone, so they match a loaded value on every machine.
    private SqlExpression InUtc(SqlExpression instance)
        => _sqlExpressionFactory.AtUtc(instance, typeof(DateTime));

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (member == Year)
        {
            return _sqlExpressionFactory.Year(InUtc(instance!));
        }

        if (member == Month)
        {
            return _sqlExpressionFactory.Month(InUtc(instance!));
        }

        if (member == Day)
        {
            return _sqlExpressionFactory.Day(InUtc(instance!));
        }

        if (member == Hour)
        {
            return _sqlExpressionFactory.Hour(InUtc(instance!));
        }

        if (member == Minute)
        {
            return _sqlExpressionFactory.Minute(InUtc(instance!));
        }

        if (member == Second)
        {
            return _sqlExpressionFactory.Second(InUtc(instance!));
        }

        if (member == DayOfWeek)
        {
            return _sqlExpressionFactory.DayOfWeek(InUtc(instance!));
        }

        if (member == UtcDateTime)
        {
            return _sqlExpressionFactory.AtUtc(instance!, typeof(DateTime));
        }

        if (member == LocalDateTime)
        {
            // A TIMESTAMPTZ cast to TIMESTAMP takes the clock time in the session's TimeZone setting, which
            // DuckDB sets from the local machine by default.
            return _sqlExpressionFactory.Convert(instance!, typeof(DateTime));
        }

        // DateTime and the offset-based members need the original offset, which TIMESTAMPTZ does not store.
        return null;
    }
}