using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.NET.Data;
using DuckDB.NET.Native;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using System.Data;
using System.Data.Common;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBTimestampTypeMapping : RelationalTypeMapping
{
    private static readonly MethodInfo GetDateTime = typeof(DuckDBDataReader)
        .GetRuntimeMethod(nameof(DbDataReader.GetDateTime), [typeof(int)])!;

    private static readonly MethodInfo GetDateTimeOffset = typeof(DuckDBDataReader)
        .GetMethod(nameof(DuckDBDataReader.GetFieldValue), 1, [typeof(int)])!
        .MakeGenericMethod(typeof(DateTimeOffset));

    private static readonly Dictionary<DuckDBType, string> Formats = new()
    {
        [DuckDBType.TimestampNs] = @"TIMESTAMP_NS '{0:yyyy-MM-dd HH\:mm\:ss.fffffff}'",
        [DuckDBType.Timestamp] = @"TIMESTAMP '{0:yyyy-MM-dd HH\:mm\:ss.ffffff}'",
        [DuckDBType.TimestampMs] = @"TIMESTAMP_MS '{0:yyyy-MM-dd HH\:mm\:ss.fff}'",
        [DuckDBType.TimestampS] = @"TIMESTAMP_S '{0:yyyy-MM-dd HH\:mm\:ss}'",
        [DuckDBType.TimestampTz] = @"TIMESTAMPTZ '{0:yyyy-MM-dd HH\:mm\:ss.fffffffzzz}'"
    };

    public static readonly DuckDBTimestampTypeMapping TimestampNs = new(
        typeof(DateTime),
        "TIMESTAMP_NS",
        System.Data.DbType.DateTime,
        JsonDateTimeReaderWriter.Instance,
        DuckDBType.TimestampNs);

    public static readonly DuckDBTimestampTypeMapping Timestamp = new(
        typeof(DateTime),
        "TIMESTAMP",
        System.Data.DbType.DateTime,
        JsonDateTimeReaderWriter.Instance,
        DuckDBType.Timestamp);

    public static readonly DuckDBTimestampTypeMapping TimestampMs = new(
        typeof(DateTime),
        "TIMESTAMP_MS",
        System.Data.DbType.DateTime,
        JsonDateTimeReaderWriter.Instance,
        DuckDBType.TimestampMs);

    public static readonly DuckDBTimestampTypeMapping TimestampS = new(
        typeof(DateTime),
        "TIMESTAMP_S",
        System.Data.DbType.DateTime,
        JsonDateTimeReaderWriter.Instance,
        DuckDBType.TimestampS);

    public static readonly DuckDBTimestampTypeMapping TimestampTz = new(
        typeof(DateTimeOffset),
        "TIMESTAMPTZ",
        System.Data.DbType.DateTimeOffset,
        JsonDateTimeOffsetReaderWriter.Instance,
        DuckDBType.TimestampTz);

    public DuckDBTimestampTypeMapping(
        Type clrType,
        string storeType,
        DbType dbType,
        JsonValueReaderWriter jsonValueReaderWriter,
        DuckDBType duckDbType)
        : base(
            storeType: storeType,
            clrType: clrType,
            dbType: dbType,
            jsonValueReaderWriter: jsonValueReaderWriter)
    {
        DuckDbType = duckDbType;
    }

    protected DuckDBTimestampTypeMapping(RelationalTypeMappingParameters parameters, DuckDBType duckDbType)
        : base(parameters)
    {
        DuckDbType = duckDbType;
    }

    protected virtual DuckDBType DuckDbType { get; private set; }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new DuckDBTimestampTypeMapping(parameters, DuckDbType);
    }

    /// <inheritdoc />
    public override MethodInfo GetDataReaderMethod()
    {
        return DbType switch
        {
            System.Data.DbType.DateTime => GetDateTime,
            System.Data.DbType.DateTimeOffset => GetDateTimeOffset,
            _ => base.GetDataReaderMethod()
        };
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        ((DuckDBParameter)parameter).RemoveDollarSign();
        base.ConfigureParameter(parameter);
    }

    /// <inheritdoc />
    protected override string SqlLiteralFormatString => Formats[DuckDbType];
}
