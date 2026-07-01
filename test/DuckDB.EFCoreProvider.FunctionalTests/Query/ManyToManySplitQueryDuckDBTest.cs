using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class ManyToManySplitQueryDuckDBTest : ManyToManyQueryRelationalTestBase<ManyToManySplitQueryDuckDBFixture>
{
    public ManyToManySplitQueryDuckDBTest(ManyToManySplitQueryDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_include_on_skip_navigation_combined_split(bool async)
    {
        return base.Filter_include_on_skip_navigation_combined_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_include_on_skip_navigation_combined(bool async)
    {
        return base.Filter_include_on_skip_navigation_combined(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_on_navigation_then_filtered_include_on_skip_navigation(bool async)
    {
        return base.Filtered_include_on_navigation_then_filtered_include_on_skip_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_include_on_skip_navigation_combined_unidirectional(bool async)
    {
        return base.Filter_include_on_skip_navigation_combined_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_on_skip_navigation_then_filtered_include_on_navigation(bool async)
    {
        return base.Filtered_include_on_skip_navigation_then_filtered_include_on_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_on_skip_navigation_then_filtered_include_on_navigation_split(bool async)
    {
        return base.Filtered_include_on_skip_navigation_then_filtered_include_on_navigation_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_on_navigation_then_filtered_include_on_skip_navigation_split(bool async)
    {
        return base.Filtered_include_on_navigation_then_filtered_include_on_skip_navigation_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_include_on_skip_navigation_combined_with_filtered_then_includes(bool async)
    {
        return base.Filter_include_on_skip_navigation_combined_with_filtered_then_includes(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_include_on_skip_navigation_combined_with_filtered_then_includes_split(bool async)
    {
        return base.Filter_include_on_skip_navigation_combined_with_filtered_then_includes_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_skip(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_skip(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_skip_split(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_skip_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_split(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_skip_take_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_split(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_split(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_take(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_take(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_take_split(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_take_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_take_unidirectional(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_take_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_unidirectional(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_where(bool async)
    {
        return base.Filtered_include_skip_navigation_where(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_where_split(bool async)
    {
        return base.Filtered_include_skip_navigation_where_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_where_then_include_skip_navigation(bool async)
    {
        return base.Filtered_include_skip_navigation_where_then_include_skip_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_where_then_include_skip_navigation_split(bool async)
    {
        return base.Filtered_include_skip_navigation_where_then_include_skip_navigation_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_where_then_include_skip_navigation_unidirectional(bool async)
    {
        return base.Filtered_include_skip_navigation_where_then_include_skip_navigation_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_where_unidirectional(bool async)
    {
        return base.Filtered_include_skip_navigation_where_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_then_include_skip_navigation_where(bool async)
    {
        return base.Filtered_then_include_skip_navigation_where(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_then_include_skip_navigation_where_split(bool async)
    {
        return base.Filtered_then_include_skip_navigation_where_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_skip_take(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_skip_take(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_where_then_include_skip_navigation_order_by_skip_take_split(bool async)
    {
        return base.Filtered_include_skip_navigation_where_then_include_skip_navigation_order_by_skip_take_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_then_include_skip_navigation_order_by_skip_take(bool async)
    {
        return base.Filtered_then_include_skip_navigation_order_by_skip_take(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_then_include_skip_navigation_order_by_skip_take_split(bool async)
    {
        return base.Filtered_then_include_skip_navigation_order_by_skip_take_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_and_reference_split(bool async)
    {
        return base.Include_skip_navigation_and_reference_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_split(bool async)
    {
        return base.Include_skip_navigation_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_then_include_reference_and_skip_navigation_split(bool async)
    {
        return base.Include_skip_navigation_then_include_reference_and_skip_navigation_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_then_include_skip_navigation_split(bool async)
    {
        return base.Include_skip_navigation_then_include_skip_navigation_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_then_reference_split(bool async)
    {
        return base.Include_skip_navigation_then_reference_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_then_include_inverse_works_for_tracking_query(bool async)
    {
        return base.Include_skip_navigation_then_include_inverse_works_for_tracking_query(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_on_skip_collection_navigation(bool async)
    {
        return base.Contains_on_skip_collection_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_on_skip_collection_navigation_unidirectional(bool async)
    {
        return base.Contains_on_skip_collection_navigation_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_EF_Property(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_skip_take_unidirectional(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_skip_take_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_skip_unidirectional(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_skip_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_where_then_include_skip_navigation_order_by_skip_take(bool async)
    {
        return base.Filtered_include_skip_navigation_where_then_include_skip_navigation_order_by_skip_take(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_skip_navigation_order_by_take_EF_Property(bool async)
    {
        return base.Filtered_include_skip_navigation_order_by_take_EF_Property(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_base_type(bool async)
    {
        return base.GetType_in_hierarchy_in_base_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_base_type_unidirectional(bool async)
    {
        return base.GetType_in_hierarchy_in_base_type_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_intermediate_type(bool async)
    {
        return base.GetType_in_hierarchy_in_intermediate_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_intermediate_type_unidirectional(bool async)
    {
        return base.GetType_in_hierarchy_in_intermediate_type_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_leaf_type(bool async)
    {
        return base.GetType_in_hierarchy_in_leaf_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_leaf_type_unidirectional(bool async)
    {
        return base.GetType_in_hierarchy_in_leaf_type_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_querying_base_type(bool async)
    {
        return base.GetType_in_hierarchy_in_querying_base_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_querying_base_type_unidirectional(bool async)
    {
        return base.GetType_in_hierarchy_in_querying_base_type_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation(bool async)
    {
        return base.Include_skip_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_and_reference(bool async)
    {
        return base.Include_skip_navigation_and_reference(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_and_reference_unidirectional(bool async)
    {
        return base.Include_skip_navigation_and_reference_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_then_include_inverse_works_for_tracking_query_unidirectional(bool async)
    {
        return base.Include_skip_navigation_then_include_inverse_works_for_tracking_query_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_then_include_reference_and_skip_navigation(bool async)
    {
        return base.Include_skip_navigation_then_include_reference_and_skip_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_then_include_reference_and_skip_navigation_unidirectional(bool async)
    {
        return base.Include_skip_navigation_then_include_reference_and_skip_navigation_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_then_include_skip_navigation(bool async)
    {
        return base.Include_skip_navigation_then_include_skip_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_then_include_skip_navigation_unidirectional(bool async)
    {
        return base.Include_skip_navigation_then_include_skip_navigation_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_then_reference(bool async)
    {
        return base.Include_skip_navigation_then_reference(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_then_reference_unidirectional(bool async)
    {
        return base.Include_skip_navigation_then_reference_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_skip_navigation_unidirectional(bool async)
    {
        return base.Include_skip_navigation_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Join_with_skip_navigation(bool async)
    {
        return base.Join_with_skip_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Join_with_skip_navigation_unidirectional(bool async)
    {
        return base.Join_with_skip_navigation_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Left_join_with_skip_navigation(bool async)
    {
        return base.Left_join_with_skip_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Left_join_with_skip_navigation_unidirectional(bool async)
    {
        return base.Left_join_with_skip_navigation_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation(bool async)
    {
        return base.Select_many_over_skip_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_cast(bool async)
    {
        return base.Select_many_over_skip_navigation_cast(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_cast_unidirectional(bool async)
    {
        return base.Select_many_over_skip_navigation_cast_unidirectional(async);
    }
    
    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_unidirectional(bool async)
    {
        return base.Select_many_over_skip_navigation_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_of_type(bool async)
    {
        return base.Select_many_over_skip_navigation_of_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_order_by_skip(bool async)
    {
        return base.Select_many_over_skip_navigation_order_by_skip(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_order_by_skip_take(bool async)
    {
        return base.Select_many_over_skip_navigation_order_by_skip_take(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_order_by_skip_take_unidirectional(bool async)
    {
        return base.Select_many_over_skip_navigation_order_by_skip_take_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_order_by_take(bool async)
    {
        return base.Select_many_over_skip_navigation_order_by_take(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_order_by_take_unidirectional(bool async)
    {
        return base.Select_many_over_skip_navigation_order_by_take_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_where(bool async)
    {
        return base.Select_many_over_skip_navigation_where(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_where_non_equality(bool async)
    {
        return base.Select_many_over_skip_navigation_where_non_equality(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_where_non_equality_unidirectional(bool async)
    {
        return base.Select_many_over_skip_navigation_where_non_equality_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_many_over_skip_navigation_where_unidirectional(bool async)
    {
        return base.Select_many_over_skip_navigation_where_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_select_many_average(bool async)
    {
        return base.Skip_navigation_select_many_average(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_skip_navigation_first_or_default(bool async)
    {
        return base.Select_skip_navigation_first_or_default(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_skip_navigation(bool async)
    {
        return base.Select_skip_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_skip_navigation_multiple(bool async)
    {
        return base.Select_skip_navigation_multiple(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_skip_navigation_unidirectional(bool async)
    {
        return base.Select_skip_navigation_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_all(bool async)
    {
        return base.Skip_navigation_all(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_all_unidirectional(bool async)
    {
        return base.Skip_navigation_all_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_any_with_predicate(bool async)
    {
        return base.Skip_navigation_any_with_predicate(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_any_with_predicate_unidirectional(bool async)
    {
        return base.Skip_navigation_any_with_predicate_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_cast(bool async)
    {
        return base.Skip_navigation_cast(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_any_without_predicate(bool async)
    {
        return base.Skip_navigation_any_without_predicate(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_contains(bool async)
    {
        return base.Skip_navigation_contains(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_contains_unidirectional(bool async)
    {
        return base.Skip_navigation_contains_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_count_with_predicate(bool async)
    {
        return base.Skip_navigation_count_with_predicate(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_count_with_predicate_unidirectional(bool async)
    {
        return base.Skip_navigation_count_with_predicate_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_count_without_predicate(bool async)
    {
        return base.Skip_navigation_count_without_predicate(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_count_without_predicate_unidirectional(bool async)
    {
        return base.Skip_navigation_count_without_predicate_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_long_count_with_predicate(bool async)
    {
        return base.Skip_navigation_long_count_with_predicate(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_long_count_without_predicate(bool async)
    {
        return base.Skip_navigation_long_count_without_predicate(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_of_type(bool async)
    {
        return base.Skip_navigation_of_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_of_type_unidirectional(bool async)
    {
        return base.Skip_navigation_of_type_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_order_by_first_or_default(bool async)
    {
        return base.Skip_navigation_order_by_first_or_default(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_order_by_last_or_default(bool async)
    {
        return base.Skip_navigation_order_by_last_or_default(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_order_by_reverse_first_or_default(bool async)
    {
        return base.Skip_navigation_order_by_reverse_first_or_default(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_order_by_reverse_first_or_default_unidirectional(bool async)
    {
        return base.Skip_navigation_order_by_reverse_first_or_default_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_order_by_single_or_default(bool async)
    {
        return base.Skip_navigation_order_by_single_or_default(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_select_many_max(bool async)
    {
        return base.Skip_navigation_select_many_max(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_select_many_min(bool async)
    {
        return base.Skip_navigation_select_many_min(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_select_many_sum(bool async)
    {
        return base.Skip_navigation_select_many_sum(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_select_subquery_average(bool async)
    {
        return base.Skip_navigation_select_subquery_average(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_select_subquery_average_unidirectional(bool async)
    {
        return base.Skip_navigation_select_subquery_average_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_select_subquery_max(bool async)
    {
        return base.Skip_navigation_select_subquery_max(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_select_subquery_min(bool async)
    {
        return base.Skip_navigation_select_subquery_min(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Skip_navigation_select_subquery_sum(bool async)
    {
        return base.Skip_navigation_select_subquery_sum(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Throws_when_different_filtered_include(bool async)
    {
        return base.Throws_when_different_filtered_include(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Throws_when_different_filtered_include_unidirectional(bool async)
    {
        return base.Throws_when_different_filtered_include_unidirectional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Throws_when_different_filtered_then_include(bool async)
    {
        return base.Throws_when_different_filtered_then_include(async);
    }
}
