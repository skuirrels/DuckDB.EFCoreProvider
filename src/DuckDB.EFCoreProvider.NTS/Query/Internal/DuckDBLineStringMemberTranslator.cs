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
public class DuckDBLineStringMemberTranslator : IMemberTranslator
{
    private static readonly IDictionary<MemberInfo, string> MemberToFunctionName = new Dictionary<MemberInfo, string>
    {
        { typeof(LineString).GetTypeInfo().GetRuntimeProperty(nameof(LineString.Count))!, "ST_NumPoints" },
        { typeof(LineString).GetTypeInfo().GetRuntimeProperty(nameof(LineString.EndPoint))!, "ST_EndPoint" },
        { typeof(LineString).GetTypeInfo().GetRuntimeProperty(nameof(LineString.IsClosed))!, "ST_IsClosed" },
        { typeof(LineString).GetTypeInfo().GetRuntimeProperty(nameof(LineString.IsRing))!, "ST_IsRing" },
        { typeof(LineString).GetTypeInfo().GetRuntimeProperty(nameof(LineString.StartPoint))!, "ST_StartPoint" }
    };

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBLineStringMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
        => _sqlExpressionFactory = sqlExpressionFactory;

    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (!MemberToFunctionName.TryGetValue(member, out var functionName) || instance == null)
        {
            return null;
        }

        var geomInstance = DuckDBSpatialHelpers.AsGeometry(instance, _sqlExpressionFactory);
        return returnType == typeof(bool)
            ? _sqlExpressionFactory.Case(
                [
                    new CaseWhenClause(
                        _sqlExpressionFactory.IsNotNull(instance),
                        _sqlExpressionFactory.Function(
                            functionName,
                            [geomInstance],
                            nullable: false,
                            argumentsPropagateNullability: [false],
                            returnType))
                ],
                null)
            : _sqlExpressionFactory.Function(
                functionName,
                [geomInstance],
                nullable: true,
                argumentsPropagateNullability: [true],
                returnType);
    }
}