using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Storage;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace DuckDB.EFCoreProvider.NTS.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBNetTopologySuiteTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    // All geometry types map to DuckDB's native GEOMETRY column type.
    // Reading is done via ST_AsWKT() projection (see DuckDBNtsQuerySqlGenerator);
    // writing uses a VARCHAR parameter with ST_GeomFromText() wrapping (DuckDB auto-accepts it).
    private static readonly Dictionary<string, Type> StoreTypeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        { "GEOMETRY", typeof(Geometry) },
        { "GEOMETRYZ", typeof(Geometry) },
        { "GEOMETRYM", typeof(Geometry) },
        { "GEOMETRYZM", typeof(Geometry) },
        { "GEOMETRYCOLLECTION", typeof(GeometryCollection) },
        { "GEOMETRYCOLLECTIONZ", typeof(GeometryCollection) },
        { "GEOMETRYCOLLECTIONM", typeof(GeometryCollection) },
        { "GEOMETRYCOLLECTIONZM", typeof(GeometryCollection) },
        { "LINESTRING", typeof(LineString) },
        { "LINESTRINGZ", typeof(LineString) },
        { "LINESTRINGM", typeof(LineString) },
        { "LINESTRINGZM", typeof(LineString) },
        { "MULTILINESTRING", typeof(MultiLineString) },
        { "MULTILINESTRINGZ", typeof(MultiLineString) },
        { "MULTILINESTRINGM", typeof(MultiLineString) },
        { "MULTILINESTRINGZM", typeof(MultiLineString) },
        { "MULTIPOINT", typeof(MultiPoint) },
        { "MULTIPOINTZ", typeof(MultiPoint) },
        { "MULTIPOINTM", typeof(MultiPoint) },
        { "MULTIPOINTZM", typeof(MultiPoint) },
        { "MULTIPOLYGON", typeof(MultiPolygon) },
        { "MULTIPOLYGONZ", typeof(MultiPolygon) },
        { "MULTIPOLYGONM", typeof(MultiPolygon) },
        { "MULTIPOLYGONZM", typeof(MultiPolygon) },
        { "POINT", typeof(Point) },
        { "POINTZ", typeof(Point) },
        { "POINTM", typeof(Point) },
        { "POINTZM", typeof(Point) },
        { "POLYGON", typeof(Polygon) },
        { "POLYGONZ", typeof(Polygon) },
        { "POLYGONM", typeof(Polygon) },
        { "POLYGONZM", typeof(Polygon) }
    };

    private readonly NtsGeometryServices _geometryServices;

    public DuckDBNetTopologySuiteTypeMappingSourcePlugin(NtsGeometryServices geometryServices)
        => _geometryServices = geometryServices;

    public RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType;
        var storeTypeName = mappingInfo.StoreTypeName;
        Type? defaultClrType = null;

        if ((clrType == null || !TryGetDefaultStoreType(clrType, out _))
            && (storeTypeName == null || !StoreTypeMappings.TryGetValue(storeTypeName, out defaultClrType)))
        {
            return null;
        }

        // Always use GEOMETRY as the physical column type (native DuckDB type).
        // SELECT projections are wrapped with ST_AsWKT() by DuckDBNtsQuerySqlGenerator;
        // INSERT/UPDATE parameters carry WKT text which DuckDB auto-casts to GEOMETRY.
        return (RelationalTypeMapping)Activator.CreateInstance(
            typeof(DuckDBGeometryTypeMapping<>).MakeGenericType(clrType ?? defaultClrType ?? typeof(Geometry)),
            _geometryServices,
            "GEOMETRY")!;
    }

    private static bool TryGetDefaultStoreType(Type type, [NotNullWhen(true)] out string? defaultStoreType)
    {
        defaultStoreType = typeof(Geometry).IsAssignableFrom(type) ? "GEOMETRY" : null;
        return defaultStoreType != null;
    }
}