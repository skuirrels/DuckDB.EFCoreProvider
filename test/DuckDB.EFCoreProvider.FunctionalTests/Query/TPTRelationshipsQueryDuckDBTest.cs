using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class TPTRelationshipsQueryDuckDBTest : TPTRelationshipsQueryTestBase<TPTRelationshipsQueryDuckDBTest.TPTRelationshipsQueryDuckDBFixture>
{
    public TPTRelationshipsQueryDuckDBTest(TPTRelationshipsQueryDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_on_derived1_split(bool async)
    {
        return base.Include_collection_with_inheritance_on_derived1_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_on_derived2_split(bool async)
    {
        return base.Include_collection_with_inheritance_on_derived2_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_on_derived3_split(bool async)
    {
        return base.Include_collection_with_inheritance_on_derived3_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_on_derived_reverse_split(bool async)
    {
        return base.Include_collection_with_inheritance_on_derived_reverse_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_reverse_split(bool async)
    {
        return base.Include_collection_with_inheritance_reverse_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_split(bool async)
    {
        return base.Include_collection_with_inheritance_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_with_filter_reverse_split(bool async)
    {
        return base.Include_collection_with_inheritance_with_filter_reverse_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_with_filter_split(bool async)
    {
        return base.Include_collection_with_inheritance_with_filter_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_without_inheritance_reverse_split(bool async)
    {
        return base.Include_collection_without_inheritance_reverse_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_without_inheritance_split(bool async)
    {
        return base.Include_collection_without_inheritance_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_without_inheritance_with_filter_reverse_split(bool async)
    {
        return base.Include_collection_without_inheritance_with_filter_reverse_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_without_inheritance(bool async)
    {
        return base.Include_collection_without_inheritance(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_on_derived1(bool async)
    {
        return base.Include_collection_with_inheritance_on_derived1(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_with_filter(bool async)
    {
        return base.Include_collection_with_inheritance_with_filter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance(bool async)
    {
        return base.Include_collection_with_inheritance(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_on_derived2(bool async)
    {
        return base.Include_collection_with_inheritance_on_derived2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_on_derived3(bool async)
    {
        return base.Include_collection_with_inheritance_on_derived3(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_on_derived_reverse(bool async)
    {
        return base.Include_collection_with_inheritance_on_derived_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_reverse(bool async)
    {
        return base.Include_collection_with_inheritance_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_inheritance_with_filter_reverse(bool async)
    {
        return base.Include_collection_with_inheritance_with_filter_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_without_inheritance_reverse(bool async)
    {
        return base.Include_collection_without_inheritance_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_without_inheritance_with_filter(bool async)
    {
        return base.Include_collection_without_inheritance_with_filter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_without_inheritance_with_filter_reverse(bool async)
    {
        return base.Include_collection_without_inheritance_with_filter_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_with_inheritance_on_derived1(bool async)
    {
        return base.Include_reference_with_inheritance_on_derived1(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_with_inheritance_with_filter(bool async)
    {
        return base.Include_reference_with_inheritance_with_filter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_without_inheritance_with_filter_split(bool async)
    {
        return base.Include_collection_without_inheritance_with_filter_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_include_collection_reference_on_non_entity_base(bool async)
    {
        return base.Nested_include_collection_reference_on_non_entity_base(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_on_derived_type_with_queryable_Cast_split(bool async)
    {
        return base.Include_on_derived_type_with_queryable_Cast_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_include_with_inheritance_collection_collection_split(bool async)
    {
        return base.Nested_include_with_inheritance_collection_collection_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_include_with_inheritance_collection_collection_reverse_split(bool async)
    {
        return base.Nested_include_with_inheritance_collection_collection_reverse_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_include_with_inheritance_collection_reference_split(bool async)
    {
        return base.Nested_include_with_inheritance_collection_reference_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_include_with_inheritance_reference_collection_on_base_split(bool async)
    {
        return base.Nested_include_with_inheritance_reference_collection_on_base_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_include_with_inheritance_reference_collection_split(bool async)
    {
        return base.Nested_include_with_inheritance_reference_collection_split(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_on_derived_type_with_queryable_Cast(bool async)
    {
        return base.Include_on_derived_type_with_queryable_Cast(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_with_inheritance(bool async)
    {
        return base.Include_reference_with_inheritance(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_with_inheritance_on_derived2(bool async)
    {
        return base.Include_reference_with_inheritance_on_derived2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_with_inheritance_on_derived4(bool async)
    {
        return base.Include_reference_with_inheritance_on_derived4(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_with_inheritance_on_derived_reverse(bool async)
    {
        return base.Include_reference_with_inheritance_on_derived_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_with_inheritance_on_derived_with_filter1(bool async)
    {
        return base.Include_reference_with_inheritance_on_derived_with_filter1(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_with_inheritance_on_derived_with_filter2(bool async)
    {
        return base.Include_reference_with_inheritance_on_derived_with_filter2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_with_inheritance_on_derived_with_filter4(bool async)
    {
        return base.Include_reference_with_inheritance_on_derived_with_filter4(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_with_inheritance_on_derived_with_filter_reverse(bool async)
    {
        return base.Include_reference_with_inheritance_on_derived_with_filter_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_with_inheritance_reverse(bool async)
    {
        return base.Include_reference_with_inheritance_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_with_inheritance_with_filter_reverse(bool async)
    {
        return base.Include_reference_with_inheritance_with_filter_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_without_inheritance(bool async)
    {
        return base.Include_reference_without_inheritance(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_without_inheritance_on_derived1(bool async)
    {
        return base.Include_reference_without_inheritance_on_derived1(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_without_inheritance_on_derived2(bool async)
    {
        return base.Include_reference_without_inheritance_on_derived2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_without_inheritance_on_derived_reverse(bool async)
    {
        return base.Include_reference_without_inheritance_on_derived_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_without_inheritance_reverse(bool async)
    {
        return base.Include_reference_without_inheritance_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_without_inheritance_with_filter(bool async)
    {
        return base.Include_reference_without_inheritance_with_filter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_reference_without_inheritance_with_filter_reverse(bool async)
    {
        return base.Include_reference_without_inheritance_with_filter_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_self_reference_with_inheritance(bool async)
    {
        return base.Include_self_reference_with_inheritance(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_self_reference_with_inheritance_reverse(bool async)
    {
        return base.Include_self_reference_with_inheritance_reverse(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_include_with_inheritance_collection_collection(bool async)
    {
        return base.Nested_include_with_inheritance_collection_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_include_with_inheritance_collection_reference(bool async)
    {
        return base.Nested_include_with_inheritance_collection_reference(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_include_with_inheritance_reference_collection(bool async)
    {
        return base.Nested_include_with_inheritance_reference_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_include_with_inheritance_reference_collection_on_base(bool async)
    {
        return base.Nested_include_with_inheritance_reference_collection_on_base(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_include_with_inheritance_reference_reference(bool async)
    {
        return base.Nested_include_with_inheritance_reference_reference(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_include_with_inheritance_reference_reference_on_base(bool async)
    {
        return base.Nested_include_with_inheritance_reference_reference_on_base(async);
    }

    public class TPTRelationshipsQueryDuckDBFixture : TPTRelationshipsQueryRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}