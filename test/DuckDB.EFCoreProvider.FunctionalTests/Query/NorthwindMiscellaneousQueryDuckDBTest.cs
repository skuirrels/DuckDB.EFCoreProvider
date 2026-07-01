using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindMiscellaneousQueryDuckDBTest: NorthwindMiscellaneousQueryRelationalTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindMiscellaneousQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Client_code_unknown_method(bool async)
    {
        return base.Client_code_unknown_method(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Client_code_using_instance_in_anonymous_type(bool async)
    {
        return base.Client_code_using_instance_in_anonymous_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Client_code_using_instance_in_static_method(bool async)
    {
        return base.Client_code_using_instance_in_static_method(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Client_code_using_instance_method_throws(bool async)
    {
        return base.Client_code_using_instance_method_throws(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Collection_navigation_equal_to_null_for_subquery_using_ElementAtOrDefault_parameter(bool async)
    {
        return base.Collection_navigation_equal_to_null_for_subquery_using_ElementAtOrDefault_parameter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Complex_nested_query_doesnt_try_binding_to_grandparent_when_parent_returns_complex_result(bool async)
    {
        return base.Complex_nested_query_doesnt_try_binding_to_grandparent_when_parent_returns_complex_result(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_with_DateTime_Date(bool async)
    {
        return base.Contains_with_DateTime_Date(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_with_distinct_without_default_identifiers_projecting_columns(bool async)
    {
        return base.Correlated_collection_with_distinct_without_default_identifiers_projecting_columns(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_collection_with_distinct_without_default_identifiers_projecting_columns_with_navigation(bool async)
    {
        return base.Correlated_collection_with_distinct_without_default_identifiers_projecting_columns_with_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task DefaultIfEmpty_in_subquery_nested_filter_order_comparison(bool async)
    {
        return base.DefaultIfEmpty_in_subquery_nested_filter_order_comparison(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Entity_equality_orderby_subquery(bool async)
    {
        return base.Entity_equality_orderby_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Entity_equality_through_subquery_composite_key(bool async)
    {
        return base.Entity_equality_through_subquery_composite_key(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Max_on_empty_sequence_throws(bool async)
    {
        return base.Max_on_empty_sequence_throws(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Projection_skip_collection_projection(bool async)
    {
        return base.Projection_skip_collection_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Projection_skip_take_collection_projection(bool async)
    {
        return base.Projection_skip_take_collection_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_expression_with_to_string_and_contains(bool async)
    {
        return base.Query_expression_with_to_string_and_contains(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_correlated_subquery_ordered(bool async)
    {
        return base.Select_correlated_subquery_ordered(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_DTO_constructor_distinct_with_collection_projection_translated_to_server_with_binding_after_client_eval(bool async)
    {
        return base.Select_DTO_constructor_distinct_with_collection_projection_translated_to_server_with_binding_after_client_eval(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_subquery_recursive_trivial(bool async)
    {
        return base.Select_subquery_recursive_trivial(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_correlated_subquery_hard(bool async)
    {
        return base.SelectMany_correlated_subquery_hard(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_correlated_with_Select_value_type_and_DefaultIfEmpty_in_selector(bool async)
    {
        return base.SelectMany_correlated_with_Select_value_type_and_DefaultIfEmpty_in_selector(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ToListAsync_with_canceled_token()
    {
        return base.ToListAsync_with_canceled_token();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Where_nanosecond_and_microsecond_component(bool async)
    {
        return base.Where_nanosecond_and_microsecond_component(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Select_expression_date_add_milliseconds_large_number_divided(bool async)
    {
        await base.Select_expression_date_add_milliseconds_large_number_divided(async);
    }
}
