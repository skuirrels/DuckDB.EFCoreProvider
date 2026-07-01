using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class OwnedEntityQueryDuckDBTest : OwnedEntityQueryRelationalTestBase
{
    public OwnedEntityQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Join_selects_with_duplicating_aliases_and_owned_expansion_uniquifies_correctly(bool async)
    {
        return base.Join_selects_with_duplicating_aliases_and_owned_expansion_uniquifies_correctly(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Multiple_owned_reference_mapped_to_own_table_containing_owned_collection_in_split_query(bool async)
    {
        return base.Multiple_owned_reference_mapped_to_own_table_containing_owned_collection_in_split_query(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Owned_entity_with_all_null_properties_entity_equality_when_not_containing_another_owned_entity(bool async)
    {
        return base.Owned_entity_with_all_null_properties_entity_equality_when_not_containing_another_owned_entity(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Owned_entity_with_all_null_properties_in_compared_to_non_null_in_conditional_projection(bool async)
    {
        return base.Owned_entity_with_all_null_properties_in_compared_to_non_null_in_conditional_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Owned_entity_with_all_null_properties_in_compared_to_null_in_conditional_projection(bool async)
    {
        return base.Owned_entity_with_all_null_properties_in_compared_to_null_in_conditional_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Owned_entity_with_all_null_properties_materializes_when_not_containing_another_owned_entity(bool async)
    {
        return base.Owned_entity_with_all_null_properties_materializes_when_not_containing_another_owned_entity(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Owned_entity_with_all_null_properties_property_access_when_not_containing_another_owned_entity(bool async)
    {
        return base.Owned_entity_with_all_null_properties_property_access_when_not_containing_another_owned_entity(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Owned_reference_mapped_to_different_table_nested_updated_correctly_after_subquery_pushdown(bool async)
    {
        return base.Owned_reference_mapped_to_different_table_nested_updated_correctly_after_subquery_pushdown(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Owned_reference_mapped_to_different_table_updated_correctly_after_subquery_pushdown(bool async)
    {
        return base.Owned_reference_mapped_to_different_table_updated_correctly_after_subquery_pushdown(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Accessing_scalar_property_in_derived_type_projection_does_not_load_owned_navigations()
    {
        return base.Accessing_scalar_property_in_derived_type_projection_does_not_load_owned_navigations();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_auto_include_navigation_from_model()
    {
        return base.Can_auto_include_navigation_from_model();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Correlated_subquery_with_owned_navigation_being_compared_to_null_works()
    {
        return base.Correlated_subquery_with_owned_navigation_being_compared_to_null_works();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_for_entity_with_owned_type_works()
    {
        return base.Include_collection_for_entity_with_owned_type_works();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Multilevel_owned_entities_determine_correct_nullability()
    {
        return base.Multilevel_owned_entities_determine_correct_nullability();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_owned_required_dependents_are_materialized()
    {
        return base.Nested_owned_required_dependents_are_materialized();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Owned_entity_multiple_level_in_aggregate()
    {
        return base.Owned_entity_multiple_level_in_aggregate();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Projecting_correlated_collection_property_for_owned_entity(bool async)
    {
        return base.Projecting_correlated_collection_property_for_owned_entity(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Projecting_owned_collection_and_aggregate(bool async)
    {
        return base.Projecting_owned_collection_and_aggregate(async);
    }

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;
}
