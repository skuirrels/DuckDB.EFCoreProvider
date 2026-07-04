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
public class DuckDBGeometryMemberTranslator : IMemberTranslator
{
    private static readonly IDictionary<MemberInfo, string> MemberToFunctionName = new Dictionary<MemberInfo, string>
    {
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.Area))!, "ST_Area" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.Boundary))!, "ST_Boundary" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.Centroid))!, "ST_Centroid" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.Dimension))!, "ST_Dimension" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.Envelope))!, "ST_Envelope" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.InteriorPoint))!, "ST_PointOnSurface" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.IsEmpty))!, "ST_IsEmpty" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.IsSimple))!, "ST_IsSimple" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.IsValid))!, "ST_IsValid" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.Length))!, "ST_Length" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.NumGeometries))!, "ST_NumGeometries" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.NumPoints))!, "ST_NPoints" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.PointOnSurface))!, "ST_PointOnSurface" },
        { typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.SRID))!, "ST_SRID" }
    };

    private static readonly MemberInfo GeometryType
        = typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.GeometryType))!;

    private static readonly MemberInfo OgcGeometryType
        = typeof(Geometry).GetTypeInfo().GetRuntimeProperty(nameof(Geometry.OgcGeometryType))!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBGeometryMemberTranslator(ISqlExpressionFactory sqlExpressionFactory)
        => _sqlExpressionFactory = sqlExpressionFactory;

    public virtual SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance == null)
        {
            return null;
        }

        if (MemberToFunctionName.TryGetValue(member, out var functionName))
        {
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

        if (Equals(member, GeometryType))
        {
            return _sqlExpressionFactory.Case(
                _sqlExpressionFactory.Function(
                    "ST_GeometryType",
                    [DuckDBSpatialHelpers.AsGeometry(instance, _sqlExpressionFactory)],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    returnType),
                [
                    new CaseWhenClause(_sqlExpressionFactory.Constant("POINT"), _sqlExpressionFactory.Constant("Point")),
                    new CaseWhenClause(_sqlExpressionFactory.Constant("LINESTRING"), _sqlExpressionFactory.Constant("LineString")),
                    new CaseWhenClause(_sqlExpressionFactory.Constant("POLYGON"), _sqlExpressionFactory.Constant("Polygon")),
                    new CaseWhenClause(_sqlExpressionFactory.Constant("MULTIPOINT"), _sqlExpressionFactory.Constant("MultiPoint")),
                    new CaseWhenClause(_sqlExpressionFactory.Constant("MULTILINESTRING"), _sqlExpressionFactory.Constant("MultiLineString")),
                    new CaseWhenClause(_sqlExpressionFactory.Constant("MULTIPOLYGON"), _sqlExpressionFactory.Constant("MultiPolygon")),
                    new CaseWhenClause(_sqlExpressionFactory.Constant("GEOMETRYCOLLECTION"), _sqlExpressionFactory.Constant("GeometryCollection"))
                ],
                null);
        }

        if (Equals(member, OgcGeometryType))
        {
            return _sqlExpressionFactory.Case(
                _sqlExpressionFactory.Function(
                    "ST_GeometryType",
                    [DuckDBSpatialHelpers.AsGeometry(instance, _sqlExpressionFactory)],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    typeof(string)),
                [
                    new CaseWhenClause(
                        _sqlExpressionFactory.Constant("POINT"),
                        _sqlExpressionFactory.Constant(NetTopologySuite.Geometries.OgcGeometryType.Point)),
                    new CaseWhenClause(
                        _sqlExpressionFactory.Constant("LINESTRING"),
                        _sqlExpressionFactory.Constant(NetTopologySuite.Geometries.OgcGeometryType.LineString)),
                    new CaseWhenClause(
                        _sqlExpressionFactory.Constant("POLYGON"),
                        _sqlExpressionFactory.Constant(NetTopologySuite.Geometries.OgcGeometryType.Polygon)),
                    new CaseWhenClause(
                        _sqlExpressionFactory.Constant("MULTIPOINT"),
                        _sqlExpressionFactory.Constant(NetTopologySuite.Geometries.OgcGeometryType.MultiPoint)),
                    new CaseWhenClause(
                        _sqlExpressionFactory.Constant("MULTILINESTRING"),
                        _sqlExpressionFactory.Constant(NetTopologySuite.Geometries.OgcGeometryType.MultiLineString)),
                    new CaseWhenClause(
                        _sqlExpressionFactory.Constant("MULTIPOLYGON"),
                        _sqlExpressionFactory.Constant(NetTopologySuite.Geometries.OgcGeometryType.MultiPolygon)),
                    new CaseWhenClause(
                        _sqlExpressionFactory.Constant("GEOMETRYCOLLECTION"),
                        _sqlExpressionFactory.Constant(NetTopologySuite.Geometries.OgcGeometryType.GeometryCollection))
                ],
                null);
        }

        return null;
    }
}