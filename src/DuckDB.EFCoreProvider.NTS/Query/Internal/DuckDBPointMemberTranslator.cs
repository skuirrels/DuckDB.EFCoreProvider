using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using NetTopologySuite.Geometries;
using System.Reflection;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBPointMemberTranslator : IMemberTranslator
{
    private static readonly IDictionary<MemberInfo, string> MemberToFunctionName = new Dictionary<MemberInfo, string>
    {
        { typeof(Point).GetTypeInfo().GetRuntimeProperty(nameof(Point.M))!, "ST_M" },
        { typeof(Point).GetTypeInfo().GetRuntimeProperty(nameof(Point.X))!, "ST_X" },
        { typeof(Point).GetTypeInfo().GetRuntimeProperty(nameof(Point.Y))!, "ST_Y" },
        { typeof(Point).GetTypeInfo().GetRuntimeProperty(nameof(Point.Z))!, "ST_Z" }
    };

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBPointMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
        => _sqlExpressionFactory = sqlExpressionFactory;

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        => MemberToFunctionName.TryGetValue(member, out var functionName)
            ? _sqlExpressionFactory.Function(
                functionName,
                [DuckDBSpatialHelpers.AsGeometry(instance!, _sqlExpressionFactory)],
                nullable: true,
                argumentsPropagateNullability: [true],
                returnType)
            : null;
}