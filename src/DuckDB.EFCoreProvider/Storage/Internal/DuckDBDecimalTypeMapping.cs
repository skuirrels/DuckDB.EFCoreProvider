using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBDecimalTypeMapping : DecimalTypeMapping
{
    private const int DefaultPrecision = 18;
    private const int DefaultScale = 3;

    public static new readonly DuckDBDecimalTypeMapping Default = new(DefaultPrecision, DefaultScale);

    public DuckDBDecimalTypeMapping(int? precision = null, int? scale = null)
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                clrType: typeof(decimal),
                jsonValueReaderWriter: JsonDecimalReaderWriter.Instance),
            storeType: "DECIMAL",
            StoreTypePostfix.PrecisionAndScale,
            System.Data.DbType.Decimal,
            precision: precision ?? DefaultPrecision,
            scale: scale ?? DefaultScale))
    {
    }

    protected DuckDBDecimalTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new DuckDBDecimalTypeMapping(parameters);
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        ((DuckDBParameter)parameter).RemoveDollarSign();
        base.ConfigureParameter(parameter);
    }
}
