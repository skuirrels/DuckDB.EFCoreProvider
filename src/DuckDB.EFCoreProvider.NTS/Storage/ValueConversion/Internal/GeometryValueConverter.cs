using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace DuckDB.EFCoreProvider.NTS.Storage.ValueConversion.Internal;

/// <summary>
/// Converts between <typeparamref name="TGeometry"/> and WKT <see cref="string"/>.
/// DuckDB stores geometry in its native GEOMETRY column type; WKT text is only the wire
/// format, because DuckDB.NET cannot read the native GEOMETRY value directly (type ID 40).
/// Reads arrive as WKT via <c>ST_AsWKT()</c> and writes are wrapped in <c>ST_GeomFromText()</c>.
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
