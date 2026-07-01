using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Microsoft.EntityFrameworkCore.GraphUpdates;

public class GraphUpdatesDuckDBChangedNotificationsTest : GraphUpdatesDuckDBTestBase<GraphUpdatesDuckDBChangedNotificationsTest.DuckDBFixture>
{
    public GraphUpdatesDuckDBChangedNotificationsTest(DuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Alternate_key_over_foreign_key_doesnt_bypass_delete_behavior(bool async)
    {
        await base.Alternate_key_over_foreign_key_doesnt_bypass_delete_behavior(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Avoid_nulling_shared_FK_property_when_deleting()
    {
        await base.Avoid_nulling_shared_FK_property_when_deleting();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Avoid_nulling_shared_FK_property_when_nulling_navigation(bool nullPrincipal)
    {
        await base.Avoid_nulling_shared_FK_property_when_nulling_navigation(nullPrincipal);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_add_multiple_dependents_when_multiple_possible_principal_sides()
    {
        await base.Can_add_multiple_dependents_when_multiple_possible_principal_sides();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_add_valid_first_dependent_when_multiple_possible_principal_sides()
    {
        await base.Can_add_valid_first_dependent_when_multiple_possible_principal_sides();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_add_valid_second_dependent_when_multiple_possible_principal_sides()
    {
        await base.Can_add_valid_second_dependent_when_multiple_possible_principal_sides();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_when_bool_PK_in_composite_key_has_sentinel_value(bool async, bool initialValue)
    {
        await base.Can_insert_when_bool_PK_in_composite_key_has_sentinel_value(async, initialValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_when_composite_FK_has_default_value_for_one_part(bool async)
    {
        await base.Can_insert_when_composite_FK_has_default_value_for_one_part(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_when_FK_has_default_value(bool async)
    {
        await base.Can_insert_when_FK_has_default_value(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_when_FK_has_sentinel_value(bool async)
    {
        await base.Can_insert_when_FK_has_sentinel_value(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_when_int_PK_in_composite_key_has_sentinel_value(bool async, int initialValue)
    {
        await base.Can_insert_when_int_PK_in_composite_key_has_sentinel_value(async, initialValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_when_nullable_bool_PK_in_composite_key_has_sentinel_value(bool async, bool? initialValue)
    {
        await base.Can_insert_when_nullable_bool_PK_in_composite_key_has_sentinel_value(async, initialValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Changes_to_Added_relationships_are_picked_up(ChangeMechanism changeMechanism)
    {
        await base.Changes_to_Added_relationships_are_picked_up(changeMechanism);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Clearing_CLR_key_owned_collection(bool async, bool useUpdate, bool addNew)
    {
        await base.Clearing_CLR_key_owned_collection(async, useUpdate, addNew);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Clearing_shadow_key_owned_collection_throws(bool async, bool useUpdate, bool addNew)
    {
        await base.Clearing_shadow_key_owned_collection_throws(async, useUpdate, addNew);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Delete_principal_with_CLR_key_owned_collection(bool async)
    {
        await base.Delete_principal_with_CLR_key_owned_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Delete_principal_with_shadow_key_owned_collection_throws(bool async)
    {
        await base.Delete_principal_with_shadow_key_owned_collection_throws(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Detaching_dependent_entity_will_not_remove_references_to_it()
    {
        await base.Detaching_dependent_entity_will_not_remove_references_to_it();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Detaching_principal_entity_will_remove_references_to_it()
    {
        await base.Detaching_principal_entity_will_remove_references_to_it();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Discriminator_values_are_not_marked_as_unknown(bool async)
    {
        await base.Discriminator_values_are_not_marked_as_unknown(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Mark_explicitly_set_dependent_appropriately_with_any_inheritance_and_stable_generator(bool async, bool useAdd)
    {
        await base.Mark_explicitly_set_dependent_appropriately_with_any_inheritance_and_stable_generator(async, useAdd);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Mark_explicitly_set_stable_dependent_appropriately(bool async, bool useAdd)
    {
        await base.Mark_explicitly_set_stable_dependent_appropriately(async, useAdd);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Mark_explicitly_set_stable_dependent_appropriately_when_deep_in_graph(bool async, bool useAdd)
    {
        await base.Mark_explicitly_set_stable_dependent_appropriately_when_deep_in_graph(async, useAdd);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Mark_modified_one_to_many_overlapping(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Mark_modified_one_to_many_overlapping(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Mutating_discriminator_value_can_be_configured_to_allow_mutation()
    {
        await base.Mutating_discriminator_value_can_be_configured_to_allow_mutation();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Mutating_discriminator_value_throws_by_convention()
    {
        await base.Mutating_discriminator_value_throws_by_convention();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task New_FK_is_not_cleared_on_old_dependent_delete(bool loadNewParent, CascadeTiming? deleteOrphansTiming)
    {
        await base.New_FK_is_not_cleared_on_old_dependent_delete(loadNewParent, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task No_fixup_to_Deleted_entities(CascadeTiming? deleteOrphansTiming)
    {
        await base.No_fixup_to_Deleted_entities(deleteOrphansTiming);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Notification_entities_can_have_indexes()
    {
        await base.Notification_entities_can_have_indexes();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_many_to_one_dependent_leaves_can_be_deleted(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_many_to_one_dependent_leaves_can_be_deleted(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_many_to_one_dependents_are_orphaned(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_many_to_one_dependents_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_many_to_one_dependents_are_orphaned_in_store(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_many_to_one_dependents_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_many_to_one_dependents_are_orphaned_starting_detached(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_many_to_one_dependents_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_many_to_one_dependents_are_orphaned_with_Added_graph(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_many_to_one_dependents_are_orphaned_with_Added_graph(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Optional_many_to_one_dependents_with_alternate_key_are_orphaned(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        return base.Optional_many_to_one_dependents_with_alternate_key_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_many_to_one_dependents_with_alternate_key_are_orphaned_in_store(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_many_to_one_dependents_with_alternate_key_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_many_to_one_dependents_with_alternate_key_are_orphaned_starting_detached(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_many_to_one_dependents_with_alternate_key_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_one_to_one_are_orphaned(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_one_to_one_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_one_to_one_are_orphaned_in_store(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_one_to_one_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_one_to_one_are_orphaned_starting_detached(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_one_to_one_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_one_to_one_leaf_can_be_deleted(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_one_to_one_leaf_can_be_deleted(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_one_to_one_relationships_are_one_to_one(CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_one_to_one_relationships_are_one_to_one(deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_one_to_one_with_AK_relationships_are_one_to_one(CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_one_to_one_with_AK_relationships_are_one_to_one(deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_one_to_one_with_alternate_key_are_orphaned(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_one_to_one_with_alternate_key_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_one_to_one_with_alternate_key_are_orphaned_in_store(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_one_to_one_with_alternate_key_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_one_to_one_with_alternate_key_are_orphaned_starting_detached(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Optional_one_to_one_with_alternate_key_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Re_childing_parent_to_new_child_with_delete(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Re_childing_parent_to_new_child_with_delete(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Reparent_dependent_one_to_many(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Reparent_dependent_one_to_many(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Reparent_dependent_one_to_many_ak(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Reparent_dependent_one_to_many_ak(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Reparent_one_to_many_overlapping(ChangeMechanism changeMechanism, bool useExistingParent, CascadeTiming? deleteOrphansTiming)
    {
        await base.Reparent_one_to_many_overlapping(changeMechanism, useExistingParent, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Reparent_optional_one_to_one(ChangeMechanism changeMechanism, bool useExistingRoot, CascadeTiming? deleteOrphansTiming)
    {
        await base.Reparent_optional_one_to_one(changeMechanism, useExistingRoot, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Reparent_optional_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingRoot, CascadeTiming? deleteOrphansTiming)
    {
        await base.Reparent_optional_one_to_one_with_alternate_key(changeMechanism, useExistingRoot, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Reparent_required_non_PK_one_to_one(ChangeMechanism changeMechanism, bool useExistingRoot, CascadeTiming? deleteOrphansTiming)
    {
        await base.Reparent_required_non_PK_one_to_one(changeMechanism, useExistingRoot, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Reparent_required_non_PK_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingRoot, CascadeTiming? deleteOrphansTiming)
    {
        await base.Reparent_required_non_PK_one_to_one_with_alternate_key(changeMechanism, useExistingRoot, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Reparent_required_one_to_one(ChangeMechanism changeMechanism, bool useExistingRoot, CascadeTiming? deleteOrphansTiming)
    {
        await base.Reparent_required_one_to_one(changeMechanism, useExistingRoot, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Reparent_required_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingRoot, CascadeTiming? deleteOrphansTiming)
    {
        await base.Reparent_required_one_to_one_with_alternate_key(changeMechanism, useExistingRoot, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Reparent_to_different_one_to_many(ChangeMechanism changeMechanism, bool useExistingParent, CascadeTiming? deleteOrphansTiming)
    {
        await base.Reparent_to_different_one_to_many(changeMechanism, useExistingParent, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_many_to_one_dependent_leaves_can_be_deleted(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_many_to_one_dependent_leaves_can_be_deleted(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_many_to_one_dependents_are_cascade_deleted(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_many_to_one_dependents_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_many_to_one_dependents_are_cascade_deleted_in_store(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_many_to_one_dependents_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_many_to_one_dependents_are_cascade_deleted_starting_detached(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_many_to_one_dependents_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_many_to_one_dependents_are_cascade_detached_when_Added(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_many_to_one_dependents_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_in_store(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_starting_detached(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_detached_when_Added(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_non_PK_one_to_one_are_cascade_deleted(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_non_PK_one_to_one_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_non_PK_one_to_one_are_cascade_deleted_in_store(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_non_PK_one_to_one_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_non_PK_one_to_one_are_cascade_deleted_starting_detached(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_non_PK_one_to_one_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_non_PK_one_to_one_are_cascade_detached_when_Added(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_non_PK_one_to_one_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_non_PK_one_to_one_leaf_can_be_deleted(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_non_PK_one_to_one_leaf_can_be_deleted(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_in_store(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_detached_when_Added(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_one_to_one_are_cascade_deleted(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_one_to_one_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_one_to_one_are_cascade_deleted_in_store(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_one_to_one_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_one_to_one_are_cascade_deleted_starting_detached(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_one_to_one_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_one_to_one_are_cascade_detached_when_Added(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_one_to_one_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_one_to_one_leaf_can_be_deleted(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_one_to_one_leaf_can_be_deleted(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_one_to_one_relationships_are_one_to_one(CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_one_to_one_relationships_are_one_to_one(deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_one_to_one_with_AK_relationships_are_one_to_one(CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_one_to_one_with_AK_relationships_are_one_to_one(deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_one_to_one_with_alternate_key_are_cascade_deleted(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_one_to_one_with_alternate_key_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_one_to_one_with_alternate_key_are_cascade_deleted_in_store(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_one_to_one_with_alternate_key_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Required_one_to_one_with_alternate_key_are_cascade_detached_when_Added(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
    {
        await base.Required_one_to_one_with_alternate_key_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Reset_unknown_original_value_when_current_value_is_set(bool async)
    {
        await base.Reset_unknown_original_value_when_current_value_is_set(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Resetting_a_deleted_reference_fixes_up_again()
    {
        await base.Resetting_a_deleted_reference_fixes_up_again();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_changed_optional_one_to_one(ChangeMechanism changeMechanism, bool useExistingEntities, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_changed_optional_one_to_one(changeMechanism, useExistingEntities, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_changed_optional_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_changed_optional_one_to_one_with_alternate_key(changeMechanism, useExistingEntities, deleteOrphansTiming);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_changed_optional_one_to_one_with_alternate_key_in_store()
    {
        await base.Save_changed_optional_one_to_one_with_alternate_key_in_store();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_changed_owned_one_to_many()
    {
        await base.Save_changed_owned_one_to_many();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_optional_many_to_one_dependents(ChangeMechanism changeMechanism, bool useExistingEntities, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_optional_many_to_one_dependents(changeMechanism, useExistingEntities, deleteOrphansTiming);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_changed_owned_one_to_one()
    {
        await base.Save_changed_owned_one_to_one();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_optional_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_optional_many_to_one_dependents_with_alternate_key(changeMechanism, useExistingEntities, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_removed_optional_many_to_one_dependents(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_removed_optional_many_to_one_dependents(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_removed_optional_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_removed_optional_many_to_one_dependents_with_alternate_key(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_removed_required_many_to_one_dependents(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_removed_required_many_to_one_dependents(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_removed_required_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_removed_required_many_to_one_dependents_with_alternate_key(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_required_many_to_one_dependents(ChangeMechanism changeMechanism, bool useExistingEntities, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_required_many_to_one_dependents(changeMechanism, useExistingEntities, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_required_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_required_many_to_one_dependents_with_alternate_key(changeMechanism, useExistingEntities, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_required_non_PK_one_to_one_changed_by_reference(ChangeMechanism changeMechanism, bool useExistingEntities, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_required_non_PK_one_to_one_changed_by_reference(changeMechanism, useExistingEntities, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_required_non_PK_one_to_one_changed_by_reference_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_required_non_PK_one_to_one_changed_by_reference_with_alternate_key(changeMechanism, useExistingEntities, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_required_one_to_one_changed_by_reference(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_required_one_to_one_changed_by_reference(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Save_required_one_to_one_changed_by_reference_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities, CascadeTiming? deleteOrphansTiming)
    {
        await base.Save_required_one_to_one_changed_by_reference_with_alternate_key(changeMechanism, useExistingEntities, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Saving_multiple_modified_entities_with_the_same_key_does_not_overflow(bool async)
    {
        await base.Saving_multiple_modified_entities_with_the_same_key_does_not_overflow(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Saving_unknown_key_value_marks_it_as_unmodified(bool async)
    {
        await base.Saving_unknown_key_value_marks_it_as_unmodified(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Sever_optional_one_to_one(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Sever_optional_one_to_one(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Sever_optional_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Sever_optional_one_to_one_with_alternate_key(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Sever_relationship_that_will_later_be_deleted(bool async)
    {
        await base.Sever_relationship_that_will_later_be_deleted(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Sever_required_non_PK_one_to_one(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Sever_required_non_PK_one_to_one(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Sever_required_non_PK_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Sever_required_non_PK_one_to_one_with_alternate_key(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Sever_required_one_to_one(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Sever_required_one_to_one(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Sever_required_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, CascadeTiming? deleteOrphansTiming)
    {
        await base.Sever_required_one_to_one_with_alternate_key(changeMechanism, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Shadow_skip_navigation_in_base_class_is_handled(bool async)
    {
        await base.Shadow_skip_navigation_in_base_class_is_handled(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Sometimes_not_calling_DetectChanges_when_required_does_not_throw_for_null_ref()
    {
        await base.Sometimes_not_calling_DetectChanges_when_required_does_not_throw_for_null_ref();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Throws_for_single_property_bool_key_with_default_value_generation(bool async, bool initialValue)
    {
        await base.Throws_for_single_property_bool_key_with_default_value_generation(async, initialValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Throws_for_single_property_nullable_bool_key_with_default_value_generation(bool async, bool? initialValue)
    {
        await base.Throws_for_single_property_nullable_bool_key_with_default_value_generation(async, initialValue);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Update_principal_with_CLR_key_owned_collection(bool async)
    {
        await base.Update_principal_with_CLR_key_owned_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Update_principal_with_non_generated_shadow_key_owned_collection_throws(bool async, bool delete)
    {
        await base.Update_principal_with_non_generated_shadow_key_owned_collection_throws(async, delete);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Update_principal_with_shadow_key_owned_collection_throws(bool async)
    {
        await base.Update_principal_with_shadow_key_owned_collection_throws(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Update_root_by_collection_replacement_of_deleted_first_level(bool async)
    {
        await base.Update_root_by_collection_replacement_of_deleted_first_level(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Update_root_by_collection_replacement_of_deleted_second_level(bool async)
    {
        await base.Update_root_by_collection_replacement_of_deleted_second_level(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Update_root_by_collection_replacement_of_deleted_third_level(bool async)
    {
        await base.Update_root_by_collection_replacement_of_deleted_third_level(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Update_root_by_collection_replacement_of_inserted_first_level(bool async)
    {
        await base.Update_root_by_collection_replacement_of_inserted_first_level(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Update_root_by_collection_replacement_of_inserted_first_level_level(bool async)
    {
        await base.Update_root_by_collection_replacement_of_inserted_first_level_level(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Update_root_by_collection_replacement_of_inserted_second_level(bool async)
    {
        await base.Update_root_by_collection_replacement_of_inserted_second_level(async);
    }

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());

    public class DuckDBFixture : GraphUpdatesDuckDBFixtureBase
    {
        protected override string StoreName
            => "GraphUpdatesChangedTest";

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangedNotifications);

            base.OnModelCreating(modelBuilder, context);
        }
    }
}