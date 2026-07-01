using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class ManyToManyLoadProxyDuckDBTest(ManyToManyLoadProxyDuckDBTest.ManyToManyLoadProxyDuckDBFixture fixture) : ManyToManyLoadDuckDBTestBase<ManyToManyLoadProxyDuckDBTest.ManyToManyLoadProxyDuckDBFixture>(fixture)
{
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
    public override void Attached_collections_are_not_marked_as_loaded(EntityState state, bool lazy)
    {
        base.Attached_collections_are_not_marked_as_loaded(state, lazy);
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
    public override async Task Load_collection_partially_loaded(EntityState state, bool forceIdentityResolution, bool async)
    {
        await base.Load_collection_partially_loaded(state, forceIdentityResolution, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_partially_loaded_no_explicit_join(EntityState state, bool forceIdentityResolution, bool async)
    {
        await base.Load_collection_partially_loaded_no_explicit_join(state, forceIdentityResolution, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Load_collection_partially_loaded_no_tracking(QueryTrackingBehavior queryTrackingBehavior)
    {
        base.Load_collection_partially_loaded_no_tracking(queryTrackingBehavior);
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

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_unidirectional(EntityState state, QueryTrackingBehavior queryTrackingBehavior, bool async)
    {
        await base.Load_collection_unidirectional(state, queryTrackingBehavior, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_unidirectional(EntityState state, bool async)
    {
        await base.Load_collection_using_Query_unidirectional(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Attached_collections_are_not_marked_as_loaded_unidirectional(EntityState state, bool lazy)
    {
        base.Attached_collections_are_not_marked_as_loaded_unidirectional(state, lazy);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_already_loaded_unidirectional(EntityState state, bool async)
    {
        await base.Load_collection_already_loaded_unidirectional(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_already_loaded_unidirectional(EntityState state, bool async)
    {
        await base.Load_collection_using_Query_already_loaded_unidirectional(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_untyped_unidirectional(EntityState state, bool async)
    {
        await base.Load_collection_untyped_unidirectional(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_untyped_unidirectional(EntityState state, bool async)
    {
        await base.Load_collection_using_Query_untyped_unidirectional(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_not_found_untyped_unidirectional(EntityState state, bool async)
    {
        await base.Load_collection_not_found_untyped_unidirectional(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_not_found_untyped_unidirectional(EntityState state, bool async)
    {
        await base.Load_collection_using_Query_not_found_untyped_unidirectional(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_already_loaded_untyped_unidirectional(EntityState state, bool async,
        CascadeTiming deleteOrphansTiming)
    {
        await base.Load_collection_already_loaded_untyped_unidirectional(state, async, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_already_loaded_untyped_unidirectional(EntityState state, bool async,
        CascadeTiming deleteOrphansTiming)
    {
        await base.Load_collection_using_Query_already_loaded_untyped_unidirectional(state, async, deleteOrphansTiming);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_composite_key_unidirectional(EntityState state, bool async)
    {
        await base.Load_collection_composite_key_unidirectional(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_composite_key_unidirectional(EntityState state, bool async)
    {
        await base.Load_collection_using_Query_composite_key_unidirectional(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_for_detached_throws_unidirectional(bool async, QueryTrackingBehavior queryTrackingBehavior)
    {
        await base.Load_collection_for_detached_throws_unidirectional(async, queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Query_collection_for_detached_throws_unidirectional(QueryTrackingBehavior queryTrackingBehavior)
    {
        base.Query_collection_for_detached_throws_unidirectional(queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_with_Include_unidirectional(bool async)
    {
        await base.Load_collection_using_Query_with_Include_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_with_Include_for_inverse_unidirectional(bool async)
    {
        await base.Load_collection_using_Query_with_Include_for_inverse_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_with_filtered_Include_unidirectional(bool async)
    {
        await base.Load_collection_using_Query_with_filtered_Include_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_with_filtered_Include_and_projection_unidirectional(bool async)
    {
        await base.Load_collection_using_Query_with_filtered_Include_and_projection_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_collection_using_Query_with_join_unidirectional(bool async)
    {
        await base.Load_collection_using_Query_with_join_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Query_with_Include_marks_only_left_as_loaded_unidirectional(bool async)
    {
        await base.Query_with_Include_marks_only_left_as_loaded_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Query_with_filtered_Include_marks_only_left_as_loaded_unidirectional(bool async)
    {
        await base.Query_with_filtered_Include_marks_only_left_as_loaded_unidirectional(async);
    }

    protected override bool ExpectLazyLoading
        => true;

    public class ManyToManyLoadProxyDuckDBFixture : ManyToManyLoadDuckDBFixtureBase
    {
        protected override string StoreName
            => "ManyToManyLoadProxies";

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).UseLazyLoadingProxies();

        protected override IServiceCollection AddServices(IServiceCollection serviceCollection)
            => base.AddServices(serviceCollection.AddEntityFrameworkProxies());
    }
}