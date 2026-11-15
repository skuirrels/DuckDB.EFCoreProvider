using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Storage.ValueConverters;
using DuckDB.NET.Native;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
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
    private static readonly MethodInfo GetTimeOnly = typeof(DbDataReader)
        .GetMethod(nameof(DbDataReader.GetFieldValue), 1, [typeof(int)])!
        .MakeGenericMethod(typeof(TimeOnly));

    private static readonly MethodInfo GetTimeSpan = typeof(DbDataReader)
        .GetMethod(nameof(DbDataReader.GetFieldValue), 1, [typeof(int)])!
        .MakeGenericMethod(typeof(TimeSpan));

    private static readonly MethodInfo GetTimeTz = typeof(DbDataReader)
        .GetMethod(nameof(DbDataReader.GetFieldValue), 1, [typeof(int)])!
        .MakeGenericMethod(typeof(DuckDBTimeTz));

    private static readonly MethodInfo ConvertTimeTzToTimeOnlyMethod = typeof(DuckDBTimeTypeMapping).GetMethod(
        nameof(ConvertTimeTzToTimeOnly),
        BindingFlags.NonPublic | BindingFlags.Static)!;

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

    /// <summary>
    ///     The <c>TIME</c> mapping that compiled models clone. The clone takes its DuckDB type from the store type.
    /// </summary>
    public static DuckDBTimeTypeMapping Default => (DuckDBTimeTypeMapping)Time;

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
        return new DuckDBTimeTypeMapping(parameters, ResolveDuckDbType(parameters.StoreType, DuckDbType));
    }

    // A compiled model rebuilds a TIMETZ mapping by cloning Default with the new store type.
    private static DuckDBType ResolveDuckDbType(string storeType, DuckDBType fallback)
        => storeType.ToUpperInvariant() switch
        {
            "TIMETZ" or "TIME WITH TIME ZONE" => DuckDBType.TimeTz,
            "TIME" or "TIME WITHOUT TIME ZONE" or "TIME_NS" => DuckDBType.Time,
            _ => fallback
        };

    /// <inheritdoc />
    public override MethodInfo GetDataReaderMethod()
        => DuckDbType == DuckDBType.TimeTz ? GetTimeTz : base.GetDataReaderMethod();

    // DuckDB.NET reads TIMETZ as DuckDBTimeTz, which does not convert to TimeOnly. The value keeps its clock time
    // and drops the offset, because TimeOnly has nowhere to hold it.
    /// <inheritdoc />
    public override Expression CustomizeDataReaderExpression(Expression expression)
        => expression.Type == typeof(DuckDBTimeTz)
            ? Expression.Call(ConvertTimeTzToTimeOnlyMethod, expression)
            : expression;

    private static TimeOnly ConvertTimeTzToTimeOnly(DuckDBTimeTz value)
        => value.Time;

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        parameter.ConfigureNameAndMetadata(this);
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