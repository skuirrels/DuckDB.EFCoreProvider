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
public class DuckDBDateOnlyMemberTranslator : IMemberTranslator
{
    private static readonly MemberInfo Year = typeof(DateOnly).GetProperty(nameof(DateOnly.Year))!;
    private static readonly MemberInfo Month = typeof(DateOnly).GetProperty(nameof(DateOnly.Month))!;
    private static readonly MemberInfo Day = typeof(DateOnly).GetProperty(nameof(DateOnly.Day))!;
    private static readonly MemberInfo DayOfWeek = typeof(DateOnly).GetProperty(nameof(DateOnly.DayOfWeek))!;
    private static readonly MemberInfo DayOfYear = typeof(DateOnly).GetProperty(nameof(DateOnly.DayOfYear))!;
    private static readonly MemberInfo DayNumber = typeof(DateOnly).GetProperty(nameof(DateOnly.DayNumber))!;

    private readonly DuckDBSqlExpressionFactory _sqlExpressionFactory;

    public DuckDBDateOnlyMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = (DuckDBSqlExpressionFactory)sqlExpressionFactory;
    }

    public SqlExpression? Translate(SqlExpression? instance, MemberInfo member, Type returnType, IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (member == Year)
        {
            return _sqlExpressionFactory.Year(instance!);
        }

        if (member == Month)
        {
            return _sqlExpressionFactory.Month(instance!);
        }

        if (member == Day)
        {
            return _sqlExpressionFactory.Day(instance!);
        }

        if (member == DayOfWeek)
        {
            return _sqlExpressionFactory.Function(
                name: "dayofweek",
                arguments: [instance!],
                argumentsPropagateNullability: [true],
                nullable: true,
                returnType: typeof(int));
        }

        if (member == DayOfYear)
        {
            return _sqlExpressionFactory.Function(
                name: "dayofyear",
                arguments: [instance!],
                argumentsPropagateNullability: [true],
                nullable: true,
                returnType: typeof(int));
        }

        if (member == DayNumber)
        {
            return _sqlExpressionFactory.Function(
                name: "date_diff",
                arguments:
                [
                    _sqlExpressionFactory.Constant("day"),
                    _sqlExpressionFactory.Constant(DateOnly.MinValue),
                    instance!
                ],
                argumentsPropagateNullability: [true, true, true],
                nullable: true,
                returnType: typeof(int));
        }

        return null;
    }
}
