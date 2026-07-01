using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore.GraphUpdates;

public class ProxyGraphUpdatesDuckDBTest
{
    public abstract class ProxyGraphUpdatesDuckDBTestBase<TFixture>(TFixture fixture) : ProxyGraphUpdatesTestBase<TFixture>(fixture)
        where TFixture : ProxyGraphUpdatesDuckDBTestBase<TFixture>.ProxyGraphUpdatesDuckDBFixtureBase, new()
    {
        protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
            => facade.UseTransaction(transaction.GetDbTransaction());

        public abstract class ProxyGraphUpdatesDuckDBFixtureBase : ProxyGraphUpdatesFixtureBase
        {
            public TestSqlLoggerFactory TestSqlLoggerFactory
                => (TestSqlLoggerFactory)ListLoggerFactory;

            protected override ITestStoreFactory TestStoreFactory
                => DuckDBTestStoreFactory.Instance;
        }
    }

    public class LazyLoading(LazyLoading.ProxyGraphUpdatesWithLazyLoadingDuckDBFixture fixture)
        : ProxyGraphUpdatesDuckDBTestBase<LazyLoading.ProxyGraphUpdatesWithLazyLoadingDuckDBFixture>(fixture)
    {
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

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_optional_AK_graph_of_duplicates()
        {
            await base.Can_attach_full_optional_AK_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_optional_graph_of_duplicates()
        {
            await base.Can_attach_full_optional_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_AK_graph_of_duplicates()
        {
            await base.Can_attach_full_required_AK_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_composite_graph_of_duplicates()
        {
            await base.Can_attach_full_required_composite_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_graph_of_duplicates()
        {
            await base.Can_attach_full_required_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_non_PK_AK_graph_of_duplicates()
        {
            await base.Can_attach_full_required_non_PK_AK_graph_of_duplicates();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_non_PK_graph_of_duplicates()
        {
            await base.Can_attach_full_required_non_PK_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_one_to_many_graph_of_duplicates()
        {
            await base.Can_attach_full_required_one_to_many_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_use_record_proxies_with_base_types_to_load_collection()
        {
            await base.Can_use_record_proxies_with_base_types_to_load_collection();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_use_record_proxies_with_base_types_to_load_reference()
        {
            await base.Can_use_record_proxies_with_base_types_to_load_reference();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task No_fixup_to_Deleted_entities()
        {
            await base.No_fixup_to_Deleted_entities();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_are_orphaned(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_are_orphaned_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_are_orphaned_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_are_orphaned_with_Added_graph(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_are_orphaned_with_Added_graph(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_with_alternate_key_are_orphaned(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_with_alternate_key_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_with_alternate_key_are_orphaned_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_with_alternate_key_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_with_alternate_key_are_orphaned_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_with_alternate_key_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_are_orphaned(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_are_orphaned_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_are_orphaned_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_relationships_are_one_to_one()
        {
            await base.Optional_one_to_one_relationships_are_one_to_one();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_with_AK_relationships_are_one_to_one()
        {
            await base.Optional_one_to_one_with_AK_relationships_are_one_to_one();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_with_alternate_key_are_orphaned(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_with_alternate_key_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_with_alternate_key_are_orphaned_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_with_alternate_key_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_with_alternate_key_are_orphaned_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_with_alternate_key_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_one_to_many_overlapping(ChangeMechanism changeMechanism, bool useExistingParent)
        {
            await base.Reparent_one_to_many_overlapping(changeMechanism, useExistingParent);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_optional_one_to_one(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_optional_one_to_one(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_optional_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_optional_one_to_one_with_alternate_key(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_required_non_PK_one_to_one(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_required_non_PK_one_to_one(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_required_non_PK_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_required_non_PK_one_to_one_with_alternate_key(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_required_one_to_one(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_required_one_to_one(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_required_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_required_one_to_one_with_alternate_key(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_to_different_one_to_many(ChangeMechanism changeMechanism, bool useExistingParent)
        {
            await base.Reparent_to_different_one_to_many(changeMechanism, useExistingParent);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_relationships_are_one_to_one()
        {
            await base.Required_one_to_one_relationships_are_one_to_one();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override Task Required_one_to_one_with_AK_relationships_are_one_to_one()
        {
            return base.Required_one_to_one_with_AK_relationships_are_one_to_one();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_with_alternate_key_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_with_alternate_key_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_with_alternate_key_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_with_alternate_key_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_with_alternate_key_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_with_alternate_key_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_changed_optional_one_to_one(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_changed_optional_one_to_one(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_changed_optional_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_changed_optional_one_to_one_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_changed_optional_one_to_one_with_alternate_key_in_store()
        {
            await base.Save_changed_optional_one_to_one_with_alternate_key_in_store();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_optional_many_to_one_dependents(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_optional_many_to_one_dependents(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_optional_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_optional_many_to_one_dependents_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_removed_optional_many_to_one_dependents(ChangeMechanism changeMechanism)
        {
            await base.Save_removed_optional_many_to_one_dependents(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_removed_optional_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism)
        {
            await base.Save_removed_optional_many_to_one_dependents_with_alternate_key(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_removed_required_many_to_one_dependents(ChangeMechanism changeMechanism)
        {
            await base.Save_removed_required_many_to_one_dependents(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_removed_required_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism)
        {
            await base.Save_removed_required_many_to_one_dependents_with_alternate_key(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_many_to_one_dependents(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_many_to_one_dependents(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_many_to_one_dependents_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_non_PK_one_to_one_changed_by_reference(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_non_PK_one_to_one_changed_by_reference(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_non_PK_one_to_one_changed_by_reference_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_non_PK_one_to_one_changed_by_reference_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_one_to_one_changed_by_reference(ChangeMechanism changeMechanism)
        {
            await base.Save_required_one_to_one_changed_by_reference(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_one_to_one_changed_by_reference_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_one_to_one_changed_by_reference_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_optional_one_to_one(ChangeMechanism changeMechanism)
        {
            await base.Sever_optional_one_to_one(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override Task Sever_optional_one_to_one_with_alternate_key(ChangeMechanism changeMechanism)
        {
            return base.Sever_optional_one_to_one_with_alternate_key(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_required_non_PK_one_to_one(ChangeMechanism changeMechanism)
        {
            await base.Sever_required_non_PK_one_to_one(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_required_non_PK_one_to_one_with_alternate_key(ChangeMechanism changeMechanism)
        {
            await base.Sever_required_non_PK_one_to_one_with_alternate_key(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_required_one_to_one(ChangeMechanism changeMechanism)
        {
            await base.Sever_required_one_to_one(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_required_one_to_one_with_alternate_key(ChangeMechanism changeMechanism)
        {
            await base.Sever_required_one_to_one_with_alternate_key(changeMechanism);
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override Task Sometimes_not_calling_DetectChanges_when_required_does_not_throw_for_null_ref()
        {
            return base.Sometimes_not_calling_DetectChanges_when_required_does_not_throw_for_null_ref();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_two_entity_cycle_with_lazy_loading()
        {
            await base.Save_two_entity_cycle_with_lazy_loading();
        }

        protected override bool DoesLazyLoading
            => true;

        protected override bool DoesChangeTracking
            => false;

        public class ProxyGraphUpdatesWithLazyLoadingDuckDBFixture : ProxyGraphUpdatesDuckDBFixtureBase
        {
            protected override string StoreName
                => "ProxyGraphLazyLoadingUpdatesTest";

            public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
                => base.AddOptions(builder.UseLazyLoadingProxies());

            protected override IServiceCollection AddServices(IServiceCollection serviceCollection)
                => base.AddServices(serviceCollection.AddEntityFrameworkProxies());
        }
    }

    public class ChangeTracking(ChangeTracking.ProxyGraphUpdatesWithChangeTrackingDuckDBFixture fixture)
        : ProxyGraphUpdatesDuckDBTestBase<ChangeTracking.ProxyGraphUpdatesWithChangeTrackingDuckDBFixture>(fixture)
    {
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

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_optional_AK_graph_of_duplicates()
        {
            await base.Can_attach_full_optional_AK_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_optional_graph_of_duplicates()
        {
            await base.Can_attach_full_optional_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_AK_graph_of_duplicates()
        {
            await base.Can_attach_full_required_AK_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_composite_graph_of_duplicates()
        {
            await base.Can_attach_full_required_composite_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_graph_of_duplicates()
        {
            await base.Can_attach_full_required_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_non_PK_AK_graph_of_duplicates()
        {
            await base.Can_attach_full_required_non_PK_AK_graph_of_duplicates();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_non_PK_graph_of_duplicates()
        {
            await base.Can_attach_full_required_non_PK_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_one_to_many_graph_of_duplicates()
        {
            await base.Can_attach_full_required_one_to_many_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_use_record_proxies_with_base_types_to_load_collection()
        {
            await base.Can_use_record_proxies_with_base_types_to_load_collection();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_use_record_proxies_with_base_types_to_load_reference()
        {
            await base.Can_use_record_proxies_with_base_types_to_load_reference();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task No_fixup_to_Deleted_entities()
        {
            await base.No_fixup_to_Deleted_entities();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_are_orphaned(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_are_orphaned_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_are_orphaned_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_are_orphaned_with_Added_graph(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_are_orphaned_with_Added_graph(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_with_alternate_key_are_orphaned(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_with_alternate_key_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_with_alternate_key_are_orphaned_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_with_alternate_key_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_with_alternate_key_are_orphaned_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_with_alternate_key_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_are_orphaned(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_are_orphaned_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_are_orphaned_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_relationships_are_one_to_one()
        {
            await base.Optional_one_to_one_relationships_are_one_to_one();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_with_AK_relationships_are_one_to_one()
        {
            await base.Optional_one_to_one_with_AK_relationships_are_one_to_one();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_with_alternate_key_are_orphaned(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_with_alternate_key_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_with_alternate_key_are_orphaned_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_with_alternate_key_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_with_alternate_key_are_orphaned_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_with_alternate_key_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_one_to_many_overlapping(ChangeMechanism changeMechanism, bool useExistingParent)
        {
            await base.Reparent_one_to_many_overlapping(changeMechanism, useExistingParent);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_optional_one_to_one(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_optional_one_to_one(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_optional_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_optional_one_to_one_with_alternate_key(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_required_non_PK_one_to_one(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_required_non_PK_one_to_one(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_required_non_PK_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_required_non_PK_one_to_one_with_alternate_key(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_required_one_to_one(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_required_one_to_one(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_required_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_required_one_to_one_with_alternate_key(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_to_different_one_to_many(ChangeMechanism changeMechanism, bool useExistingParent)
        {
            await base.Reparent_to_different_one_to_many(changeMechanism, useExistingParent);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_relationships_are_one_to_one()
        {
            await base.Required_one_to_one_relationships_are_one_to_one();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override Task Required_one_to_one_with_AK_relationships_are_one_to_one()
        {
            return base.Required_one_to_one_with_AK_relationships_are_one_to_one();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_with_alternate_key_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_with_alternate_key_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_with_alternate_key_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_with_alternate_key_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_with_alternate_key_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_with_alternate_key_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_changed_optional_one_to_one(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_changed_optional_one_to_one(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_changed_optional_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_changed_optional_one_to_one_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_changed_optional_one_to_one_with_alternate_key_in_store()
        {
            await base.Save_changed_optional_one_to_one_with_alternate_key_in_store();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_optional_many_to_one_dependents(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_optional_many_to_one_dependents(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_optional_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_optional_many_to_one_dependents_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_removed_optional_many_to_one_dependents(ChangeMechanism changeMechanism)
        {
            await base.Save_removed_optional_many_to_one_dependents(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_removed_optional_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism)
        {
            await base.Save_removed_optional_many_to_one_dependents_with_alternate_key(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_removed_required_many_to_one_dependents(ChangeMechanism changeMechanism)
        {
            await base.Save_removed_required_many_to_one_dependents(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_removed_required_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism)
        {
            await base.Save_removed_required_many_to_one_dependents_with_alternate_key(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_many_to_one_dependents(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_many_to_one_dependents(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_many_to_one_dependents_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_non_PK_one_to_one_changed_by_reference(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_non_PK_one_to_one_changed_by_reference(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_non_PK_one_to_one_changed_by_reference_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_non_PK_one_to_one_changed_by_reference_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_one_to_one_changed_by_reference(ChangeMechanism changeMechanism)
        {
            await base.Save_required_one_to_one_changed_by_reference(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_one_to_one_changed_by_reference_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_one_to_one_changed_by_reference_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_optional_one_to_one(ChangeMechanism changeMechanism)
        {
            await base.Sever_optional_one_to_one(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override Task Sever_optional_one_to_one_with_alternate_key(ChangeMechanism changeMechanism)
        {
            return base.Sever_optional_one_to_one_with_alternate_key(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_required_non_PK_one_to_one(ChangeMechanism changeMechanism)
        {
            await base.Sever_required_non_PK_one_to_one(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_required_non_PK_one_to_one_with_alternate_key(ChangeMechanism changeMechanism)
        {
            await base.Sever_required_non_PK_one_to_one_with_alternate_key(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_required_one_to_one(ChangeMechanism changeMechanism)
        {
            await base.Sever_required_one_to_one(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_required_one_to_one_with_alternate_key(ChangeMechanism changeMechanism)
        {
            await base.Sever_required_one_to_one_with_alternate_key(changeMechanism);
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override Task Sometimes_not_calling_DetectChanges_when_required_does_not_throw_for_null_ref()
        {
            return base.Sometimes_not_calling_DetectChanges_when_required_does_not_throw_for_null_ref();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_two_entity_cycle_with_lazy_loading()
        {
            await base.Save_two_entity_cycle_with_lazy_loading();
        }

        protected override bool DoesLazyLoading
            => false;

        protected override bool DoesChangeTracking
            => true;

        public class ProxyGraphUpdatesWithChangeTrackingDuckDBFixture : ProxyGraphUpdatesDuckDBFixtureBase
        {
            protected override string StoreName
                => "ProxyGraphChangeTrackingUpdatesTest";

            public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
                => base.AddOptions(builder.UseChangeTrackingProxies());

            protected override IServiceCollection AddServices(IServiceCollection serviceCollection)
                => base.AddServices(serviceCollection.AddEntityFrameworkProxies());
        }
    }

    public class ChangeTrackingAndLazyLoading(
        ChangeTrackingAndLazyLoading.ProxyGraphUpdatesWithChangeTrackingAndLazyLoadingDuckDBFixture fixture)
        : ProxyGraphUpdatesDuckDBTestBase<
            ChangeTrackingAndLazyLoading.ProxyGraphUpdatesWithChangeTrackingAndLazyLoadingDuckDBFixture>(fixture)
    {
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

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_optional_AK_graph_of_duplicates()
        {
            await base.Can_attach_full_optional_AK_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_optional_graph_of_duplicates()
        {
            await base.Can_attach_full_optional_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_AK_graph_of_duplicates()
        {
            await base.Can_attach_full_required_AK_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_composite_graph_of_duplicates()
        {
            await base.Can_attach_full_required_composite_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_graph_of_duplicates()
        {
            await base.Can_attach_full_required_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_non_PK_AK_graph_of_duplicates()
        {
            await base.Can_attach_full_required_non_PK_AK_graph_of_duplicates();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_non_PK_graph_of_duplicates()
        {
            await base.Can_attach_full_required_non_PK_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_attach_full_required_one_to_many_graph_of_duplicates()
        {
            await base.Can_attach_full_required_one_to_many_graph_of_duplicates();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_use_record_proxies_with_base_types_to_load_collection()
        {
            await base.Can_use_record_proxies_with_base_types_to_load_collection();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Can_use_record_proxies_with_base_types_to_load_reference()
        {
            await base.Can_use_record_proxies_with_base_types_to_load_reference();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task No_fixup_to_Deleted_entities()
        {
            await base.No_fixup_to_Deleted_entities();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_are_orphaned(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_are_orphaned_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_are_orphaned_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_are_orphaned_with_Added_graph(CascadeTiming? cascadeDeleteTiming, CascadeTiming? deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_are_orphaned_with_Added_graph(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_with_alternate_key_are_orphaned(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_with_alternate_key_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_with_alternate_key_are_orphaned_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_with_alternate_key_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_many_to_one_dependents_with_alternate_key_are_orphaned_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_many_to_one_dependents_with_alternate_key_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_are_orphaned(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_are_orphaned_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_are_orphaned_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_relationships_are_one_to_one()
        {
            await base.Optional_one_to_one_relationships_are_one_to_one();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_with_AK_relationships_are_one_to_one()
        {
            await base.Optional_one_to_one_with_AK_relationships_are_one_to_one();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_with_alternate_key_are_orphaned(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_with_alternate_key_are_orphaned(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_with_alternate_key_are_orphaned_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_with_alternate_key_are_orphaned_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Optional_one_to_one_with_alternate_key_are_orphaned_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Optional_one_to_one_with_alternate_key_are_orphaned_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_one_to_many_overlapping(ChangeMechanism changeMechanism, bool useExistingParent)
        {
            await base.Reparent_one_to_many_overlapping(changeMechanism, useExistingParent);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_optional_one_to_one(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_optional_one_to_one(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_optional_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_optional_one_to_one_with_alternate_key(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_required_non_PK_one_to_one(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_required_non_PK_one_to_one(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_required_non_PK_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_required_non_PK_one_to_one_with_alternate_key(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_required_one_to_one(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_required_one_to_one(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_required_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingRoot)
        {
            await base.Reparent_required_one_to_one_with_alternate_key(changeMechanism, useExistingRoot);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Reparent_to_different_one_to_many(ChangeMechanism changeMechanism, bool useExistingParent)
        {
            await base.Reparent_to_different_one_to_many(changeMechanism, useExistingParent);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_many_to_one_dependents_with_alternate_key_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_many_to_one_dependents_with_alternate_key_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_non_PK_one_to_one_with_alternate_key_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_non_PK_one_to_one_with_alternate_key_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_relationships_are_one_to_one()
        {
            await base.Required_one_to_one_relationships_are_one_to_one();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override Task Required_one_to_one_with_AK_relationships_are_one_to_one()
        {
            return base.Required_one_to_one_with_AK_relationships_are_one_to_one();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_with_alternate_key_are_cascade_deleted(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_with_alternate_key_are_cascade_deleted(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_with_alternate_key_are_cascade_deleted_in_store(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_with_alternate_key_are_cascade_deleted_in_store(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_with_alternate_key_are_cascade_deleted_starting_detached(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Required_one_to_one_with_alternate_key_are_cascade_detached_when_Added(CascadeTiming cascadeDeleteTiming, CascadeTiming deleteOrphansTiming)
        {
            await base.Required_one_to_one_with_alternate_key_are_cascade_detached_when_Added(cascadeDeleteTiming, deleteOrphansTiming);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_changed_optional_one_to_one(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_changed_optional_one_to_one(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_changed_optional_one_to_one_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_changed_optional_one_to_one_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_changed_optional_one_to_one_with_alternate_key_in_store()
        {
            await base.Save_changed_optional_one_to_one_with_alternate_key_in_store();
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_optional_many_to_one_dependents(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_optional_many_to_one_dependents(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_optional_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_optional_many_to_one_dependents_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_removed_optional_many_to_one_dependents(ChangeMechanism changeMechanism)
        {
            await base.Save_removed_optional_many_to_one_dependents(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_removed_optional_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism)
        {
            await base.Save_removed_optional_many_to_one_dependents_with_alternate_key(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_removed_required_many_to_one_dependents(ChangeMechanism changeMechanism)
        {
            await base.Save_removed_required_many_to_one_dependents(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_removed_required_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism)
        {
            await base.Save_removed_required_many_to_one_dependents_with_alternate_key(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_many_to_one_dependents(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_many_to_one_dependents(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_many_to_one_dependents_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_many_to_one_dependents_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_non_PK_one_to_one_changed_by_reference(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_non_PK_one_to_one_changed_by_reference(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_non_PK_one_to_one_changed_by_reference_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_non_PK_one_to_one_changed_by_reference_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_one_to_one_changed_by_reference(ChangeMechanism changeMechanism)
        {
            await base.Save_required_one_to_one_changed_by_reference(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_required_one_to_one_changed_by_reference_with_alternate_key(ChangeMechanism changeMechanism, bool useExistingEntities)
        {
            await base.Save_required_one_to_one_changed_by_reference_with_alternate_key(changeMechanism, useExistingEntities);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_optional_one_to_one(ChangeMechanism changeMechanism)
        {
            await base.Sever_optional_one_to_one(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override Task Sever_optional_one_to_one_with_alternate_key(ChangeMechanism changeMechanism)
        {
            return base.Sever_optional_one_to_one_with_alternate_key(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_required_non_PK_one_to_one(ChangeMechanism changeMechanism)
        {
            await base.Sever_required_non_PK_one_to_one(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_required_non_PK_one_to_one_with_alternate_key(ChangeMechanism changeMechanism)
        {
            await base.Sever_required_non_PK_one_to_one_with_alternate_key(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_required_one_to_one(ChangeMechanism changeMechanism)
        {
            await base.Sever_required_one_to_one(changeMechanism);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Sever_required_one_to_one_with_alternate_key(ChangeMechanism changeMechanism)
        {
            await base.Sever_required_one_to_one_with_alternate_key(changeMechanism);
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override Task Sometimes_not_calling_DetectChanges_when_required_does_not_throw_for_null_ref()
        {
            return base.Sometimes_not_calling_DetectChanges_when_required_does_not_throw_for_null_ref();
        }

        [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Save_two_entity_cycle_with_lazy_loading()
        {
            await base.Save_two_entity_cycle_with_lazy_loading();
        }

        protected override bool DoesLazyLoading
            => true;

        protected override bool DoesChangeTracking
            => true;

        public class ProxyGraphUpdatesWithChangeTrackingAndLazyLoadingDuckDBFixture : ProxyGraphUpdatesDuckDBFixtureBase
        {
            protected override string StoreName
                => "ProxyGraphChangeTrackingAndLazyLoadingUpdatesTest";

            public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
                => base.AddOptions(builder.UseChangeTrackingProxies().UseLazyLoadingProxies());

            protected override IServiceCollection AddServices(IServiceCollection serviceCollection)
                => base.AddServices(serviceCollection.AddEntityFrameworkProxies());
        }
    }
}