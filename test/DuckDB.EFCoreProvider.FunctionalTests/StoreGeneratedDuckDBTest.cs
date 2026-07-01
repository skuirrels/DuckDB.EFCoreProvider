using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

// TODO
/*
 * Change_state_of_entity_with_temp_non_key_does_not_throw
 * Clearing_optional_FK_does_not_leave_temporary_value
 */
public abstract class StoreGeneratedDuckDBTest : StoreGeneratedTestBase<StoreGeneratedDuckDBTest.StoreGeneratedDuckDBFixture>
{
    protected StoreGeneratedDuckDBTest(StoreGeneratedDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Value_generation_works_for_common_GUID_conversions()
    {
        await base.Value_generation_works_for_common_GUID_conversions();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Before_save_throw_always_throws_if_value_set(string propertyName)
    {
        await base.Before_save_throw_always_throws_if_value_set(propertyName);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Before_save_throw_ignores_value_if_not_set(string propertyName, string? expectedValue)
    {
        await base.Before_save_throw_ignores_value_if_not_set(propertyName, expectedValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Before_save_use_always_uses_value_if_set(string propertyName)
    {
        await base.Before_save_use_always_uses_value_if_set(propertyName);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Before_save_use_ignores_value_if_not_set(string propertyName, string? expectedValue)
    {
        await base.Before_save_use_ignores_value_if_not_set(propertyName, expectedValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Before_save_ignore_ignores_value_if_not_set(string propertyName, string? expectedValue)
    {
        await base.Before_save_ignore_ignores_value_if_not_set(propertyName, expectedValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Before_save_ignore_ignores_value_even_if_set(string propertyName, string? expectedValue)
    {
        await base.Before_save_ignore_ignores_value_even_if_set(propertyName, expectedValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task After_save_throw_always_throws_if_value_modified(string propertyName)
    {
        await base.After_save_throw_always_throws_if_value_modified(propertyName);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task After_save_throw_ignores_value_if_not_modified(string propertyName, string? expectedValue)
    {
        await base.After_save_throw_ignores_value_if_not_modified(propertyName, expectedValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task After_save_ignore_ignores_value_if_not_modified(string propertyName, string? expectedValue)
    {
        await base.After_save_ignore_ignores_value_if_not_modified(propertyName, expectedValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task After_save_ignore_ignores_value_even_if_modified(string propertyName, string? expectedValue)
    {
        await base.After_save_ignore_ignores_value_even_if_modified(propertyName, expectedValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task After_save_use_ignores_value_if_not_modified(string propertyName, string? expectedValue)
    {
        await base.After_save_use_ignores_value_if_not_modified(propertyName, expectedValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task After_save_use_uses_value_if_modified(string propertyName, string expectedValue)
    {
        await base.After_save_use_uses_value_if_modified(propertyName, expectedValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Identity_key_with_read_only_before_save_throws_if_explicit_values_set()
    {
        await base.Identity_key_with_read_only_before_save_throws_if_explicit_values_set();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Identity_property_on_Added_entity_with_temporary_value_gets_value_from_store()
    {
        await base.Identity_property_on_Added_entity_with_temporary_value_gets_value_from_store();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_generated_values_are_propagated_with_composite_key_cycles()
    {
        await base.Store_generated_values_are_propagated_with_composite_key_cycles();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Identity_property_on_Added_entity_with_temporary_value_gets_value_from_store_even_if_same()
    {
        await base.Identity_property_on_Added_entity_with_temporary_value_gets_value_from_store_even_if_same();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Identity_property_on_Added_entity_with_default_value_gets_value_from_store()
    {
        await base.Identity_property_on_Added_entity_with_default_value_gets_value_from_store();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Identity_property_on_Added_entity_with_read_only_before_save_throws_if_explicit_values_set()
    {
        await base.Identity_property_on_Added_entity_with_read_only_before_save_throws_if_explicit_values_set();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Identity_property_on_Added_entity_can_have_value_set_explicitly()
    {
        await base.Identity_property_on_Added_entity_can_have_value_set_explicitly();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Identity_property_on_Modified_entity_with_read_only_after_save_throws_if_value_is_in_modified_state()
    {
        await base.Identity_property_on_Modified_entity_with_read_only_after_save_throws_if_value_is_in_modified_state();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Identity_property_on_Modified_entity_is_included_in_update_when_modified()
    {
        await base.Identity_property_on_Modified_entity_is_included_in_update_when_modified();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Identity_property_on_Modified_entity_is_not_included_in_update_when_not_modified()
    {
        await base.Identity_property_on_Modified_entity_is_not_included_in_update_when_not_modified();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Always_identity_property_on_Added_entity_with_temporary_value_gets_value_from_store()
    {
        await base.Always_identity_property_on_Added_entity_with_temporary_value_gets_value_from_store();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Always_identity_property_on_Added_entity_with_default_value_gets_value_from_store()
    {
        await base.Always_identity_property_on_Added_entity_with_default_value_gets_value_from_store();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Always_identity_property_on_Added_entity_with_read_only_before_save_throws_if_explicit_values_set()
    {
        await base.Always_identity_property_on_Added_entity_with_read_only_before_save_throws_if_explicit_values_set();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Always_identity_property_on_Modified_entity_with_read_only_after_save_throws_if_value_is_in_modified_state()
    {
        await base.Always_identity_property_on_Modified_entity_with_read_only_after_save_throws_if_value_is_in_modified_state();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Always_identity_property_on_Modified_entity_is_not_included_in_the_update_when_not_modified()
    {
        await base.Always_identity_property_on_Modified_entity_is_not_included_in_the_update_when_not_modified();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Computed_property_on_Added_entity_with_temporary_value_gets_value_from_store()
    {
        await base.Computed_property_on_Added_entity_with_temporary_value_gets_value_from_store();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Computed_property_on_Added_entity_with_default_value_gets_value_from_store()
    {
        await base.Computed_property_on_Added_entity_with_default_value_gets_value_from_store();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Computed_property_on_Added_entity_with_read_only_before_save_throws_if_explicit_values_set()
    {
        await base.Computed_property_on_Added_entity_with_read_only_before_save_throws_if_explicit_values_set();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Computed_property_on_Added_entity_can_have_value_set_explicitly()
    {
        await base.Computed_property_on_Added_entity_can_have_value_set_explicitly();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Computed_property_on_Modified_entity_with_read_only_after_save_throws_if_value_is_in_modified_state()
    {
        await base.Computed_property_on_Modified_entity_with_read_only_after_save_throws_if_value_is_in_modified_state();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Computed_property_on_Modified_entity_is_included_in_update_when_modified()
    {
        await base.Computed_property_on_Modified_entity_is_included_in_update_when_modified();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Computed_property_on_Modified_entity_is_read_from_store_when_not_modified()
    {
        await base.Computed_property_on_Modified_entity_is_read_from_store_when_not_modified();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Always_computed_property_on_Added_entity_with_temporary_value_gets_value_from_store()
    {
        await base.Always_computed_property_on_Added_entity_with_temporary_value_gets_value_from_store();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Always_computed_property_on_Added_entity_with_default_value_gets_value_from_store()
    {
        await base.Always_computed_property_on_Added_entity_with_default_value_gets_value_from_store();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Always_computed_property_on_Added_entity_with_read_only_before_save_throws_if_explicit_values_set()
    {
        await base.Always_computed_property_on_Added_entity_with_read_only_before_save_throws_if_explicit_values_set();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Always_computed_property_on_Modified_entity_with_read_only_after_save_throws_if_value_is_in_modified_state()
    {
        await base.Always_computed_property_on_Modified_entity_with_read_only_after_save_throws_if_value_is_in_modified_state();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Always_computed_property_on_Modified_entity_is_read_from_store_when_not_modified()
    {
        await base.Always_computed_property_on_Modified_entity_is_read_from_store_when_not_modified();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Fields_used_correctly_for_store_generated_values()
    {
        await base.Fields_used_correctly_for_store_generated_values();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Nullable_fields_get_defaults_when_not_set()
    {
        await base.Nullable_fields_get_defaults_when_not_set();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Properties_get_database_defaults_when_set_to_sentinel_values()
    {
        await base.Properties_get_database_defaults_when_set_to_sentinel_values();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Properties_get_set_values_when_not_set_to_sentinel_values()
    {
        await base.Properties_get_set_values_when_not_set_to_sentinel_values();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Nullable_fields_store_non_defaults_when_set()
    {
        await base.Nullable_fields_store_non_defaults_when_set();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Nullable_fields_store_any_value_when_set()
    {
        await base.Nullable_fields_store_any_value_when_set();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Object_fields_get_defaults_when_not_set()
    {
        await base.Object_fields_get_defaults_when_not_set();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Object_fields_store_non_defaults_when_set()
    {
        await base.Object_fields_store_non_defaults_when_set();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Object_fields_store_any_value_when_set()
    {
        await base.Object_fields_store_any_value_when_set();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Insert_update_and_delete_with_wrapped_int_key()
    {
        await base.Insert_update_and_delete_with_wrapped_int_key();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Insert_update_and_delete_with_long_to_int_conversion()
    {
        await base.Insert_update_and_delete_with_long_to_int_conversion();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Insert_update_and_delete_with_wrapped_string_key()
    {
        await base.Insert_update_and_delete_with_wrapped_string_key();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Insert_update_and_delete_with_wrapped_Guid_key()
    {
        await base.Insert_update_and_delete_with_wrapped_Guid_key();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Insert_update_and_delete_with_wrapped_Uri_key()
    {
        await base.Insert_update_and_delete_with_wrapped_Uri_key();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Insert_update_and_delete_with_Uri_key()
    {
        await base.Insert_update_and_delete_with_Uri_key();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Insert_update_and_delete_with_enum_key()
    {
        await base.Insert_update_and_delete_with_enum_key();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Insert_update_and_delete_with_GuidAsString_key()
    {
        await base.Insert_update_and_delete_with_GuidAsString_key();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Insert_update_and_delete_with_StringAsGuid_key()
    {
        await base.Insert_update_and_delete_with_StringAsGuid_key();
    }

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());

    public class StoreGeneratedDuckDBFixture : StoreGeneratedFixtureBase
    {
        protected override string StoreName
            => "StoreGeneratedTest";

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => builder
                .EnableSensitiveDataLogging()
                .ConfigureWarnings(b => b.Default(WarningBehavior.Throw)
                    .Ignore(CoreEventId.SensitiveDataLoggingEnabledWarning)
                    .Ignore(RelationalEventId.BoolWithDefaultWarning));

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            modelBuilder.Entity<Gumball>(b =>
            {
                b.Property(e => e.Identity).HasDefaultValue("Banana Joe");
                b.Property(e => e.IdentityReadOnlyBeforeSave).HasDefaultValue("Doughnut Sheriff");
                b.Property(e => e.IdentityReadOnlyAfterSave).HasDefaultValue("Anton");
                b.Property(e => e.AlwaysIdentity).HasDefaultValue("Banana Joe");
                b.Property(e => e.AlwaysIdentityReadOnlyBeforeSave).HasDefaultValue("Doughnut Sheriff");
                b.Property(e => e.AlwaysIdentityReadOnlyAfterSave).HasDefaultValue("Anton");
                b.Property(e => e.Computed).HasDefaultValue("Alan");
                b.Property(e => e.ComputedReadOnlyBeforeSave).HasDefaultValue("Carmen");
                b.Property(e => e.ComputedReadOnlyAfterSave).HasDefaultValue("Tina Rex");
                b.Property(e => e.AlwaysComputed).HasDefaultValue("Alan");
                b.Property(e => e.AlwaysComputedReadOnlyBeforeSave).HasDefaultValue("Carmen");
                b.Property(e => e.AlwaysComputedReadOnlyAfterSave).HasDefaultValue("Tina Rex");
            });

            modelBuilder.Entity<Anais>(b =>
            {
                b.Property(e => e.OnAdd).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddUseBeforeUseAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddIgnoreBeforeUseAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddThrowBeforeUseAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddUseBeforeIgnoreAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddIgnoreBeforeIgnoreAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddThrowBeforeIgnoreAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddUseBeforeThrowAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddIgnoreBeforeThrowAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddThrowBeforeThrowAfter).HasDefaultValue("Rabbit");

                b.Property(e => e.OnAddOrUpdate).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddOrUpdateUseBeforeUseAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddOrUpdateIgnoreBeforeUseAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddOrUpdateThrowBeforeUseAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddOrUpdateUseBeforeIgnoreAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddOrUpdateIgnoreBeforeIgnoreAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddOrUpdateThrowBeforeIgnoreAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddOrUpdateUseBeforeThrowAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddOrUpdateIgnoreBeforeThrowAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnAddOrUpdateThrowBeforeThrowAfter).HasDefaultValue("Rabbit");

                b.Property(e => e.OnUpdate).HasDefaultValue("Rabbit");
                b.Property(e => e.OnUpdateUseBeforeUseAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnUpdateIgnoreBeforeUseAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnUpdateThrowBeforeUseAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnUpdateUseBeforeIgnoreAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnUpdateIgnoreBeforeIgnoreAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnUpdateThrowBeforeIgnoreAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnUpdateUseBeforeThrowAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnUpdateIgnoreBeforeThrowAfter).HasDefaultValue("Rabbit");
                b.Property(e => e.OnUpdateThrowBeforeThrowAfter).HasDefaultValue("Rabbit");
            });

            modelBuilder.Entity<WithNoBackingFields>(b =>
            {
                b.Property(e => e.TrueDefault).HasDefaultValue(true);
                b.Property(e => e.NonZeroDefault).HasDefaultValue(-1);
                b.Property(e => e.FalseDefault).HasDefaultValue(false);
                b.Property(e => e.ZeroDefault).HasDefaultValue(0);
            });

            modelBuilder.Entity<WithNullableBackingFields>(b =>
            {
                b.Property(e => e.NullableBackedBoolTrueDefault).HasDefaultValue(true);
                b.Property(e => e.NullableBackedIntNonZeroDefault).HasDefaultValue(-1);
                b.Property(e => e.NullableBackedBoolFalseDefault).HasDefaultValue(false);
                b.Property(e => e.NullableBackedIntZeroDefault).HasDefaultValue(0);
            });

            modelBuilder.Entity<WithObjectBackingFields>(b =>
            {
                b.Property(e => e.NullableBackedBoolTrueDefault).HasDefaultValue(true);
                b.Property(e => e.NullableBackedIntNonZeroDefault).HasDefaultValue(-1);
                b.Property(e => e.NullableBackedBoolFalseDefault).HasDefaultValue(false);
                b.Property(e => e.NullableBackedIntZeroDefault).HasDefaultValue(0);
            });

            modelBuilder.Entity<Zach>().Property(e => e.Id)
                .ValueGeneratedOnAdd()
                .HasDefaultValueSql("randomblob(16)");

            modelBuilder.Entity<NonStoreGenDependent>().Property(e => e.HasTemp).HasDefaultValue(777);

            base.OnModelCreating(modelBuilder, context);
        }
    }

    private class Zach
    {
        public byte[] Id { get; set; } = null!;
    }
}