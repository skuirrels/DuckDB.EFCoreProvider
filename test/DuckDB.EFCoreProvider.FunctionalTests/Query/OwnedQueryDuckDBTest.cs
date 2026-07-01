using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class OwnedQueryDuckDBTest : OwnedQueryRelationalTestBase<OwnedQueryDuckDBTest.OwnedQueryDuckDBFixture>
{
    public OwnedQueryDuckDBTest(OwnedQueryDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_query_on_indexer_properties_split(bool async)
    {
        return base.Can_query_on_indexer_properties_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_multiple_owned_navigations_split(bool async)
    {
        return base.Project_multiple_owned_navigations_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_for_base_type_loads_all_owned_navs_split(bool async)
    {
        return base.Query_for_base_type_loads_all_owned_navs_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_for_branch_type_loads_all_owned_navs_split(bool async)
    {
        return base.Query_for_branch_type_loads_all_owned_navs_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_when_subquery_split(bool async)
    {
        return base.Query_when_subquery_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_with_OfType_eagerly_loads_correct_owned_navigations_split(bool async)
    {
        return base.Query_with_OfType_eagerly_loads_correct_owned_navigations_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Any_with_predicate_over_owned_collection(bool async)
    {
        return base.Any_with_predicate_over_owned_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Any_without_predicate_over_owned_collection(bool async)
    {
        return base.Any_without_predicate_over_owned_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_OrderBy_indexer_properties(bool async)
    {
        return base.Can_OrderBy_indexer_properties(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_query_on_indexer_properties(bool async)
    {
        return base.Can_query_on_indexer_properties(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_query_owner_with_different_owned_types_having_same_property_name_in_hierarchy(bool async)
    {
        return base.Can_query_owner_with_different_owned_types_having_same_property_name_in_hierarchy(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Client_method_skip_loads_owned_navigations(bool async)
    {
        return base.Client_method_skip_loads_owned_navigations(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Client_method_skip_loads_owned_navigations_variation_2(bool async)
    {
        return base.Client_method_skip_loads_owned_navigations_variation_2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Client_method_skip_take_loads_owned_navigations(bool async)
    {
        return base.Client_method_skip_take_loads_owned_navigations(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Client_method_skip_take_loads_owned_navigations_variation_2(bool async)
    {
        return base.Client_method_skip_take_loads_owned_navigations_variation_2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Client_method_take_loads_owned_navigations(bool async)
    {
        return base.Client_method_take_loads_owned_navigations(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Client_method_take_loads_owned_navigations_variation_2(bool async)
    {
        return base.Client_method_take_loads_owned_navigations_variation_2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Count_over_owned_collection(bool async)
    {
        return base.Count_over_owned_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Distinct_over_owned_collection(bool async)
    {
        return base.Distinct_over_owned_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_on_indexer_using_closure(bool async)
    {
        return base.Filter_on_indexer_using_closure(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Left_join_on_entity_with_owned_navigations(bool async)
    {
        return base.Left_join_on_entity_with_owned_navigations(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Left_join_on_entity_with_owned_navigations_complex(bool async)
    {
        return base.Left_join_on_entity_with_owned_navigations_complex(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_another_reference(bool async)
    {
        return base.Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_another_reference(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_another_reference_in_predicate_and_projection(bool async)
    {
        return base.Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_another_reference_in_predicate_and_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Navigation_rewrite_on_owned_reference_followed_by_regular_entity_filter(bool async)
    {
        return base.Navigation_rewrite_on_owned_reference_followed_by_regular_entity_filter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Navigation_rewrite_on_owned_reference_projecting_entity(bool async)
    {
        return base.Navigation_rewrite_on_owned_reference_projecting_entity(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task NoTracking_Include_with_cycles_throws(bool async)
    {
        return base.NoTracking_Include_with_cycles_throws(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task OrderBy_ElementAt_over_owned_collection(bool async)
    {
        return base.OrderBy_ElementAt_over_owned_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Ordering_by_identifying_projection(bool async)
    {
        return base.Ordering_by_identifying_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Preserve_includes_when_applying_skip_take_after_anonymous_type_select(bool async)
    {
        return base.Preserve_includes_when_applying_skip_take_after_anonymous_type_select(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_multiple_owned_navigations(bool async)
    {
        return base.Project_multiple_owned_navigations(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_owned_reference_navigation_which_owns_additional(bool async)
    {
        return base.Project_owned_reference_navigation_which_owns_additional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_for_base_type_loads_all_owned_navs(bool async)
    {
        return base.Query_for_base_type_loads_all_owned_navs(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_for_branch_type_loads_all_owned_navs(bool async)
    {
        return base.Query_for_branch_type_loads_all_owned_navs(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_for_branch_type_loads_all_owned_navs_tracking(bool async)
    {
        return base.Query_for_branch_type_loads_all_owned_navs_tracking(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_for_leaf_type_loads_all_owned_navs(bool async)
    {
        return base.Query_for_leaf_type_loads_all_owned_navs(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_loads_reference_nav_automatically_in_projection(bool async)
    {
        return base.Query_loads_reference_nav_automatically_in_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_when_subquery(bool async)
    {
        return base.Query_when_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_with_OfType_eagerly_loads_correct_owned_navigations(bool async)
    {
        return base.Query_with_OfType_eagerly_loads_correct_owned_navigations(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Simple_query_entity_with_owned_collection(bool async)
    {
        return base.Simple_query_entity_with_owned_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Union_over_owned_collection(bool async)
    {
        return base.Union_over_owned_collection(async);
    }

    public class OwnedQueryDuckDBFixture : RelationalOwnedQueryFixture
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
