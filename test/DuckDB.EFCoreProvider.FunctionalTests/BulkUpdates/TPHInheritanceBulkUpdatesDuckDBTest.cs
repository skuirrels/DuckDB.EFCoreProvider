using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.BulkUpdates;

public class TPHInheritanceBulkUpdatesDuckDBTest : TPHInheritanceBulkUpdatesTestBase<TPHInheritanceBulkUpdatesDuckDBFixture>
{
    public TPHInheritanceBulkUpdatesDuckDBTest(TPHInheritanceBulkUpdatesDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_where_keyless_entity_mapped_to_sql_query(bool async)
    {
        return base.Delete_where_keyless_entity_mapped_to_sql_query(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_where_keyless_entity_mapped_to_sql_query(bool async)
    {
        return base.Update_where_keyless_entity_mapped_to_sql_query(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_GroupBy_Where_Select_First_2(bool async)
    {
        return base.Delete_GroupBy_Where_Select_First_2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_GroupBy_Where_Select_First_3(bool async)
    {
        return base.Delete_GroupBy_Where_Select_First_3(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_where_hierarchy(bool async)
    {
        return base.Delete_where_hierarchy(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_where_hierarchy_derived(bool async)
    {
        return base.Delete_where_hierarchy_derived(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_where_hierarchy_subquery(bool async)
    {
        return base.Delete_where_hierarchy_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_where_using_hierarchy(bool async)
    {
        return base.Delete_where_using_hierarchy(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_where_using_hierarchy_derived(bool async)
    {
        return base.Delete_where_using_hierarchy_derived(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_base_and_derived_types(bool async)
    {
        return base.Update_base_and_derived_types(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_base_property_on_derived_type(bool async)
    {
        return base.Update_base_property_on_derived_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_base_type(bool async)
    {
        return base.Update_base_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_base_type_with_OfType(bool async)
    {
        return base.Update_base_type_with_OfType(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_derived_property_on_derived_type(bool async)
    {
        return base.Update_derived_property_on_derived_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_where_using_hierarchy(bool async)
    {
        return base.Update_where_using_hierarchy(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_where_using_hierarchy_derived(bool async)
    {
        return base.Update_where_using_hierarchy_derived(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_where_hierarchy_subquery(bool async)
    {
        return base.Update_where_hierarchy_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_with_interface_in_EF_Property_in_property_expression(bool async)
    {
        return base.Update_with_interface_in_EF_Property_in_property_expression(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_with_interface_in_property_expression(bool async)
    {
        return base.Update_with_interface_in_property_expression(async);
    }
}
