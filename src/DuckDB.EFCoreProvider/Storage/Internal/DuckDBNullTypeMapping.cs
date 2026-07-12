using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     Type mapping used for untyped null values in raw SQL commands.
/// </summary>
internal sealed class DuckDBNullTypeMapping : RelationalTypeMapping
{
    public static DuckDBNullTypeMapping Default { get; } = new();

    private DuckDBNullTypeMapping()
        : base(new RelationalTypeMappingParameters(new CoreTypeMappingParameters(typeof(object)), "NULL"))
    {
    }

    private DuckDBNullTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DuckDBNullTypeMapping(parameters);

    protected override void ConfigureParameter(DbParameter parameter)
    {
        ((DuckDBParameter)parameter).RemoveDollarSign();
        base.ConfigureParameter(parameter);
    }
}