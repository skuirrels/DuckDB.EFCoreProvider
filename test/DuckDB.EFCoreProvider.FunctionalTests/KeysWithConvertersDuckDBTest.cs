using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class KeysWithConvertersDuckDBTest : KeysWithConvertersTestBase<KeysWithConvertersDuckDBTest.KeysWithConvertersDuckDBFixture>
{
    public KeysWithConvertersDuckDBTest(KeysWithConvertersDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_comparable_struct_binary_key_and_optional_dependents()
    {
        await base.Can_insert_and_read_back_with_comparable_struct_binary_key_and_optional_dependents();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_comparable_struct_binary_key_and_optional_dependents_with_shadow_FK()
    {
        await base.Can_insert_and_read_back_with_comparable_struct_binary_key_and_optional_dependents_with_shadow_FK();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_comparable_struct_binary_key_and_required_dependents()
    {
        await base.Can_insert_and_read_back_with_comparable_struct_binary_key_and_required_dependents();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_comparable_struct_binary_key_and_required_dependents_with_shadow_FK()
    {
        await base.Can_insert_and_read_back_with_comparable_struct_binary_key_and_required_dependents_with_shadow_FK();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_comparable_struct_key_and_required_dependents()
    {
        await base.Can_insert_and_read_back_with_comparable_struct_key_and_required_dependents();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_generic_comparable_struct_binary_key_and_optional_dependents()
    {
        await base.Can_insert_and_read_back_with_generic_comparable_struct_binary_key_and_optional_dependents();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_generic_comparable_struct_binary_key_and_optional_dependents_with_shadow_FK()
    {
        await base.Can_insert_and_read_back_with_generic_comparable_struct_binary_key_and_optional_dependents_with_shadow_FK();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_generic_comparable_struct_binary_key_and_required_dependents()
    {
        await base.Can_insert_and_read_back_with_generic_comparable_struct_binary_key_and_required_dependents();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_generic_comparable_struct_binary_key_and_required_dependents_with_shadow_FK()
    {
        await base.Can_insert_and_read_back_with_generic_comparable_struct_binary_key_and_required_dependents_with_shadow_FK();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_generic_comparable_struct_key_and_required_dependents()
    {
        await base.Can_insert_and_read_back_with_generic_comparable_struct_key_and_required_dependents();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_struct_binary_key_and_optional_dependents()
    {
        await base.Can_insert_and_read_back_with_struct_binary_key_and_optional_dependents();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_struct_binary_key_and_optional_dependents_with_shadow_FK()
    {
        await base.Can_insert_and_read_back_with_struct_binary_key_and_optional_dependents_with_shadow_FK();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_struct_binary_key_and_required_dependents()
    {
        await base.Can_insert_and_read_back_with_struct_binary_key_and_required_dependents();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_struct_binary_key_and_required_dependents_with_shadow_FK()
    {
        await base.Can_insert_and_read_back_with_struct_binary_key_and_required_dependents_with_shadow_FK();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_struct_key_and_required_dependents()
    {
        await base.Can_insert_and_read_back_with_struct_key_and_required_dependents();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_structural_struct_binary_key_and_optional_dependents()
    {
        await base.Can_insert_and_read_back_with_structural_struct_binary_key_and_optional_dependents();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_structural_struct_binary_key_and_optional_dependents_with_shadow_FK()
    {
        await base.Can_insert_and_read_back_with_structural_struct_binary_key_and_optional_dependents_with_shadow_FK();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_structural_struct_binary_key_and_required_dependents()
    {
        await base.Can_insert_and_read_back_with_structural_struct_binary_key_and_required_dependents();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_structural_struct_binary_key_and_required_dependents_with_shadow_FK()
    {
        await base.Can_insert_and_read_back_with_structural_struct_binary_key_and_required_dependents_with_shadow_FK();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_and_update_owned_entity_with_binary_struct_key()
    {
        await base.Can_query_and_update_owned_entity_with_binary_struct_key();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_and_update_owned_entity_with_comparable_bytes_struct_key()
    {
        await base.Can_query_and_update_owned_entity_with_comparable_bytes_struct_key();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_and_update_owned_entity_with_comparable_int_class_key()
    {
        await base.Can_query_and_update_owned_entity_with_comparable_int_class_key();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_and_update_owned_entity_with_comparable_int_struct_key()
    {
        await base.Can_query_and_update_owned_entity_with_comparable_int_struct_key();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_and_update_owned_entity_with_generic_comparable_bytes_struct_key()
    {
        await base.Can_query_and_update_owned_entity_with_generic_comparable_bytes_struct_key();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_and_update_owned_entity_with_generic_comparable_int_class_key()
    {
        await base.Can_query_and_update_owned_entity_with_generic_comparable_int_class_key();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_and_update_owned_entity_with_generic_comparable_int_struct_key()
    {
        await base.Can_query_and_update_owned_entity_with_generic_comparable_int_struct_key();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_and_update_owned_entity_with_int_bare_class_key()
    {
        await base.Can_query_and_update_owned_entity_with_int_bare_class_key();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_and_update_owned_entity_with_int_class_key()
    {
        await base.Can_query_and_update_owned_entity_with_int_class_key();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_and_update_owned_entity_with_int_struct_key()
    {
        await base.Can_query_and_update_owned_entity_with_int_struct_key();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_and_update_owned_entity_with_structural_generic_comparable_bytes_struct_key()
    {
        await base.Can_query_and_update_owned_entity_with_structural_generic_comparable_bytes_struct_key();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_and_update_owned_entity_with_value_converter()
    {
        await base.Can_query_and_update_owned_entity_with_value_converter();
    }

    public class KeysWithConvertersDuckDBFixture : KeysWithConvertersFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => builder.UseDuckDB(b => b.MinBatchSize(1));
    }
}
