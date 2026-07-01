using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace DuckDB.EFCoreProvider.NTS.Storage.Json;

/// <summary>
///     Reads and writes JSON using the well-known-text format for <see cref="Geometry" /> values.
/// </summary>
public sealed class DuckDBJsonGeometryWktReaderWriter : JsonValueReaderWriter<Geometry>
{
    private static readonly WKTReader WktReader = new();

    private static readonly PropertyInfo InstanceProperty =
        typeof(DuckDBJsonGeometryWktReaderWriter).GetProperty(nameof(Instance))!;

    /// <summary>The singleton instance of this stateless reader/writer.</summary>
    public static DuckDBJsonGeometryWktReaderWriter Instance { get; } = new();

    private DuckDBJsonGeometryWktReaderWriter()
    {
    }

    /// <inheritdoc />
    public override Geometry FromJsonTyped(ref Utf8JsonReaderManager manager, object? existingObject = null)
        => WktReader.Read(manager.CurrentReader.GetString());

    /// <inheritdoc />
    public override void ToJsonTyped(Utf8JsonWriter writer, Geometry value)
        => writer.WriteStringValue(value.ToText());

    /// <inheritdoc />
    public override Expression ConstructorExpression
        => Expression.Property(null, InstanceProperty);
}

