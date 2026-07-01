using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore;

public class TableSplittingDuckDBTest : TableSplittingTestBase
{
    public TableSplittingDuckDBTest(NonSharedFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_change_dependent_instance_non_derived()
    {
        await base.Can_change_dependent_instance_non_derived();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_change_principal_instance_non_derived()
    {
        await base.Can_change_principal_instance_non_derived();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_change_principal_and_dependent_instance_non_derived()
    {
        await base.Can_change_principal_and_dependent_instance_non_derived();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_dependent_with_just_one_parent()
    {
        await base.Can_insert_dependent_with_just_one_parent();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_manipulate_entities_sharing_row_independently()
    {
        await base.Can_manipulate_entities_sharing_row_independently();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_shared()
    {
        await base.Can_query_shared();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_shared_derived_hierarchy()
    {
        await base.Can_query_shared_derived_hierarchy();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_shared_derived_nonhierarchy()
    {
        await base.Can_query_shared_derived_nonhierarchy();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_shared_derived_nonhierarchy_all_required()
    {
        await base.Can_query_shared_derived_nonhierarchy_all_required();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_shared_nonhierarchy()
    {
        await base.Can_query_shared_nonhierarchy();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_shared_nonhierarchy_with_nonshared_dependent()
    {
        await base.Can_query_shared_nonhierarchy_with_nonshared_dependent();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_share_required_columns()
    {
        await base.Can_share_required_columns();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_update_just_dependents()
    {
        await base.Can_update_just_dependents();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_use_optional_dependents_with_shared_concurrency_tokens()
    {
        await base.Can_use_optional_dependents_with_shared_concurrency_tokens();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_use_optional_dependents_with_shared_concurrency_tokens_with_complex_types()
    {
        await base.Can_use_optional_dependents_with_shared_concurrency_tokens_with_complex_types();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_use_with_chained_relationships()
    {
        await base.Can_use_with_chained_relationships();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_use_with_fanned_relationships()
    {
        await base.Can_use_with_fanned_relationships();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_use_with_redundant_relationships()
    {
        await base.Can_use_with_redundant_relationships();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task ExecuteDelete_throws_for_table_sharing(bool async)
    {
        await base.ExecuteDelete_throws_for_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task ExecuteUpdate_works_for_table_sharing(bool async)
    {
        await base.ExecuteUpdate_works_for_table_sharing(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Optional_dependent_materialized_when_no_properties()
    {
        await base.Optional_dependent_materialized_when_no_properties();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task No_warn_when_save_optional_dependent_at_least_one_none_null()
    {
        await base.No_warn_when_save_optional_dependent_at_least_one_none_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Warn_when_save_optional_dependent_with_null_values()
    {
        await base.Warn_when_save_optional_dependent_with_null_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Warn_when_save_optional_dependent_with_null_values_sensitive()
    {
        await base.Warn_when_save_optional_dependent_with_null_values_sensitive();
    }

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;
}