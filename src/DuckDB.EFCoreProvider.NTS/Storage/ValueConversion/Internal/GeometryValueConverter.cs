using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace DuckDB.EFCoreProvider.NTS.Storage.ValueConversion.Internal;

/// <summary>
/// Converts between <typeparamref name="TGeometry"/> and WKT <see cref="string"/>.
/// DuckDB stores geometry as VARCHAR (WKT text) because DuckDB.NET does not support
/// reading the native GEOMETRY type (type ID 40). Spatial functions receive a
/// VARCHAR column/parameter wrapped in <c>ST_GeomFromText()</c>.
/// </summary>
public class GeometryValueConverter<TGeometry> : ValueConverter<TGeometry, string>
    where TGeometry : Geometry
{
    // WKTWriter is stateless after construction – safe to share.
    // OutputOrdinates = XYZM preserves Z and M coordinates when present.
    private static readonly WKTWriter WktWriter = new() { OutputOrdinates = Ordinates.XYZM };

    public GeometryValueConverter(WKTReader reader)
        : base(
            g => WktWriter.Write(g),
            s => (TGeometry)reader.Read(s))
    {
    }
}
