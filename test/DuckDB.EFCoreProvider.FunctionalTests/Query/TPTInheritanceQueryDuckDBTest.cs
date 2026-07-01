using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class TPTInheritanceQueryDuckDBTest : TPTInheritanceQueryTestBase<TPTInheritanceQueryDuckDBFixture>
{
    public TPTInheritanceQueryDuckDBTest(TPTInheritanceQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Byte_enum_value_constant_used_in_projection(bool async)
    {
        return base.Byte_enum_value_constant_used_in_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_filter_all_animals(bool async)
    {
        return base.Can_filter_all_animals(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_include_animals(bool async)
    {
        return base.Can_include_animals(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_include_prey(bool async)
    {
        return base.Can_include_prey(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_insert_update_delete()
    {
        return base.Can_insert_update_delete();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_query_all_animals(bool async)
    {
        return base.Can_query_all_animals(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_query_all_birds(bool async)
    {
        return base.Can_query_all_birds(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_query_all_plants(bool async)
    {
        return base.Can_query_all_plants(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_query_all_types_when_shared_column(bool async)
    {
        return base.Can_query_all_types_when_shared_column(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_query_just_kiwis(bool async)
    {
        return base.Can_query_just_kiwis(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_query_just_roses(bool async)
    {
        return base.Can_query_just_roses(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_query_when_shared_column(bool async)
    {
        return base.Can_query_when_shared_column(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_backwards_is_animal(bool async)
    {
        return base.Can_use_backwards_is_animal(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_backwards_of_type_animal(bool async)
    {
        return base.Can_use_backwards_of_type_animal(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_is_kiwi(bool async)
    {
        return base.Can_use_is_kiwi(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_is_kiwi_in_projection(bool async)
    {
        return base.Can_use_is_kiwi_in_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_is_kiwi_with_cast(bool async)
    {
        return base.Can_use_is_kiwi_with_cast(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_is_kiwi_with_other_predicate(bool async)
    {
        return base.Can_use_is_kiwi_with_other_predicate(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_of_type_animal(bool async)
    {
        return base.Can_use_of_type_animal(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_of_type_bird(bool async)
    {
        return base.Can_use_of_type_bird(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_of_type_bird_first(bool async)
    {
        return base.Can_use_of_type_bird_first(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_of_type_bird_with_projection(bool async)
    {
        return base.Can_use_of_type_bird_with_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_of_type_bird_predicate(bool async)
    {
        return base.Can_use_of_type_bird_predicate(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_of_type_kiwi(bool async)
    {
        return base.Can_use_of_type_kiwi(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_of_type_kiwi_where_north_on_derived_property(bool async)
    {
        return base.Can_use_of_type_kiwi_where_north_on_derived_property(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_of_type_kiwi_where_south_on_derived_property(bool async)
    {
        return base.Can_use_of_type_kiwi_where_south_on_derived_property(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_of_type_rose(bool async)
    {
        return base.Can_use_of_type_rose(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_on_property_inside_complex_type_on_derived_type(bool async)
    {
        return base.Filter_on_property_inside_complex_type_on_derived_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_abstract_base_type(bool async)
    {
        return base.GetType_in_hierarchy_in_abstract_base_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_intermediate_type(bool async)
    {
        return base.GetType_in_hierarchy_in_intermediate_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_leaf_type_with_sibling(bool async)
    {
        return base.GetType_in_hierarchy_in_leaf_type_with_sibling(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_leaf_type_with_sibling2(bool async)
    {
        return base.GetType_in_hierarchy_in_leaf_type_with_sibling2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_leaf_type_with_sibling2_not_equal(bool async)
    {
        return base.GetType_in_hierarchy_in_leaf_type_with_sibling2_not_equal(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_leaf_type_with_sibling2_reverse(bool async)
    {
        return base.GetType_in_hierarchy_in_leaf_type_with_sibling2_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Is_operator_on_result_of_FirstOrDefault(bool async)
    {
        return base.Is_operator_on_result_of_FirstOrDefault(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Member_access_on_intermediate_type_works()
    {
        return base.Member_access_on_intermediate_type_works();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Selecting_only_base_properties_on_base_type(bool async)
    {
        return base.Selecting_only_base_properties_on_base_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Selecting_only_base_properties_on_derived_type(bool async)
    {
        return base.Selecting_only_base_properties_on_derived_type(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Setting_foreign_key_to_a_different_type_throws()
    {
        return base.Setting_foreign_key_to_a_different_type_throws();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Subquery_OfType(bool async)
    {
        return base.Subquery_OfType(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Using_is_operator_on_multiple_type_with_no_result(bool async)
    {
        return base.Using_is_operator_on_multiple_type_with_no_result(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Using_is_operator_with_of_type_on_multiple_type_with_no_result(bool async)
    {
        return base.Using_is_operator_with_of_type_on_multiple_type_with_no_result(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Using_OfType_on_multiple_type_with_no_result(bool async)
    {
        return base.Using_OfType_on_multiple_type_with_no_result(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_query_all_animal_views(bool async)
    {
        return base.Can_query_all_animal_views(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Discriminator_used_when_projection_over_derived_type(bool async)
    {
        return base.Discriminator_used_when_projection_over_derived_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Discriminator_used_when_projection_over_derived_type2(bool async)
    {
        return base.Discriminator_used_when_projection_over_derived_type2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Discriminator_used_when_projection_over_of_type(bool async)
    {
        return base.Discriminator_used_when_projection_over_of_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Discriminator_with_cast_in_shadow_property(bool async)
    {
        return base.Discriminator_with_cast_in_shadow_property(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Using_from_sql_throws()
    {
        base.Using_from_sql_throws();
    }

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());
}
