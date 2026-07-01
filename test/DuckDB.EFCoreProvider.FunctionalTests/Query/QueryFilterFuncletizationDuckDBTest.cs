using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class QueryFilterFuncletizationDuckDBTest : QueryFilterFuncletizationTestBase<QueryFilterFuncletizationDuckDBTest.QueryFilterFuncletizationDuckDBFixture>
{
    public QueryFilterFuncletizationDuckDBTest(QueryFilterFuncletizationDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void DbContext_complex_expression_is_parameterized()
    {
        base.DbContext_complex_expression_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void DbContext_field_is_parameterized()
    {
        base.DbContext_field_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void DbContext_list_is_parameterized()
    {
        base.DbContext_list_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void DbContext_method_call_chain_is_parameterized()
    {
        base.DbContext_method_call_chain_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void DbContext_method_call_is_parameterized()
    {
        base.DbContext_method_call_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void DbContext_property_based_filter_does_not_short_circuit()
    {
        base.DbContext_property_based_filter_does_not_short_circuit();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void DbContext_property_chain_is_parameterized()
    {
        base.DbContext_property_chain_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void DbContext_property_is_parameterized()
    {
        base.DbContext_property_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void DbContext_property_method_call_is_parameterized()
    {
        base.DbContext_property_method_call_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void DbContext_property_parameter_does_not_clash_with_closure_parameter_name()
    {
        base.DbContext_property_parameter_does_not_clash_with_closure_parameter_name();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void EntityTypeConfiguration_DbContext_field_is_parameterized()
    {
        base.EntityTypeConfiguration_DbContext_field_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void EntityTypeConfiguration_DbContext_method_call_is_parameterized()
    {
        base.EntityTypeConfiguration_DbContext_method_call_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void EntityTypeConfiguration_DbContext_property_chain_is_parameterized()
    {
        base.EntityTypeConfiguration_DbContext_property_chain_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void EntityTypeConfiguration_DbContext_property_is_parameterized()
    {
        base.EntityTypeConfiguration_DbContext_property_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Extension_method_DbContext_field_is_parameterized()
    {
        base.Extension_method_DbContext_field_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Extension_method_DbContext_property_chain_is_parameterized()
    {
        base.Extension_method_DbContext_property_chain_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Local_method_DbContext_field_is_parameterized()
    {
        base.Local_method_DbContext_field_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Local_static_method_DbContext_property_is_parameterized()
    {
        base.Local_static_method_DbContext_property_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Local_variable_from_OnModelCreating_can_throw_exception()
    {
        base.Local_variable_from_OnModelCreating_can_throw_exception();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Local_variable_from_OnModelCreating_is_inlined()
    {
        base.Local_variable_from_OnModelCreating_is_inlined();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Method_parameter_is_inlined()
    {
        base.Method_parameter_is_inlined();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Remote_method_DbContext_property_method_call_is_parameterized()
    {
        base.Remote_method_DbContext_property_method_call_is_parameterized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Static_member_from_dbContext_is_inlined()
    {
        base.Static_member_from_dbContext_is_inlined();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Static_member_from_non_dbContext_is_inlined()
    {
        base.Static_member_from_non_dbContext_is_inlined();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Using_Context_set_method_in_filter_works()
    {
        base.Using_Context_set_method_in_filter_works();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Using_DbSet_in_filter_works()
    {
        base.Using_DbSet_in_filter_works();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Using_multiple_context_in_filter_parametrize_only_current_context()
    {
        base.Using_multiple_context_in_filter_parametrize_only_current_context();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Using_multiple_entities_with_filters_reuses_parameters()
    {
        base.Using_multiple_entities_with_filters_reuses_parameters();
    }

    public class QueryFilterFuncletizationDuckDBFixture : QueryFilterFuncletizationRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}