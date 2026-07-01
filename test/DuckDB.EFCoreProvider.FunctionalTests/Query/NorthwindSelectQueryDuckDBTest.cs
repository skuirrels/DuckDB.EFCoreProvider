using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindSelectQueryDuckDBTest : NorthwindSelectQueryRelationalTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindSelectQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Collection_projection_selecting_outer_element_followed_by_take(bool async)
    {
        return base.Collection_projection_selecting_outer_element_followed_by_take(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_after_distinct_not_containing_original_identifier(bool async)
    {
        return base.Correlated_collection_after_distinct_not_containing_original_identifier(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_after_distinct_with_complex_projection_containing_original_identifier(bool async)
    {
        return base.Correlated_collection_after_distinct_with_complex_projection_containing_original_identifier(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_after_distinct_with_complex_projection_not_containing_original_identifier(bool async)
    {
        return base.Correlated_collection_after_distinct_with_complex_projection_not_containing_original_identifier(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_after_groupby_with_complex_projection_containing_original_identifier(bool async)
    {
        return base.Correlated_collection_after_groupby_with_complex_projection_containing_original_identifier(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_after_groupby_with_complex_projection_not_containing_original_identifier(bool async)
    {
        return base.Correlated_collection_after_groupby_with_complex_projection_not_containing_original_identifier(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Explicit_cast_in_arithmetic_operation_is_preserved(bool async)
    {
        return base.Explicit_cast_in_arithmetic_operation_is_preserved(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Member_binding_after_ctor_arguments_fails_with_client_eval(bool async)
    {
        return base.Member_binding_after_ctor_arguments_fails_with_client_eval(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_keyless_entity_FirstOrDefault_without_orderby(bool async)
    {
        return base.Project_keyless_entity_FirstOrDefault_without_orderby(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_single_element_from_collection_with_OrderBy_Take_and_FirstOrDefault_with_parameter(bool async)
    {
        return base.Project_single_element_from_collection_with_OrderBy_Take_and_FirstOrDefault_with_parameter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Projecting_after_navigation_and_distinct(bool async)
    {
        return base.Projecting_after_navigation_and_distinct(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Projection_containing_DateTime_subtraction(bool async)
    {
        return base.Projection_containing_DateTime_subtraction(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Projection_when_arithmetic_mixed_subqueries(bool async)
    {
        return base.Projection_when_arithmetic_mixed_subqueries(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Reverse_in_projection_subquery(bool async)
    {
        return base.Reverse_in_projection_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Reverse_in_projection_subquery_single_result(bool async)
    {
        return base.Reverse_in_projection_subquery_single_result(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Reverse_in_SelectMany_with_Take(bool async)
    {
        return base.Reverse_in_SelectMany_with_Take(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_conditional_with_null_comparison_in_test(bool async)
    {
        return base.Select_conditional_with_null_comparison_in_test(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_nested_collection_deep(bool async)
    {
        return base.Select_nested_collection_deep(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_nested_collection_deep_distinct_no_identifiers(bool async)
    {
        return base.Select_nested_collection_deep_distinct_no_identifiers(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_correlated_with_outer_1(bool async)
    {
        return base.SelectMany_correlated_with_outer_1(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_correlated_with_outer_2(bool async)
    {
        return base.SelectMany_correlated_with_outer_2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_correlated_with_outer_3(bool async)
    {
        return base.SelectMany_correlated_with_outer_3(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_correlated_with_outer_4(bool async)
    {
        return base.SelectMany_correlated_with_outer_4(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_correlated_with_outer_5(bool async)
    {
        return base.SelectMany_correlated_with_outer_5(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_correlated_with_outer_6(bool async)
    {
        return base.SelectMany_correlated_with_outer_6(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_correlated_with_outer_7(bool async)
    {
        return base.SelectMany_correlated_with_outer_7(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_whose_selector_references_outer_source(bool async)
    {
        return base.SelectMany_whose_selector_references_outer_source(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_with_collection_being_correlated_subquery_which_references_inner_and_outer_entity(bool async)
    {
        return base.SelectMany_with_collection_being_correlated_subquery_which_references_inner_and_outer_entity(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_with_collection_being_correlated_subquery_which_references_non_mapped_properties_from_inner_and_outer_entity(bool async)
    {
        return base.SelectMany_with_collection_being_correlated_subquery_which_references_non_mapped_properties_from_inner_and_outer_entity(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Set_operation_in_pending_collection(bool async)
    {
        return base.Set_operation_in_pending_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Take_on_correlated_collection_in_first(bool async)
    {
        return base.Take_on_correlated_collection_in_first(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Take_on_top_level_and_on_collection_projection_with_outer_apply(bool async)
    {
        return base.Take_on_top_level_and_on_collection_projection_with_outer_apply(async);
    }
}
