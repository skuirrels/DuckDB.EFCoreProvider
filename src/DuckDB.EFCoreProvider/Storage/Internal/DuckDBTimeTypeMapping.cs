using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Storage.ValueConverters;
using DuckDB.NET.Data;
using DuckDB.NET.Native;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
public class DuckDBTimeTypeMapping : RelationalTypeMapping
{
    private static readonly MethodInfo GetTimeOnly = typeof(DuckDBDataReader)
        .GetMethod(nameof(DuckDBDataReader.GetFieldValue), 1, [typeof(int)])!
        .MakeGenericMethod(typeof(TimeOnly));

    private static readonly MethodInfo GetTimeSpan = typeof(DuckDBDataReader)
        .GetMethod(nameof(DuckDBDataReader.GetFieldValue), 1, [typeof(int)])!
        .MakeGenericMethod(typeof(TimeSpan));

    // Keyed by store-type name rather than DuckDBType because the DuckDBType enum does not contain TIME_NS.
    private static readonly IReadOnlyDictionary<string, string> Formats = new Dictionary<string, string>
    {
        ["TIME"] = @"TIME '{0:HH\:mm\:ss\.FFFFFFF}'",
        ["TIMETZ"] = @"TIMETZ '{0:HH\:mm\:ss\.FFFFFFF}'",
        ["TIME_NS"] = @"'{0:HH\:mm\:ss\.FFFFFFF}00'::TIME_NS"
    };

    internal static readonly RelationalTypeMapping Time = new DuckDBTimeTypeMapping(
        "TIME",
        typeof(TimeOnly),
        System.Data.DbType.Time,
        JsonTimeOnlyReaderWriter.Instance,
        null,
        DuckDBType.Time);

    internal static readonly RelationalTypeMapping TimeTz = new DuckDBTimeTypeMapping(
        "TIMETZ",
        typeof(TimeOnly),
        System.Data.DbType.Time,
        JsonTimeOnlyReaderWriter.Instance,
        null,
        DuckDBType.TimeTz);

    internal static readonly RelationalTypeMapping TimeNs = new DuckDBTimeTypeMapping(
        "TIME_NS",
        typeof(TimeOnly),
        System.Data.DbType.Time,
        JsonTimeSpanReaderWriter.Instance,
        null,
        DuckDBType.Time);

    internal static readonly RelationalTypeMapping TimeSpan = new DuckDBTimeTypeMapping(
        "TIME",
        typeof(TimeOnly),
        System.Data.DbType.Time,
        JsonTimeSpanReaderWriter.Instance,
        DuckDBTimeSpanToTimeOnlyValueConverter.Instance,
        DuckDBType.Time);

    public DuckDBTimeTypeMapping(
        string storeType,
        Type clrType,
        DbType dbType,
        JsonValueReaderWriter jsonValueReaderWriter,
        ValueConverter? converter,
        DuckDBType duckDbType)
        : base(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(
                    clrType: clrType,
                    converter: converter,
                    jsonValueReaderWriter: jsonValueReaderWriter),
                storeType: storeType,
                dbType: dbType))
    {
        DuckDbType = duckDbType;
    }

    protected DuckDBTimeTypeMapping(RelationalTypeMappingParameters parameters, DuckDBType duckDbType)
        : base(parameters)
    {
        DuckDbType = duckDbType;
    }

    protected virtual DuckDBType DuckDbType { get; private set; }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new DuckDBTimeTypeMapping(parameters, DuckDbType);
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        ((DuckDBParameter)parameter).RemoveDollarSign();
        base.ConfigureParameter(parameter);
    }

    /// <inheritdoc />
    protected override string SqlLiteralFormatString
    {
        get
        {
            return Formats.TryGetValue(StoreType, out var format) ? format : base.SqlLiteralFormatString;
        }
    }
}
