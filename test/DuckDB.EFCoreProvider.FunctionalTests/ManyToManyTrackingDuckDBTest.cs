using Microsoft.EntityFrameworkCore.TestModels.ManyToManyModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

// TODO Many_to_many_delete_behaviors_are_set - is not virtual
public abstract class ManyToManyTrackingDuckDBTest : ManyToManyTrackingRelationalTestBase<ManyToManyTrackingDuckDBTest.ManyToManyTrackingDuckDBFixture>
{
    protected ManyToManyTrackingDuckDBTest(ManyToManyTrackingDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_composite_with_navs(bool async)
    {
        await base.Can_insert_many_to_many_composite_with_navs(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_composite_with_navs()
    {
        await base.Can_update_many_to_many_composite_with_navs();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_delete_with_many_to_many_composite_with_navs()
    {
        await base.Can_delete_with_many_to_many_composite_with_navs();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_composite_shared_with_navs(bool async)
    {
        await base.Can_insert_many_to_many_composite_shared_with_navs(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_composite_shared_with_navs()
    {
        await base.Can_update_many_to_many_composite_shared_with_navs();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_delete_with_many_to_many_composite_shared_with_navs()
    {
        await base.Can_delete_with_many_to_many_composite_shared_with_navs();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_composite_additional_pk_with_navs(bool async)
    {
        await base.Can_insert_many_to_many_composite_additional_pk_with_navs(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_composite_additional_pk_with_navs()
    {
        await base.Can_update_many_to_many_composite_additional_pk_with_navs();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_delete_with_many_to_many_composite_additional_pk_with_navs()
    {
        await base.Can_delete_with_many_to_many_composite_additional_pk_with_navs();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_self_shared(bool async)
    {
        await base.Can_insert_many_to_many_self_shared(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_self()
    {
        await base.Can_update_many_to_many_self();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_with_navs(bool async)
    {
        await base.Can_insert_many_to_many_with_navs(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_with_navs()
    {
        await base.Can_update_many_to_many_with_navs();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_with_inheritance(bool async)
    {
        await base.Can_insert_many_to_many_with_inheritance(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_with_inheritance()
    {
        await base.Can_update_many_to_many_with_inheritance();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_self_with_payload(bool async)
    {
        await base.Can_insert_many_to_many_self_with_payload(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_self_with_payload()
    {
        await base.Can_update_many_to_many_self_with_payload();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_shared_with_payload(bool async)
    {
        await base.Can_insert_many_to_many_shared_with_payload(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_shared_with_payload()
    {
        await base.Can_update_many_to_many_shared_with_payload();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_shared(bool async)
    {
        await base.Can_insert_many_to_many_shared(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_shared()
    {
        await base.Can_update_many_to_many_shared();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_with_payload(bool async)
    {
        await base.Can_insert_many_to_many_with_payload(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_with_payload()
    {
        await base.Can_update_many_to_many_with_payload();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_delete_with_many_to_many_with_navs()
    {
        await base.Can_delete_with_many_to_many_with_navs();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many(bool async)
    {
        await base.Can_insert_many_to_many(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_with_suspected_dangling_join(bool async, bool useTrackGraph, bool useDetectChanges)
    {
        await base.Can_insert_many_to_many_with_suspected_dangling_join(async, useTrackGraph, useDetectChanges);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_with_dangling_join(bool async, bool useTrackGraph, bool useDetectChanges)
    {
        await base.Can_insert_many_to_many_with_dangling_join(async, useTrackGraph, useDetectChanges);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many()
    {
        await base.Can_update_many_to_many();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_delete_with_many_to_many()
    {
        await base.Can_delete_with_many_to_many();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_fully_by_convention(bool async)
    {
        await base.Can_insert_many_to_many_fully_by_convention(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_fully_by_convention_generated_keys(bool async)
    {
        await base.Can_insert_many_to_many_fully_by_convention_generated_keys(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_Attach_or_Update_a_many_to_many_with_mixed_set_and_unset_keys(bool useUpdate, bool async)
    {
        await base.Can_Attach_or_Update_a_many_to_many_with_mixed_set_and_unset_keys(useUpdate, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Initial_tracking_uses_skip_navigations(bool async)
    {
        await base.Initial_tracking_uses_skip_navigations(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_load_entities_in_any_order(int[] order)
    {
        base.Can_load_entities_in_any_order(order);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_update_delete_shared_type_entity_type()
    {
        await base.Can_insert_update_delete_shared_type_entity_type();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_update_delete_proxyable_shared_type_entity_type()
    {
        await base.Can_insert_update_delete_proxyable_shared_type_entity_type();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_with_navs_by_join_entity(bool async)
    {
        await base.Can_insert_many_to_many_with_navs_by_join_entity(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_add_and_remove_a_new_relationship(bool modifyLeft, bool modifyRight, bool useJoin, bool useNavs)
    {
        await base.Can_add_and_remove_a_new_relationship(modifyLeft, modifyRight, useJoin, useNavs);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_add_and_remove_a_new_relationship_self(bool modifyLeft, bool modifyRight, bool useJoin)
    {
        await base.Can_add_and_remove_a_new_relationship_self(modifyLeft, modifyRight, useJoin);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_add_and_remove_a_new_relationship_composite_with_navs(bool modifyLeft, bool modifyRight, bool useJoin,
        bool useNavs)
    {
        await base.Can_add_and_remove_a_new_relationship_composite_with_navs(modifyLeft, modifyRight, useJoin, useNavs);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_add_and_remove_a_new_relationship_composite_additional_pk_with_navs(bool modifyLeft, bool modifyRight,
        bool useJoin, bool useNavs)
    {
        await base.Can_add_and_remove_a_new_relationship_composite_additional_pk_with_navs(modifyLeft, modifyRight, useJoin, useNavs);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_add_and_remove_a_new_relationship_with_inheritance(bool modifyLeft, bool modifyRight, bool useJoin)
    {
        await base.Can_add_and_remove_a_new_relationship_with_inheritance(modifyLeft, modifyRight, useJoin);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_add_and_remove_a_new_relationship_shared_with_payload(bool modifyLeft, bool modifyRight, bool useJoin)
    {
        await base.Can_add_and_remove_a_new_relationship_shared_with_payload(modifyLeft, modifyRight, useJoin);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_add_and_remove_a_new_relationship_shared(bool modifyLeft, bool modifyRight, bool useJoin)
    {
        await base.Can_add_and_remove_a_new_relationship_shared(modifyLeft, modifyRight, useJoin);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_add_and_remove_a_new_relationship_with_payload(bool modifyLeft, bool modifyRight, bool useJoin, bool useNavs)
    {
        await base.Can_add_and_remove_a_new_relationship_with_payload(modifyLeft, modifyRight, useJoin, useNavs);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_replace_dependent_with_many_to_many(bool createNewCollection, bool async)
    {
        await base.Can_replace_dependent_with_many_to_many(createNewCollection, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_composite_with_navs_unidirectional(bool async)
    {
        await base.Can_insert_many_to_many_composite_with_navs_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_composite_with_navs_unidirectional()
    {
        await base.Can_update_many_to_many_composite_with_navs_unidirectional();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_delete_with_many_to_many_composite_with_navs_unidirectional()
    {
        await base.Can_delete_with_many_to_many_composite_with_navs_unidirectional();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_composite_additional_pk_with_navs_unidirectional(bool async)
    {
        await base.Can_insert_many_to_many_composite_additional_pk_with_navs_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_composite_additional_pk_with_navs_unidirectional()
    {
        await base.Can_update_many_to_many_composite_additional_pk_with_navs_unidirectional();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_delete_with_many_to_many_composite_additional_pk_with_navs_unidirectional()
    {
        await base.Can_delete_with_many_to_many_composite_additional_pk_with_navs_unidirectional();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_self_shared_unidirectional(bool async)
    {
        await base.Can_insert_many_to_many_self_shared_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_self_unidirectional()
    {
        await base.Can_update_many_to_many_self_unidirectional();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_with_inheritance_unidirectional(bool async)
    {
        await base.Can_insert_many_to_many_with_inheritance_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_with_inheritance_unidirectional()
    {
        await base.Can_update_many_to_many_with_inheritance_unidirectional();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_self_with_payload_unidirectional(bool async)
    {
        await base.Can_insert_many_to_many_self_with_payload_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_self_with_payload_unidirectional()
    {
        await base.Can_update_many_to_many_self_with_payload_unidirectional();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_shared_with_payload_unidirectional(bool async)
    {
        await base.Can_insert_many_to_many_shared_with_payload_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_shared_with_payload_unidirectional()
    {
        await base.Can_update_many_to_many_shared_with_payload_unidirectional();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_shared_unidirectional(bool async)
    {
        await base.Can_insert_many_to_many_shared_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_many_to_many_shared_unidirectional()
    {
        await base.Can_update_many_to_many_shared_unidirectional();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_with_suspected_dangling_join_unidirectional(bool async, bool useTrackGraph,
        bool useDetectChanges)
    {
        await base.Can_insert_many_to_many_with_suspected_dangling_join_unidirectional(async, useTrackGraph, useDetectChanges);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_with_dangling_join_unidirectional(bool async, bool useTrackGraph, bool useDetectChanges)
    {
        await base.Can_insert_many_to_many_with_dangling_join_unidirectional(async, useTrackGraph, useDetectChanges);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_update_delete_proxyable_shared_type_entity_type_unidirectional()
    {
        await base.Can_insert_update_delete_proxyable_shared_type_entity_type_unidirectional();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_many_to_many_with_navs_by_join_entity_unidirectional(bool async)
    {
        await base.Can_insert_many_to_many_with_navs_by_join_entity_unidirectional(async);
    }

    public class ManyToManyTrackingDuckDBFixture : ManyToManyTrackingRelationalFixture, ITestSqlLoggerFactory
    {
        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder
                .Entity<JoinOneSelfPayload>()
                .Property(e => e.Payload)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            modelBuilder
                .SharedTypeEntity<Dictionary<string, object>>("JoinOneToThreePayloadFullShared")
                .IndexerProperty<string>("Payload")
                .HasDefaultValue("Generated");

            modelBuilder
                .Entity<JoinOneToThreePayloadFull>()
                .Property(e => e.Payload)
                .HasDefaultValue("Generated");

            modelBuilder
                .Entity<UnidirectionalJoinOneSelfPayload>()
                .Property(e => e.Payload)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            modelBuilder
                .SharedTypeEntity<Dictionary<string, object>>("UnidirectionalJoinOneToThreePayloadFullShared")
                .IndexerProperty<string>("Payload")
                .HasDefaultValue("Generated");

            modelBuilder
                .Entity<UnidirectionalJoinOneToThreePayloadFull>()
                .Property(e => e.Payload)
                .HasDefaultValue("Generated");
        }
    }
}