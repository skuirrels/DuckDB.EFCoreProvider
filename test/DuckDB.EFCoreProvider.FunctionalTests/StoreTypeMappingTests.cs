using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class StoreTypeMappingTests : DuckDBTestBase
{
    [ConditionalTheory]
    [InlineData("TINYINT", typeof(sbyte))]
    [InlineData("INT1", typeof(sbyte))]
    [InlineData("SMALLINT", typeof(short))]
    [InlineData("INT16", typeof(short))]
    [InlineData("INTEGER", typeof(int))]
    [InlineData("INT32", typeof(int))]
    [InlineData("BIGINT", typeof(long))]
    [InlineData("INT8", typeof(long))]
    [InlineData("INT64", typeof(long))]
    [InlineData("LONG", typeof(long))]
    [InlineData("UTINYINT", typeof(byte))]
    [InlineData("UINT8", typeof(byte))]
    [InlineData("USMALLINT", typeof(ushort))]
    [InlineData("UINT16", typeof(ushort))]
    [InlineData("UINTEGER", typeof(uint))]
    [InlineData("UINT32", typeof(uint))]
    [InlineData("UBIGINT", typeof(ulong))]
    [InlineData("UINT64", typeof(ulong))]
    public void Integer_store_types_and_aliases_preserve_signedness(string storeType, Type clrType)
    {
        using var context = new MappingContext(FileOptions<MappingContext>());

        var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping(storeType);

        Assert.NotNull(mapping);
        Assert.Equal(clrType, mapping.ClrType);
    }

    [ConditionalFact]
    public void Faceted_store_type_uses_its_canonical_mapping()
    {
        using var context = new MappingContext(FileOptions<MappingContext>());

        var mapping = context.GetService<IRelationalTypeMappingSource>().FindMapping("DECIMAL(12,2)");

        Assert.NotNull(mapping);
        Assert.Equal(typeof(decimal), mapping.ClrType);
        Assert.Equal(12, mapping.Precision);
        Assert.Equal(2, mapping.Scale);
    }

    [ConditionalTheory]
    [InlineData("BIGINT", DuckDBStoreTypeSupport.ScalarProperty, typeof(long))]
    [InlineData("TIMESTAMP_NS", DuckDBStoreTypeSupport.ScalarProperty, typeof(DateTime))]
    [InlineData("JSON", DuckDBStoreTypeSupport.ScalarProperty, typeof(string))]
    [InlineData("INTEGER[]", DuckDBStoreTypeSupport.ScalarProperty, typeof(List<int>))]
    [InlineData("INTEGER[3]", DuckDBStoreTypeSupport.RawReaderOnly, null)]
    [InlineData("STRUCT(name VARCHAR)", DuckDBStoreTypeSupport.ComplexProperty, null)]
    [InlineData("MAP(VARCHAR, INTEGER)", DuckDBStoreTypeSupport.RawReaderOnly, null)]
    [InlineData("HUGEINT", DuckDBStoreTypeSupport.RawReaderOnly, null)]
    [InlineData("NOT_A_DUCKDB_TYPE", DuckDBStoreTypeSupport.Unsupported, null)]
    public void Public_inspection_distinguishes_model_raw_and_unsupported_contracts(
        string storeType,
        DuckDBStoreTypeSupport support,
        Type? clrType)
    {
        using var context = new MappingContext(FileOptions<MappingContext>());

        var result = context.Database.GetDuckDBStoreTypeMapping(storeType);

        Assert.Equal(support, result.Support);
        Assert.Equal(clrType, result.ClrType);
    }

    [ConditionalFact]
    public void Fixed_array_inspection_reports_its_element_mapping()
    {
        using var context = new MappingContext(FileOptions<MappingContext>());

        var result = context.Database.GetDuckDBStoreTypeMapping("INTEGER[3]");

        Assert.Equal(DuckDBStoreTypeSupport.RawReaderOnly, result.Support);
        Assert.NotNull(result.ElementType);
        Assert.Equal(DuckDBStoreTypeSupport.ScalarProperty, result.ElementType.Support);
        Assert.Equal(typeof(int), result.ElementType.ClrType);
    }

    private sealed class MappingContext(DbContextOptions<MappingContext> options) : DbContext(options);
}