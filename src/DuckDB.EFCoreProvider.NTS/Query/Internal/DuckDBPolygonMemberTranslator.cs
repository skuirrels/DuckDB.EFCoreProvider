using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using NetTopologySuite.Geometries;
using System.Reflection;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

public class DuckDBPolygonMemberTranslator : IMemberTranslator
{
    private static readonly IDictionary<MemberInfo, string> MemberToFunctionName = new Dictionary<MemberInfo, string>
    {
        { typeof(Polygon).GetTypeInfo().GetRuntimeProperty(nameof(Polygon.ExteriorRing))!, "ST_ExteriorRing" },
        { typeof(Polygon).GetTypeInfo().GetRuntimeProperty(nameof(Polygon.NumInteriorRings))!, "ST_NumInteriorRings" }
    };

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBPolygonMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
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