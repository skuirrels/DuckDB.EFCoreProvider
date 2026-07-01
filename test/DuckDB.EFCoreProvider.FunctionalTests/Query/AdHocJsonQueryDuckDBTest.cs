using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class AdHocJsonQueryDuckDBTest : AdHocJsonQueryRelationalTestBase
{
    public AdHocJsonQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }
    
    protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;

    protected override Task Seed30028(DbContext ctx)
    {
        throw new NotImplementedException();
    }

    protected override Task Seed33046(DbContext ctx)
    {
        throw new NotImplementedException();
    }

    protected override Task SeedJunkInJson(DbContext ctx)
    {
        throw new NotImplementedException();
    }

    protected override Task SeedTrickyBuffering(DbContext ctx)
    {
        throw new NotImplementedException();
    }

    protected override Task SeedShadowProperties(DbContext ctx)
    {
        throw new NotImplementedException();
    }

    protected override Task SeedNotICollection(DbContext ctx)
    {
        throw new NotImplementedException();
    }

    protected override Task SeedBadJsonProperties(ContextBadJsonProperties ctx)
    {
        throw new NotImplementedException();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Bad_json_properties_duplicated_navigations(bool noTracking)
    {
        return base.Bad_json_properties_duplicated_navigations(noTracking);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Bad_json_properties_null_scalars(bool noTracking)
    {
        return base.Bad_json_properties_null_scalars(noTracking);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Bad_json_properties_null_navigations(bool noTracking)
    {
        return base.Bad_json_properties_null_navigations(noTracking);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Entity_splitting_with_owned_json()
    {
        return base.Entity_splitting_with_owned_json();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_missing_required_navigation(bool async)
    {
        return base.Project_missing_required_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_null_required_navigation(bool async)
    {
        return base.Project_null_required_navigation(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_optional_json_entity_owned_by_required_json_entity()
    {
        return base.Project_optional_json_entity_owned_by_required_json_entity();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_required_json_entity()
    {
        return base.Project_required_json_entity();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_top_level_entity_with_null_value_required_scalars(bool async)
    {
        return base.Project_top_level_entity_with_null_value_required_scalars(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Try_project_collection_but_JSON_is_entity()
    {
        return base.Try_project_collection_but_JSON_is_entity();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Try_project_reference_but_JSON_is_collection()
    {
        return base.Try_project_reference_but_JSON_is_collection();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Accessing_missing_navigation_works()
    {
        return base.Accessing_missing_navigation_works();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Bad_json_properties_duplicated_scalars(bool noTracking)
    {
        return base.Bad_json_properties_duplicated_scalars(noTracking);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Bad_json_properties_empty_navigations(bool noTracking)
    {
        return base.Bad_json_properties_empty_navigations(noTracking);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Bad_json_properties_empty_scalars(bool noTracking)
    {
        return base.Bad_json_properties_empty_scalars(noTracking);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_project_nullable_json_property_when_the_element_in_json_is_not_present()
    {
        return base.Can_project_nullable_json_property_when_the_element_in_json_is_not_present();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_on_nested_collection_with_init_only_navigation()
    {
        return base.Contains_on_nested_collection_with_init_only_navigation();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Junk_in_json_basic_no_tracking()
    {
        return base.Junk_in_json_basic_no_tracking();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Junk_in_json_basic_tracking()
    {
        return base.Junk_in_json_basic_tracking();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Missing_navigation_works_with_deduplication(bool async)
    {
        return base.Missing_navigation_works_with_deduplication(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Not_ICollection_basic_projection()
    {
        return base.Not_ICollection_basic_projection();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Optional_json_properties_materialized_as_null_when_the_element_in_json_is_not_present()
    {
        return base.Optional_json_properties_materialized_as_null_when_the_element_in_json_is_not_present();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Predicate_based_on_element_of_json_array_of_primitives1()
    {
        return base.Predicate_based_on_element_of_json_array_of_primitives1();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Predicate_based_on_element_of_json_array_of_primitives2()
    {
        return base.Predicate_based_on_element_of_json_array_of_primitives2();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Predicate_based_on_element_of_json_array_of_primitives3()
    {
        return base.Predicate_based_on_element_of_json_array_of_primitives3();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_element_of_json_array_of_primitives()
    {
        return base.Project_element_of_json_array_of_primitives();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_json_array_of_primitives_on_reference()
    {
        return base.Project_json_array_of_primitives_on_reference();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_missing_required_scalar(bool async)
    {
        return base.Project_missing_required_scalar(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_nested_json_entity_with_missing_scalars(bool async)
    {
        return base.Project_nested_json_entity_with_missing_scalars(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_null_required_scalar(bool async)
    {
        return base.Project_null_required_scalar(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_root_entity_with_missing_required_navigation(bool async)
    {
        return base.Project_root_entity_with_missing_required_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_root_entity_with_null_required_navigation(bool async)
    {
        return base.Project_root_entity_with_null_required_navigation(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_root_with_missing_scalars(bool async)
    {
        return base.Project_root_with_missing_scalars(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_shadow_properties_from_json_entity()
    {
        return base.Project_shadow_properties_from_json_entity();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Project_top_level_json_entity_with_missing_scalars(bool async)
    {
        return base.Project_top_level_json_entity_with_missing_scalars(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Query_with_nested_json_collection_mapped_to_private_field_via_IReadOnlyList()
    {
        return base.Query_with_nested_json_collection_mapped_to_private_field_via_IReadOnlyList();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Shadow_properties_basic_no_tracking()
    {
        return base.Shadow_properties_basic_no_tracking();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Shadow_properties_basic_tracking()
    {
        return base.Shadow_properties_basic_tracking();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tricky_buffering_basic()
    {
        return base.Tricky_buffering_basic();
    }
}