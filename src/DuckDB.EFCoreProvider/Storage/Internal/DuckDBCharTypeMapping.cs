using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Data.Common;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBCharTypeMapping : CharTypeMapping
{
    public DuckDBCharTypeMapping()
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                clrType: typeof(char),
                converter: new CharToStringConverter(),
                comparer: null,
                keyComparer: null,
                providerValueComparer: null,
                valueGeneratorFactory: null,
                elementMapping: null,
                jsonValueReaderWriter: new JsonConvertedValueReaderWriter<char, string>(
                    JsonStringReaderWriter.Instance, new CharToStringConverter())),
            "VARCHAR",
            StoreTypePostfix.Size,
            System.Data.DbType.StringFixedLength,
            unicode: true,
            size: 1,
            fixedLength: true))
    {
    }

    protected DuckDBCharTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
    {
    }

    /// <inheritdoc />
    public override MethodInfo GetDataReaderMethod()
    {
        return GetDataReaderMethod(typeof(string));
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new DuckDBCharTypeMapping(parameters);
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        ((DuckDBParameter)parameter).RemoveDollarSign();
        base.ConfigureParameter(parameter);
    }
}
