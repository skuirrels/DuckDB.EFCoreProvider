using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

// TODO
/*
 * Reload_when_entity_deleted_in_store_can_happen_for_any_state
 * Store_values_really_are_store_values_not_current_or_original_values
 * Store_values_really_are_store_values_not_current_or_original_values_async
 * Values_can_be_reloaded_from_database_for_entity_in_any_state
 */
public abstract class PropertyValuesDuckDBTest : PropertyValuesRelationalTestBase<PropertyValuesDuckDBTest.PropertyValuesDuckDBFixture>
{
    protected PropertyValuesDuckDBTest(PropertyValuesDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Complex_collection_current_values_can_be_accessed_as_a_property_dictionary()
    {
        await base.Complex_collection_current_values_can_be_accessed_as_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_current_values_can_be_accessed_as_a_property_dictionary()
    {
        await base.Scalar_current_values_can_be_accessed_as_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_original_values_can_be_accessed_as_a_property_dictionary()
    {
        await base.Scalar_original_values_can_be_accessed_as_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_store_values_can_be_accessed_as_a_property_dictionary()
    {
        await base.Scalar_store_values_can_be_accessed_as_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_store_values_can_be_accessed_asynchronously_as_a_property_dictionary()
    {
        await base.Scalar_store_values_can_be_accessed_asynchronously_as_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_current_values_can_be_accessed_as_a_property_dictionary_using_IProperty()
    {
        await base.Scalar_current_values_can_be_accessed_as_a_property_dictionary_using_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_original_values_can_be_accessed_as_a_property_dictionary_using_IProperty()
    {
        await base.Scalar_original_values_can_be_accessed_as_a_property_dictionary_using_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_store_values_can_be_accessed_as_a_property_dictionary_using_IProperty()
    {
        await base.Scalar_store_values_can_be_accessed_as_a_property_dictionary_using_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_store_values_can_be_accessed_asynchronously_as_a_property_dictionary_using_IProperty()
    {
        await base.Scalar_store_values_can_be_accessed_asynchronously_as_a_property_dictionary_using_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_current_values_of_a_derived_object_can_be_accessed_as_a_property_dictionary()
    {
        await base.Scalar_current_values_of_a_derived_object_can_be_accessed_as_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_original_values_of_a_derived_object_can_be_accessed_as_a_property_dictionary()
    {
        await base.Scalar_original_values_of_a_derived_object_can_be_accessed_as_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_store_values_of_a_derived_object_can_be_accessed_as_a_property_dictionary()
    {
        await base.Scalar_store_values_of_a_derived_object_can_be_accessed_as_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_store_values_of_a_derived_object_can_be_accessed_asynchronously_as_a_property_dictionary()
    {
        await base.Scalar_store_values_of_a_derived_object_can_be_accessed_asynchronously_as_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_current_values_can_be_accessed_as_a_non_generic_property_dictionary()
    {
        await base.Scalar_current_values_can_be_accessed_as_a_non_generic_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_original_values_can_be_accessed_as_a_non_generic_property_dictionary()
    {
        await base.Scalar_original_values_can_be_accessed_as_a_non_generic_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_store_values_can_be_accessed_as_a_non_generic_property_dictionary()
    {
        await base.Scalar_store_values_can_be_accessed_as_a_non_generic_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_store_values_can_be_accessed_asynchronously_as_a_non_generic_property_dictionary()
    {
        await base.Scalar_store_values_can_be_accessed_asynchronously_as_a_non_generic_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_current_values_can_be_accessed_as_a_non_generic_property_dictionary_using_IProperty()
    {
        await base.Scalar_current_values_can_be_accessed_as_a_non_generic_property_dictionary_using_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_original_values_can_be_accessed_as_a_non_generic_property_dictionary_using_IProperty()
    {
        await base.Scalar_original_values_can_be_accessed_as_a_non_generic_property_dictionary_using_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_store_values_can_be_accessed_as_a_non_generic_property_dictionary_using_IProperty()
    {
        await base.Scalar_store_values_can_be_accessed_as_a_non_generic_property_dictionary_using_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_store_values_can_be_accessed_asynchronously_as_a_non_generic_property_dictionary_using_IProperty()
    {
        await base.Scalar_store_values_can_be_accessed_asynchronously_as_a_non_generic_property_dictionary_using_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_current_values_of_a_derived_object_can_be_accessed_as_a_non_generic_property_dictionary()
    {
        await base.Scalar_current_values_of_a_derived_object_can_be_accessed_as_a_non_generic_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_original_values_of_a_derived_object_can_be_accessed_as_a_non_generic_property_dictionary()
    {
        await base.Scalar_original_values_of_a_derived_object_can_be_accessed_as_a_non_generic_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_store_values_of_a_derived_object_can_be_accessed_as_a_non_generic_property_dictionary()
    {
        await base.Scalar_store_values_of_a_derived_object_can_be_accessed_as_a_non_generic_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Scalar_store_values_of_a_derived_object_can_be_accessed_asynchronously_as_a_non_generic_property_dictionary()
    {
        await base.Scalar_store_values_of_a_derived_object_can_be_accessed_asynchronously_as_a_non_generic_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Scalar_current_values_can_be_set_using_a_property_dictionary()
    {
        base.Scalar_current_values_can_be_set_using_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Scalar_original_values_can_be_set_using_a_property_dictionary()
    {
        base.Scalar_original_values_can_be_set_using_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Scalar_current_values_can_be_set_using_a_property_dictionary_with_IProperty()
    {
        base.Scalar_current_values_can_be_set_using_a_property_dictionary_with_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Scalar_original_values_can_be_set_using_a_property_dictionary_with_IProperty()
    {
        base.Scalar_original_values_can_be_set_using_a_property_dictionary_with_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Scalar_current_values_can_be_set_using_a_non_generic_property_dictionary()
    {
        base.Scalar_current_values_can_be_set_using_a_non_generic_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Scalar_original_values_can_be_set_using_a_non_generic_property_dictionary()
    {
        base.Scalar_original_values_can_be_set_using_a_non_generic_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Complex_current_values_can_be_accessed_as_a_property_dictionary_using_IProperty()
    {
        await base.Complex_current_values_can_be_accessed_as_a_property_dictionary_using_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Complex_original_values_can_be_accessed_as_a_property_dictionary_using_IProperty()
    {
        await base.Complex_original_values_can_be_accessed_as_a_property_dictionary_using_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Complex_store_values_can_be_accessed_as_a_property_dictionary_using_IProperty()
    {
        await base.Complex_store_values_can_be_accessed_as_a_property_dictionary_using_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Complex_store_values_can_be_accessed_asynchronously_as_a_property_dictionary_using_IProperty()
    {
        await base.Complex_store_values_can_be_accessed_asynchronously_as_a_property_dictionary_using_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Current_values_can_be_copied_into_an_object()
    {
        await base.Current_values_can_be_copied_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Original_values_can_be_copied_into_an_object()
    {
        await base.Original_values_can_be_copied_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_copied_into_an_object()
    {
        await base.Store_values_can_be_copied_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_copied_into_an_object_asynchronously()
    {
        await base.Store_values_can_be_copied_into_an_object_asynchronously();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Current_values_for_derived_object_can_be_copied_into_an_object()
    {
        await base.Current_values_for_derived_object_can_be_copied_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Original_values_for_derived_object_can_be_copied_into_an_object()
    {
        await base.Original_values_for_derived_object_can_be_copied_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_for_derived_object_can_be_copied_into_an_object()
    {
        await base.Store_values_for_derived_object_can_be_copied_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_for_derived_object_can_be_copied_into_an_object_asynchronously()
    {
        await base.Store_values_for_derived_object_can_be_copied_into_an_object_asynchronously();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Current_values_for_join_entity_can_be_copied_into_an_object()
    {
        await base.Current_values_for_join_entity_can_be_copied_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Original_values_for_join_entity_can_be_copied_into_an_object()
    {
        await base.Original_values_for_join_entity_can_be_copied_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_for_join_entity_can_be_copied_into_an_object()
    {
        await base.Store_values_for_join_entity_can_be_copied_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_for_join_entity_can_be_copied_into_an_object_asynchronously()
    {
        await base.Store_values_for_join_entity_can_be_copied_into_an_object_asynchronously();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Current_values_can_be_copied_from_a_non_generic_property_dictionary_into_an_object()
    {
        await base.Current_values_can_be_copied_from_a_non_generic_property_dictionary_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Original_values_can_be_copied_non_generic_property_dictionary_into_an_object()
    {
        await base.Original_values_can_be_copied_non_generic_property_dictionary_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_copied_non_generic_property_dictionary_into_an_object()
    {
        await base.Store_values_can_be_copied_non_generic_property_dictionary_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_copied_asynchronously_non_generic_property_dictionary_into_an_object()
    {
        await base.Store_values_can_be_copied_asynchronously_non_generic_property_dictionary_into_an_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Current_values_can_be_copied_into_a_cloned_dictionary()
    {
        await base.Current_values_can_be_copied_into_a_cloned_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Original_values_can_be_copied_into_a_cloned_dictionary()
    {
        await base.Original_values_can_be_copied_into_a_cloned_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_copied_into_a_cloned_dictionary()
    {
        await base.Store_values_can_be_copied_into_a_cloned_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_copied_into_a_cloned_dictionary_asynchronously()
    {
        await base.Store_values_can_be_copied_into_a_cloned_dictionary_asynchronously();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Values_in_cloned_dictionary_can_be_set_with_IProperty()
    {
        base.Values_in_cloned_dictionary_can_be_set_with_IProperty();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Using_bad_property_names_throws()
    {
        base.Using_bad_property_names_throws();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Using_bad_IProperty_instances_throws()
    {
        base.Using_bad_IProperty_instances_throws();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Using_bad_property_names_throws_derived()
    {
        base.Using_bad_property_names_throws_derived();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Using_bad_IProperty_instances_throws_derived()
    {
        base.Using_bad_IProperty_instances_throws_derived();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Using_non_collection_complex_property_throws()
    {
        base.Using_non_collection_complex_property_throws();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Using_complex_property_value_not_list_throws()
    {
        base.Using_complex_property_value_not_list_throws();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Current_values_can_be_copied_into_a_non_generic_cloned_dictionary()
    {
        await base.Current_values_can_be_copied_into_a_non_generic_cloned_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Original_values_can_be_copied_into_a_non_generic_cloned_dictionary()
    {
        await base.Original_values_can_be_copied_into_a_non_generic_cloned_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_copied_into_a_non_generic_cloned_dictionary()
    {
        await base.Store_values_can_be_copied_into_a_non_generic_cloned_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_copied_asynchronously_into_a_non_generic_cloned_dictionary()
    {
        await base.Store_values_can_be_copied_asynchronously_into_a_non_generic_cloned_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Current_values_can_be_read_or_set_for_an_object_in_the_Deleted_state()
    {
        await base.Current_values_can_be_read_or_set_for_an_object_in_the_Deleted_state();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Original_values_can_be_read_and_set_for_an_object_in_the_Deleted_state()
    {
        await base.Original_values_can_be_read_and_set_for_an_object_in_the_Deleted_state();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_read_and_set_for_an_object_in_the_Deleted_state()
    {
        await base.Store_values_can_be_read_and_set_for_an_object_in_the_Deleted_state();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_read_and_set_for_an_object_in_the_Deleted_state_asynchronously()
    {
        await base.Store_values_can_be_read_and_set_for_an_object_in_the_Deleted_state_asynchronously();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Current_values_can_be_read_and_set_for_an_object_in_the_Unchanged_state()
    {
        await base.Current_values_can_be_read_and_set_for_an_object_in_the_Unchanged_state();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Original_values_can_be_read_and_set_for_an_object_in_the_Unchanged_state()
    {
        await base.Original_values_can_be_read_and_set_for_an_object_in_the_Unchanged_state();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_read_and_set_for_an_object_in_the_Unchanged_state()
    {
        await base.Store_values_can_be_read_and_set_for_an_object_in_the_Unchanged_state();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_read_and_set_for_an_object_in_the_Unchanged_state_asynchronously()
    {
        await base.Store_values_can_be_read_and_set_for_an_object_in_the_Unchanged_state_asynchronously();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Current_values_can_be_read_and_set_for_an_object_in_the_Modified_state()
    {
        await base.Current_values_can_be_read_and_set_for_an_object_in_the_Modified_state();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Original_values_can_be_read_and_set_for_an_object_in_the_Modified_state()
    {
        await base.Original_values_can_be_read_and_set_for_an_object_in_the_Modified_state();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_read_and_set_for_an_object_in_the_Modified_state()
    {
        await base.Store_values_can_be_read_and_set_for_an_object_in_the_Modified_state();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_read_and_set_for_an_object_in_the_Modified_state_asynchronously()
    {
        await base.Store_values_can_be_read_and_set_for_an_object_in_the_Modified_state_asynchronously();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Current_values_can_be_read_and_set_for_an_object_in_the_Added_state()
    {
        await base.Current_values_can_be_read_and_set_for_an_object_in_the_Added_state();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Original_values_can_be_read_or_set_for_an_object_in_the_Added_state()
    {
        await base.Original_values_can_be_read_or_set_for_an_object_in_the_Added_state();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_read_or_set_for_an_object_in_the_Added_state()
    {
        await base.Store_values_can_be_read_or_set_for_an_object_in_the_Added_state();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_read_or_set_for_an_object_in_the_Added_state_asynchronously()
    {
        await base.Store_values_can_be_read_or_set_for_an_object_in_the_Added_state_asynchronously();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Current_values_can_be_read_or_set_for_a_Detached_object()
    {
        await base.Current_values_can_be_read_or_set_for_a_Detached_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Original_values_can_be_read_or_set_for_a_Detached_object()
    {
        await base.Original_values_can_be_read_or_set_for_a_Detached_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_read_or_set_for_a_Detached_object()
    {
        await base.Store_values_can_be_read_or_set_for_a_Detached_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_read_or_set_for_a_Detached_object_asynchronously()
    {
        await base.Store_values_can_be_read_or_set_for_a_Detached_object_asynchronously();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Values_can_be_reloaded_from_database_for_entity_in_any_state_with_inheritance(EntityState state, bool async)
    {
        await base.Values_can_be_reloaded_from_database_for_entity_in_any_state_with_inheritance(state, async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Current_values_can_be_set_from_an_object_using_generic_dictionary()
    {
        base.Current_values_can_be_set_from_an_object_using_generic_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Original_values_can_be_set_from_an_object_using_generic_dictionary()
    {
        base.Original_values_can_be_set_from_an_object_using_generic_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Current_values_can_be_set_from_an_object_using_non_generic_dictionary()
    {
        base.Current_values_can_be_set_from_an_object_using_non_generic_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Original_values_can_be_set_from_an_object_using_non_generic_dictionary()
    {
        base.Original_values_can_be_set_from_an_object_using_non_generic_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Current_values_can_be_set_from_DTO_object_using_non_generic_dictionary()
    {
        base.Current_values_can_be_set_from_DTO_object_using_non_generic_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Original_values_can_be_set_from_DTO_object_using_non_generic_dictionary()
    {
        base.Original_values_can_be_set_from_DTO_object_using_non_generic_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Current_values_can_be_set_from_DTO_object_missing_key_using_non_generic_dictionary()
    {
        base.Current_values_can_be_set_from_DTO_object_missing_key_using_non_generic_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Original_values_can_be_set_from_DTO_object_missing_key_using_non_generic_dictionary()
    {
        base.Original_values_can_be_set_from_DTO_object_missing_key_using_non_generic_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Current_values_can_be_set_from_dictionary()
    {
        base.Current_values_can_be_set_from_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Original_values_can_be_set_from_dictionary()
    {
        base.Original_values_can_be_set_from_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Current_values_can_be_set_from_dictionary_typed_int()
    {
        base.Current_values_can_be_set_from_dictionary_typed_int();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Original_values_can_be_set_from_dictionary_typed_int()
    {
        base.Original_values_can_be_set_from_dictionary_typed_int();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Current_values_can_be_set_from_dictionary_typed_string()
    {
        base.Current_values_can_be_set_from_dictionary_typed_string();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Original_values_can_be_set_from_dictionary_typed_string()
    {
        base.Original_values_can_be_set_from_dictionary_typed_string();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Current_values_can_be_set_from_dictionary_some_missing()
    {
        base.Current_values_can_be_set_from_dictionary_some_missing();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Original_values_can_be_set_from_dictionary_some_missing()
    {
        base.Original_values_can_be_set_from_dictionary_some_missing();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Current_values_can_be_set_from_one_generic_dictionary_to_another_generic_dictionary()
    {
        base.Current_values_can_be_set_from_one_generic_dictionary_to_another_generic_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Original_values_can_be_set_from_one_generic_dictionary_to_another_generic_dictionary()
    {
        base.Original_values_can_be_set_from_one_generic_dictionary_to_another_generic_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Current_values_can_be_set_from_one_non_generic_dictionary_to_another_generic_dictionary()
    {
        base.Current_values_can_be_set_from_one_non_generic_dictionary_to_another_generic_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Original_values_can_be_set_from_one_non_generic_dictionary_to_another_generic_dictionary()
    {
        base.Original_values_can_be_set_from_one_non_generic_dictionary_to_another_generic_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Primary_key_in_current_values_cannot_be_changed_in_property_dictionary()
    {
        base.Primary_key_in_current_values_cannot_be_changed_in_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Primary_key_in_original_values_cannot_be_changed_in_property_dictionary()
    {
        base.Primary_key_in_original_values_cannot_be_changed_in_property_dictionary();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Non_nullable_property_in_current_values_results_in_conceptual_null(CascadeTiming deleteOrphansTiming)
    {
        base.Non_nullable_property_in_current_values_results_in_conceptual_null(deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Non_nullable_shadow_property_in_current_values_results_in_conceptual_null(CascadeTiming deleteOrphansTiming)
    {
        base.Non_nullable_shadow_property_in_current_values_results_in_conceptual_null(deleteOrphansTiming);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Non_nullable_property_in_original_values_cannot_be_set_to_null_in_property_dictionary()
    {
        base.Non_nullable_property_in_original_values_cannot_be_set_to_null_in_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Non_nullable_shadow_property_in_original_values_cannot_be_set_to_null_in_property_dictionary()
    {
        base.Non_nullable_shadow_property_in_original_values_cannot_be_set_to_null_in_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Non_nullable_property_in_cloned_dictionary_cannot_be_set_to_null()
    {
        base.Non_nullable_property_in_cloned_dictionary_cannot_be_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Property_in_current_values_cannot_be_set_to_instance_of_wrong_type()
    {
        base.Property_in_current_values_cannot_be_set_to_instance_of_wrong_type();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Property_in_original_values_cannot_be_set_to_instance_of_wrong_type()
    {
        base.Property_in_original_values_cannot_be_set_to_instance_of_wrong_type();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Shadow_property_in_current_values_cannot_be_set_to_instance_of_wrong_type()
    {
        base.Shadow_property_in_current_values_cannot_be_set_to_instance_of_wrong_type();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Shadow_property_in_original_values_cannot_be_set_to_instance_of_wrong_type()
    {
        base.Shadow_property_in_original_values_cannot_be_set_to_instance_of_wrong_type();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Property_in_cloned_dictionary_cannot_be_set_to_instance_of_wrong_type()
    {
        base.Property_in_cloned_dictionary_cannot_be_set_to_instance_of_wrong_type();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Primary_key_in_current_values_cannot_be_changed_by_setting_values_from_object()
    {
        base.Primary_key_in_current_values_cannot_be_changed_by_setting_values_from_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Primary_key_in_original_values_cannot_be_changed_by_setting_values_from_object()
    {
        base.Primary_key_in_original_values_cannot_be_changed_by_setting_values_from_object();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Primary_key_in_current_values_cannot_be_changed_by_setting_values_from_another_dictionary()
    {
        base.Primary_key_in_current_values_cannot_be_changed_by_setting_values_from_another_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Primary_key_in_original_values_cannot_be_changed_by_setting_values_from_another_dictionary()
    {
        base.Primary_key_in_original_values_cannot_be_changed_by_setting_values_from_another_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Properties_for_current_values_returns_properties()
    {
        await base.Properties_for_current_values_returns_properties();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Properties_for_original_values_returns_properties()
    {
        await base.Properties_for_original_values_returns_properties();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Properties_for_store_values_returns_properties()
    {
        await base.Properties_for_store_values_returns_properties();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Properties_for_store_values_returns_properties_asynchronously()
    {
        await base.Properties_for_store_values_returns_properties_asynchronously();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Properties_for_cloned_dictionary_returns_properties()
    {
        await base.Properties_for_cloned_dictionary_returns_properties();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task GetDatabaseValues_for_entity_not_in_the_store_returns_null()
    {
        await base.GetDatabaseValues_for_entity_not_in_the_store_returns_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task GetDatabaseValuesAsync_for_entity_not_in_the_store_returns_null()
    {
        await base.GetDatabaseValuesAsync_for_entity_not_in_the_store_returns_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task NonGeneric_GetDatabaseValues_for_entity_not_in_the_store_returns_null()
    {
        await base.NonGeneric_GetDatabaseValues_for_entity_not_in_the_store_returns_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task NonGeneric_GetDatabaseValuesAsync_for_entity_not_in_the_store_returns_null()
    {
        await base.NonGeneric_GetDatabaseValuesAsync_for_entity_not_in_the_store_returns_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task GetDatabaseValues_for_derived_entity_not_in_the_store_returns_null()
    {
        await base.GetDatabaseValues_for_derived_entity_not_in_the_store_returns_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task GetDatabaseValuesAsync_for_derived_entity_not_in_the_store_returns_null()
    {
        await base.GetDatabaseValuesAsync_for_derived_entity_not_in_the_store_returns_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task NonGeneric_GetDatabaseValues_for_derived_entity_not_in_the_store_returns_null()
    {
        await base.NonGeneric_GetDatabaseValues_for_derived_entity_not_in_the_store_returns_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task NonGeneric_GetDatabaseValuesAsync_for_derived_entity_not_in_the_store_returns_null()
    {
        await base.NonGeneric_GetDatabaseValuesAsync_for_derived_entity_not_in_the_store_returns_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task GetDatabaseValues_for_the_wrong_type_in_the_store_returns_null()
    {
        await base.GetDatabaseValues_for_the_wrong_type_in_the_store_returns_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task GetDatabaseValuesAsync_for_the_wrong_type_in_the_store_returns_null()
    {
        await base.GetDatabaseValuesAsync_for_the_wrong_type_in_the_store_returns_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task NonGeneric_GetDatabaseValues_for_the_wrong_type_in_the_store_throws()
    {
        await base.NonGeneric_GetDatabaseValues_for_the_wrong_type_in_the_store_throws();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task NonGeneric_GetDatabaseValuesAsync_for_the_wrong_type_in_the_store_throws()
    {
        await base.NonGeneric_GetDatabaseValuesAsync_for_the_wrong_type_in_the_store_throws();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Setting_store_values_does_not_change_current_or_original_values()
    {
        base.Setting_store_values_does_not_change_current_or_original_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Complex_collection_original_values_can_be_accessed_as_a_property_dictionary()
    {
        await base.Complex_collection_original_values_can_be_accessed_as_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Complex_collection_store_values_can_be_accessed_as_a_property_dictionary()
    {
        await base.Complex_collection_store_values_can_be_accessed_as_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Complex_collection_store_values_can_be_accessed_asynchronously_as_a_property_dictionary()
    {
        await base.Complex_collection_store_values_can_be_accessed_asynchronously_as_a_property_dictionary();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Setting_complex_collection_values_from_object_works()
    {
        base.Setting_complex_collection_values_from_object_works();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Setting_complex_collection_current_values_from_object_with_nulls_works()
    {
        base.Setting_complex_collection_current_values_from_object_with_nulls_works();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Setting_complex_collection_original_values_from_object_with_nulls_works()
    {
        base.Setting_complex_collection_original_values_from_object_with_nulls_works();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Setting_complex_collection_current_values_from_dictionary_works()
    {
        base.Setting_complex_collection_current_values_from_dictionary_works();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Setting_complex_collection_values_from_DTO_with_nulls_works()
    {
        base.Setting_complex_collection_values_from_DTO_with_nulls_works();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Setting_complex_collection_current_values_from_dictionary_with_nulls_works()
    {
        base.Setting_complex_collection_current_values_from_dictionary_with_nulls_works();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Setting_complex_collection_original_values_from_dictionary_with_nulls_works()
    {
        base.Setting_complex_collection_original_values_from_dictionary_with_nulls_works();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Setting_complex_collection_current_values_from_DTO_with_complex_metadata_access_works()
    {
        base.Setting_complex_collection_current_values_from_DTO_with_complex_metadata_access_works();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void SetValues_throws_for_complex_collection_with_non_list_value()
    {
        base.SetValues_throws_for_complex_collection_with_non_list_value();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void SetValues_throws_for_complex_collection_with_non_dictionary_item()
    {
        base.SetValues_throws_for_complex_collection_with_non_dictionary_item();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void SetValues_throws_for_nested_complex_collection_with_non_list_value()
    {
        base.SetValues_throws_for_nested_complex_collection_with_non_list_value();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void SetValues_throws_for_nested_complex_collection_with_non_dictionary_item()
    {
        base.SetValues_throws_for_nested_complex_collection_with_non_dictionary_item();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void SetValues_throws_for_complex_property_with_non_dictionary_value()
    {
        base.SetValues_throws_for_complex_property_with_non_dictionary_value();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Nullable_complex_property_with_null_value_returns_null_when_using_ToObject()
    {
        base.Nullable_complex_property_with_null_value_returns_null_when_using_ToObject();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Setting_current_values_from_cloned_values_sets_nullable_complex_property_to_null()
    {
        base.Setting_current_values_from_cloned_values_sets_nullable_complex_property_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Current_values_can_be_copied_to_object_using_ToObject()
    {
        base.Current_values_can_be_copied_to_object_using_ToObject();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Original_values_can_be_copied_to_object_using_ToObject()
    {
        base.Original_values_can_be_copied_to_object_using_ToObject();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_copied_to_object_using_ToObject()
    {
        await base.Store_values_can_be_copied_to_object_using_ToObject();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_copied_to_object_using_ToObject_asynchronously()
    {
        await base.Store_values_can_be_copied_to_object_using_ToObject_asynchronously();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Current_values_can_be_cloned()
    {
        base.Current_values_can_be_cloned();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Original_values_can_be_cloned()
    {
        base.Original_values_can_be_cloned();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_cloned()
    {
        await base.Store_values_can_be_cloned();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Store_values_can_be_cloned_asynchronously()
    {
        await base.Store_values_can_be_cloned_asynchronously();
    }

    public class PropertyValuesDuckDBFixture : PropertyValuesRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}