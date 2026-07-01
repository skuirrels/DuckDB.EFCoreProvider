using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.BulkUpdates;

public class NonSharedModelBulkUpdatesDuckDBTest : NonSharedModelBulkUpdatesRelationalTestBase
{
    public NonSharedModelBulkUpdatesDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_aggregate_root_when_table_sharing_with_non_owned_throws(bool async)
    {
        return base.Delete_aggregate_root_when_table_sharing_with_non_owned_throws(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_main_table_in_entity_with_entity_splitting(bool async)
    {
        return base.Update_main_table_in_entity_with_entity_splitting(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_non_main_table_in_entity_with_entity_splitting(bool async)
    {
        return base.Update_non_main_table_in_entity_with_entity_splitting(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_aggregate_root_when_table_sharing_with_owned(bool async)
    {
        return base.Delete_aggregate_root_when_table_sharing_with_owned(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_predicate_based_on_optional_navigation(bool async)
    {
        return base.Delete_predicate_based_on_optional_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Replace_ColumnExpression_in_column_setter(bool async)
    {
        return base.Replace_ColumnExpression_in_column_setter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_non_owned_property_on_entity_with_owned(bool async)
    {
        return base.Update_non_owned_property_on_entity_with_owned(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_non_owned_property_on_entity_with_owned2(bool async)
    {
        return base.Update_non_owned_property_on_entity_with_owned2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_non_owned_property_on_entity_with_owned_in_join(bool async)
    {
        return base.Update_non_owned_property_on_entity_with_owned_in_join(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_owned_and_non_owned_properties_with_table_sharing(bool async)
    {
        return base.Update_owned_and_non_owned_properties_with_table_sharing(async);
    }

    protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
}
