using DuckDB.NET.Native;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     <c>TIMESTAMPTZ</c> maps to <see cref="DateTimeOffset" />. It has its own type, so a compiled model that clones
///     <see cref="Default" /> keeps that CLR type rather than inheriting <see cref="DateTime" /> from
///     <see cref="DuckDBTimestampTypeMapping.Default" />.
/// </remarks>
public class DuckDBTimestampTzTypeMapping : DuckDBTimestampTypeMapping
{
    // Read through the base field so the instance exists whichever class is initialised first.
    public static new DuckDBTimestampTzTypeMapping Default => (DuckDBTimestampTzTypeMapping)TimestampTz;

    public DuckDBTimestampTzTypeMapping()
        : base(
            typeof(DateTimeOffset),
            "TIMESTAMPTZ",
            System.Data.DbType.DateTimeOffset,
            JsonDateTimeOffsetReaderWriter.Instance,
            DuckDBType.TimestampTz)
    {
    }

    protected DuckDBTimestampTzTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters, DuckDBType.TimestampTz)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DuckDBTimestampTzTypeMapping(parameters);
}