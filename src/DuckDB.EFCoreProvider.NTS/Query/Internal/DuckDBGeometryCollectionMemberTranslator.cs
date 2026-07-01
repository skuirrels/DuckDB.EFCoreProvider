using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using NetTopologySuite.Geometries;
using System.Reflection;

namespace DuckDB.EFCoreProvider.NTS.Query.Internal;

public class DuckDBGeometryCollectionMemberTranslator : IMemberTranslator
{
    private static readonly MemberInfo Count
        = typeof(GeometryCollection).GetTypeInfo().GetRuntimeProperty(nameof(GeometryCollection.Count))!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBGeometryCollectionMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
        => _sqlExpressionFactory = sqlExpressionFactory;

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        => Equals(member, Count)
            ? _sqlExpressionFactory.Function(
                "ST_NumGeometries",
                [DuckDBSpatialHelpers.AsGeometry(instance!, _sqlExpressionFactory)],
                nullable: true,
                argumentsPropagateNullability: [true],
                returnType)
            : null;
}