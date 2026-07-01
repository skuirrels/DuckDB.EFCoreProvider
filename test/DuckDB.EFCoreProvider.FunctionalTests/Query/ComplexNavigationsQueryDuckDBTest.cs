using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class ComplexNavigationsQueryDuckDBTest : ComplexNavigationsQueryRelationalTestBase<ComplexNavigationsQueryDuckDBFixture>
{
    public ComplexNavigationsQueryDuckDBTest(ComplexNavigationsQueryDuckDBFixture fixture) : base(fixture)
    {
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
    public override Task Optional_navigation_inside_nested_method_call_translated_to_join_keeps_original_nullability_also_for_arguments(bool async)
    {
        return base.Optional_navigation_inside_nested_method_call_translated_to_join_keeps_original_nullability_also_for_arguments(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Optional_navigation_inside_method_call_translated_to_join_keeps_original_nullability(bool async)
    {
        return base.Optional_navigation_inside_method_call_translated_to_join_keeps_original_nullability(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Join_with_result_selector_returning_queryable_throws_validation_error(bool async)
    {
        return base.Join_with_result_selector_returning_queryable_throws_validation_error(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Optional_navigation_inside_nested_method_call_translated_to_join_keeps_original_nullability(bool async)
    {
        return base.Optional_navigation_inside_nested_method_call_translated_to_join_keeps_original_nullability(async);
    }
}
