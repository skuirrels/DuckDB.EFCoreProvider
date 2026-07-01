using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class ComplexNavigationsSharedTypeQueryDuckDBTest : ComplexNavigationsSharedTypeQueryRelationalTestBase<ComplexNavigationsSharedTypeQueryDuckDBFixture>
{
    public ComplexNavigationsSharedTypeQueryDuckDBTest(ComplexNavigationsSharedTypeQueryDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task OrderBy_nav_prop_reference_optional_via_DefaultIfEmpty(bool async)
    {
        return base.OrderBy_nav_prop_reference_optional_via_DefaultIfEmpty(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GroupJoin_on_left_side_being_a_subquery(bool async)
    {
        return base.GroupJoin_on_left_side_being_a_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GroupJoin_client_method_in_OrderBy(bool async)
    {
        return base.GroupJoin_client_method_in_OrderBy(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Let_let_contains_from_outer_let(bool async)
    {
        return base.Let_let_contains_from_outer_let(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Optional_navigation_take_optional_navigation(bool async)
    {
        return base.Optional_navigation_take_optional_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_SelectMany_correlated_with_join_table_correctly_translated_to_apply(bool async)
    {
        return base.Nested_SelectMany_correlated_with_join_table_correctly_translated_to_apply(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Prune_does_not_throw_null_ref(bool async)
    {
        return base.Prune_does_not_throw_null_ref(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Optional_navigation_inside_method_call_translated_to_join_keeps_original_nullability(bool async)
    {
        return base.Optional_navigation_inside_method_call_translated_to_join_keeps_original_nullability(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Optional_navigation_inside_nested_method_call_translated_to_join_keeps_original_nullability_also_for_arguments(bool async)
    {
        return base.Optional_navigation_inside_nested_method_call_translated_to_join_keeps_original_nullability_also_for_arguments(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Member_over_null_check_ternary_and_nested_dto_type(bool async)
    {
        return base.Member_over_null_check_ternary_and_nested_dto_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Join_with_result_selector_returning_queryable_throws_validation_error(bool async)
    {
        return base.Join_with_result_selector_returning_queryable_throws_validation_error(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Multiple_select_many_in_projection(bool async)
    {
        return base.Multiple_select_many_in_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_projection_with_first(bool async)
    {
        return base.Correlated_projection_with_first(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_subquery_with_custom_projection(bool async)
    {
        return base.SelectMany_subquery_with_custom_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GroupJoin_on_right_side_being_a_subquery(bool async)
    {
        return base.GroupJoin_on_right_side_being_a_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task OrderBy_nav_prop_reference_optional(bool async)
    {
        return base.OrderBy_nav_prop_reference_optional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Optional_navigation_inside_nested_method_call_translated_to_join_keeps_original_nullability(bool async)
    {
        return base.Optional_navigation_inside_nested_method_call_translated_to_join_keeps_original_nullability(async);
    }
}
