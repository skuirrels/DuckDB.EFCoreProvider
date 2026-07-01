using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.TestModels.ManyToManyFieldsModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class ManyToManyFieldsLoadDuckDBTest : ManyToManyFieldsLoadTestBase<ManyToManyFieldsLoadDuckDBTest.ManyToManyFieldsLoadDuckDBFixture>
{
    public ManyToManyFieldsLoadDuckDBTest(ManyToManyFieldsLoadDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection(EntityState state, QueryTrackingBehavior queryTrackingBehavior, bool async)
    {
        await base.Load_collection(state, queryTrackingBehavior, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query(EntityState state, bool async)
    {
        await base.Load_collection_using_Query(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Attached_collections_are_not_marked_as_loaded(EntityState state)
    {
        base.Attached_collections_are_not_marked_as_loaded(state);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_already_loaded(EntityState state, bool async)
    {
        await base.Load_collection_already_loaded(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_already_loaded(EntityState state, bool async)
    {
        await base.Load_collection_using_Query_already_loaded(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_untyped(EntityState state, bool async)
    {
        await base.Load_collection_untyped(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_untyped(EntityState state, bool async)
    {
        await base.Load_collection_using_Query_untyped(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_not_found_untyped(EntityState state, bool async)
    {
        await base.Load_collection_not_found_untyped(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_not_found_untyped(EntityState state, bool async)
    {
        await base.Load_collection_using_Query_not_found_untyped(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_already_loaded_untyped(EntityState state, bool async, CascadeTiming deleteOrphansTiming)
    {
        await base.Load_collection_already_loaded_untyped(state, async, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_already_loaded_untyped(EntityState state, bool async,
        CascadeTiming deleteOrphansTiming)
    {
        await base.Load_collection_using_Query_already_loaded_untyped(state, async, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_composite_key(EntityState state, bool async)
    {
        await base.Load_collection_composite_key(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_composite_key(EntityState state, bool async)
    {
        await base.Load_collection_using_Query_composite_key(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_for_detached_throws(bool async, QueryTrackingBehavior queryTrackingBehavior)
    {
        await base.Load_collection_for_detached_throws(async, queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Query_collection_for_detached_throws(QueryTrackingBehavior queryTrackingBehavior)
    {
        base.Query_collection_for_detached_throws(queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_with_Include(bool async)
    {
        await base.Load_collection_using_Query_with_Include(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_with_Include_for_inverse(bool async)
    {
        await base.Load_collection_using_Query_with_Include_for_inverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_with_Include_for_same_collection(bool async)
    {
        await base.Load_collection_using_Query_with_Include_for_same_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_with_filtered_Include(bool async)
    {
        await base.Load_collection_using_Query_with_filtered_Include(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_with_filtered_Include_and_projection(bool async)
    {
        await base.Load_collection_using_Query_with_filtered_Include_and_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_with_join(bool async)
    {
        await base.Load_collection_using_Query_with_join(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Query_with_Include_marks_only_left_as_loaded(bool async)
    {
        await base.Query_with_Include_marks_only_left_as_loaded(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Query_with_filtered_Include_marks_only_left_as_loaded(bool async)
    {
        await base.Query_with_filtered_Include_marks_only_left_as_loaded(async);
    }

    public class ManyToManyFieldsLoadDuckDBFixture : ManyToManyFieldsLoadFixtureBase, ITestSqlLoggerFactory
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
        }
    }
}