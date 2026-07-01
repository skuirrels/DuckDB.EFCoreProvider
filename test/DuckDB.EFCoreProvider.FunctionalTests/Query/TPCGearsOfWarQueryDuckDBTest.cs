using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class TPCGearsOfWarQueryDuckDBTest : TPCGearsOfWarQueryRelationalTestBase<TPCGearsOfWarQueryDuckDBFixture>
{
    public TPCGearsOfWarQueryDuckDBTest(TPCGearsOfWarQueryDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_after_distinct_3_levels(bool async)
    {
        return base.Correlated_collection_after_distinct_3_levels(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_via_SelectMany_with_Distinct_missing_indentifying_columns_in_projection(bool async)
    {
        return base.Correlated_collection_via_SelectMany_with_Distinct_missing_indentifying_columns_in_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_with_distinct_not_projecting_identifier_column(bool async)
    {
        return base.Correlated_collection_with_distinct_not_projecting_identifier_column(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_with_distinct_projecting_identifier_column(bool async)
    {
        return base.Correlated_collection_with_distinct_projecting_identifier_column(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_with_groupby_not_projecting_identifier_column_but_only_grouping_key_in_final_projection(bool async)
    {
        return base.Correlated_collection_with_groupby_not_projecting_identifier_column_but_only_grouping_key_in_final_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection(bool async)
    {
        return base.Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection_multiple_grouping_keys(bool async)
    {
        return base.Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection_multiple_grouping_keys(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_with_groupby_with_complex_grouping_key_not_projecting_identifier_column_with_group_aggregate_in_final_projection(bool async)
    {
        return base.Correlated_collection_with_groupby_with_complex_grouping_key_not_projecting_identifier_column_with_group_aggregate_in_final_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_with_inner_collection_references_element_two_levels_up(bool async)
    {
        return base.Correlated_collection_with_inner_collection_references_element_two_levels_up(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collections_inner_subquery_predicate_references_outer_qsre(bool async)
    {
        return base.Correlated_collections_inner_subquery_predicate_references_outer_qsre(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collections_inner_subquery_selector_references_outer_qsre(bool async)
    {
        return base.Correlated_collections_inner_subquery_selector_references_outer_qsre(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collections_nested_inner_subquery_references_outer_qsre_one_level_up(bool async)
    {
        return base.Correlated_collections_nested_inner_subquery_references_outer_qsre_one_level_up(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collections_nested_inner_subquery_references_outer_qsre_two_levels_up(bool async)
    {
        return base.Correlated_collections_nested_inner_subquery_references_outer_qsre_two_levels_up(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collections_with_Distinct(bool async)
    {
        return base.Correlated_collections_with_Distinct(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collections_with_funky_orderby_complex_scenario2(bool async)
    {
        return base.Correlated_collections_with_funky_orderby_complex_scenario2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task DateTimeOffset_Contains_Less_than_Greater_than(bool async)
    {
        return base.DateTimeOffset_Contains_Less_than_Greater_than(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task DateTimeOffset_Date_returns_datetime(bool async)
    {
        return base.DateTimeOffset_Date_returns_datetime(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task DateTimeOffsetNow_minus_timespan(bool async)
    {
        return base.DateTimeOffsetNow_minus_timespan(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Double_order_by_on_nullable_bool_coming_from_optional_navigation(bool async)
    {
        return base.Double_order_by_on_nullable_bool_coming_from_optional_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FirstOrDefault_on_empty_collection_of_DateTime_in_subquery(bool async)
    {
        return base.FirstOrDefault_on_empty_collection_of_DateTime_in_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Group_by_on_StartsWith_with_null_parameter_as_argument(bool async)
    {
        return base.Group_by_on_StartsWith_with_null_parameter_as_argument(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Groupby_anonymous_type_with_navigations_followed_up_by_anonymous_projection_and_orderby(bool async)
    {
        return base.Groupby_anonymous_type_with_navigations_followed_up_by_anonymous_projection_and_orderby(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_with_complex_order_by(bool async)
    {
        return base.Include_with_complex_order_by(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_with_nested_navigation_in_order_by(bool async)
    {
        return base.Include_with_nested_navigation_in_order_by(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Non_string_concat_uses_appropriate_type_mapping(bool async)
    {
        return base.Non_string_concat_uses_appropriate_type_mapping(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Order_by_entity_qsre(bool async)
    {
        return base.Order_by_entity_qsre(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Order_by_entity_qsre_composite_key(bool async)
    {
        return base.Order_by_entity_qsre_composite_key(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task OrderBy_bool_coming_from_optional_navigation(bool async)
    {
        return base.OrderBy_bool_coming_from_optional_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Outer_parameter_in_group_join_with_DefaultIfEmpty(bool async)
    {
        return base.Outer_parameter_in_group_join_with_DefaultIfEmpty(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Outer_parameter_in_join_key(bool async)
    {
        return base.Outer_parameter_in_join_key(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Outer_parameter_in_join_key_inner_and_outer(bool async)
    {
        return base.Outer_parameter_in_join_key_inner_and_outer(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Projecting_property_converted_to_nullable_into_member_access(bool async)
    {
        return base.Projecting_property_converted_to_nullable_into_member_access(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_null_propagation_negative3(bool async)
    {
        return base.Select_null_propagation_negative3(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_null_propagation_negative4(bool async)
    {
        return base.Select_null_propagation_negative4(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_null_propagation_negative5(bool async)
    {
        return base.Select_null_propagation_negative5(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_StartsWith_with_null_parameter_as_argument(bool async)
    {
        return base.Select_StartsWith_with_null_parameter_as_argument(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_predicate_after_navigation_with_non_equality_comparison_DefaultIfEmpty_converted_to_left_join(bool async)
    {
        return base.SelectMany_predicate_after_navigation_with_non_equality_comparison_DefaultIfEmpty_converted_to_left_join(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_predicate_with_non_equality_comparison_with_Take_doesnt_convert_to_join(bool async)
    {
        return base.SelectMany_predicate_with_non_equality_comparison_with_Take_doesnt_convert_to_join(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion(bool async)
    {
        return base.Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion_negated(bool async)
    {
        return base.Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion_negated(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion(bool async)
    {
        return base.Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion_negated(bool async)
    {
        return base.Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion_negated(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Where_equals_method_on_nullable_with_object_overload(bool async)
    {
        return base.Where_equals_method_on_nullable_with_object_overload(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Where_subquery_with_ElementAt_using_column_as_index(bool async)
    {
        return base.Where_subquery_with_ElementAt_using_column_as_index(async);
    }
}

