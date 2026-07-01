using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class JsonTypesDuckDBTest : JsonTypesRelationalTestBase
{
    public JsonTypesDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_array_of_array_of_array_of_int_JSON_values()
    {
        await base.Can_read_write_array_of_array_of_array_of_int_JSON_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_array_of_list_of_array_of_IPAddress_JSON_values()
    {
        await base.Can_read_write_array_of_list_of_array_of_IPAddress_JSON_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_array_of_list_of_array_of_string_JSON_values()
    {
        await base.Can_read_write_array_of_list_of_array_of_string_JSON_values();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_array_of_list_of_binary_JSON_values(string expected)
    {
        await base.Can_read_write_array_of_list_of_binary_JSON_values(expected);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_array_of_list_of_GUID_JSON_values(string expected)
    {
        await base.Can_read_write_array_of_list_of_GUID_JSON_values(expected);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_array_of_list_of_int_JSON_values()
    {
        await base.Can_read_write_array_of_list_of_int_JSON_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_array_of_list_of_IPAddress_JSON_values()
    {
        await base.Can_read_write_array_of_list_of_IPAddress_JSON_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_array_of_list_of_string_JSON_values()
    {
        await base.Can_read_write_array_of_list_of_string_JSON_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_array_of_list_of_ulong_JSON_values()
    {
        await base.Can_read_write_array_of_list_of_ulong_JSON_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_collection_of_nullable_ulong_enum_JSON_values()
    {
        await base.Can_read_write_collection_of_nullable_ulong_enum_JSON_values();
    }

    public override Task Can_read_write_collection_of_ulong_enum_JSON_values()
        => Can_read_and_write_JSON_value<EnumU64CollectionType, List<EnumU64>>(
            nameof(EnumU64CollectionType.EnumU64),
            [
                EnumU64.Min,
                EnumU64.Max,
                EnumU64.Default,
                EnumU64.One,
                (EnumU64)8
            ],
            """{"Prop":[0,18446744073709551615,0,1,8]}""", // DuckDB supports UBIGINT natively, unlike SQL Server
            mappedCollection: true);

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_binary_JSON_values(string expected)
    {
        await base.Can_read_write_list_of_array_of_binary_JSON_values(expected);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_GUID_JSON_values(string expected)
    {
        await base.Can_read_write_list_of_array_of_GUID_JSON_values(expected);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_int_JSON_values()
    {
        await base.Can_read_write_list_of_array_of_int_JSON_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_list_of_IPAddress_JSON_values()
    {
        await base.Can_read_write_list_of_array_of_list_of_IPAddress_JSON_values();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_IPAddress_JSON_values()
    {
        await base.Can_read_write_list_of_array_of_IPAddress_JSON_values();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_list_of_array_of_binary_JSON_values(string expected)
    {
        await base.Can_read_write_list_of_array_of_list_of_array_of_binary_JSON_values(expected);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_list_of_string_JSON_values()
    {
        await base.Can_read_write_list_of_array_of_list_of_string_JSON_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_list_of_ulong_JSON_values()
    {
        await base.Can_read_write_list_of_array_of_list_of_ulong_JSON_values();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_nullable_GUID_JSON_values(string expected)
    {
        await base.Can_read_write_list_of_array_of_nullable_GUID_JSON_values(expected);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_nullable_int_JSON_values()
    {
        await base.Can_read_write_list_of_array_of_nullable_int_JSON_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_nullable_ulong_JSON_values()
    {
        await base.Can_read_write_list_of_array_of_nullable_ulong_JSON_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_string_JSON_values()
    {
        await base.Can_read_write_list_of_array_of_string_JSON_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_array_of_ulong_JSON_values()
    {
        await base.Can_read_write_list_of_array_of_ulong_JSON_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_list_of_list_of_list_of_int_JSON_values()
    {
        await base.Can_read_write_list_of_list_of_list_of_int_JSON_values();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_nullable_ulong_enum_JSON_values(object? value, string json)
    {
        await base.Can_read_write_nullable_ulong_enum_JSON_values(value, json);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_ulong_enum_JSON_values(EnumU64 value, string json)
    {
        await base.Can_read_write_ulong_enum_JSON_values(value, json);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_binary_JSON_values(string value, string json)
    {
        await base.Can_read_write_binary_JSON_values(value, json);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_collection_of_binary_JSON_values(string expected)
    {
        await base.Can_read_write_collection_of_binary_JSON_values(expected);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_collection_of_Guid_converted_to_bytes_JSON_values(string expected)
    {
        await base.Can_read_write_collection_of_Guid_converted_to_bytes_JSON_values(expected);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_collection_of_nullable_binary_JSON_values(string expected)
    {
        await base.Can_read_write_collection_of_nullable_binary_JSON_values(expected);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_read_write_nullable_binary_JSON_values(string? value, string json)
    {
        await base.Can_read_write_nullable_binary_JSON_values(value, json);
    }

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;
}
