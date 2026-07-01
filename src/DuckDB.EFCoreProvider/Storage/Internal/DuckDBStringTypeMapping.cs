using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Json;
using System.Data;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBStringTypeMapping : StringTypeMapping
{
    public static new DuckDBStringTypeMapping Default { get; } = new(DuckDBTypeMappingSource.VarCharTypeName);

    public DuckDBStringTypeMapping(
        string storeType,
        DbType? dbType = null,
        bool unicode = false,
        int? size = null)
        : base(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(
                    typeof(string), jsonValueReaderWriter: JsonStringReaderWriter.Instance), storeType, StoreTypePostfix.None, dbType,
                unicode, size))
    {
    }

    protected DuckDBStringTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new DuckDBStringTypeMapping(parameters);
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        ((DuckDBParameter)parameter).RemoveDollarSign();
        base.ConfigureParameter(parameter);
    }
}
