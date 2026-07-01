using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class EntitySplittingQueryDuckDBTest : EntitySplittingQueryTestBase
{
    public EntitySplittingQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Normal_entity_owning_a_split_collection(bool async)
    {
        return base.Normal_entity_owning_a_split_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Normal_entity_owning_a_split_reference_with_main_fragment_not_sharing(bool async)
    {
        return base.Normal_entity_owning_a_split_reference_with_main_fragment_not_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Normal_entity_owning_a_split_reference_with_main_fragment_sharing(bool async)
    {
        return base.Normal_entity_owning_a_split_reference_with_main_fragment_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Normal_entity_owning_a_split_reference_with_main_fragment_sharing_multiple_level(bool async)
    {
        return base.Normal_entity_owning_a_split_reference_with_main_fragment_sharing_multiple_level(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Split_entity_owning_a_collection(bool async)
    {
        return base.Split_entity_owning_a_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Split_entity_owning_a_reference(bool async)
    {
        return base.Split_entity_owning_a_reference(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Split_entity_owning_a_split_collection(bool async)
    {
        return base.Split_entity_owning_a_split_collection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Split_entity_owning_a_split_reference_with_table_sharing_1(bool async)
    {
        return base.Split_entity_owning_a_split_reference_with_table_sharing_1(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Split_entity_owning_a_split_reference_with_table_sharing_4(bool async)
    {
        return base.Split_entity_owning_a_split_reference_with_table_sharing_4(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Split_entity_owning_a_split_reference_with_table_sharing_6(bool async)
    {
        return base.Split_entity_owning_a_split_reference_with_table_sharing_6(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Split_entity_owning_a_split_reference_without_table_sharing(bool async)
    {
        return base.Split_entity_owning_a_split_reference_without_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpc_entity_owning_a_split_collection_on_base(bool async)
    {
        return base.Tpc_entity_owning_a_split_collection_on_base(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpc_entity_owning_a_split_collection_on_leaf(bool async)
    {
        return base.Tpc_entity_owning_a_split_collection_on_leaf(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpc_entity_owning_a_split_collection_on_middle(bool async)
    {
        return base.Tpc_entity_owning_a_split_collection_on_middle(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpc_entity_owning_a_split_reference_on_base_without_table_sharing(bool async)
    {
        return base.Tpc_entity_owning_a_split_reference_on_base_without_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpc_entity_owning_a_split_reference_on_leaf_with_table_sharing(bool async)
    {
        return base.Tpc_entity_owning_a_split_reference_on_leaf_with_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpc_entity_owning_a_split_reference_on_leaf_with_table_sharing_querying_sibling(bool async)
    {
        return base.Tpc_entity_owning_a_split_reference_on_leaf_with_table_sharing_querying_sibling(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpc_entity_owning_a_split_reference_on_leaf_without_table_sharing(bool async)
    {
        return base.Tpc_entity_owning_a_split_reference_on_leaf_without_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpc_entity_owning_a_split_reference_on_middle_without_table_sharing(bool async)
    {
        return base.Tpc_entity_owning_a_split_reference_on_middle_without_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tph_entity_owning_a_split_collection_on_base(bool async)
    {
        return base.Tph_entity_owning_a_split_collection_on_base(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tph_entity_owning_a_split_collection_on_leaf(bool async)
    {
        return base.Tph_entity_owning_a_split_collection_on_leaf(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tph_entity_owning_a_split_collection_on_middle(bool async)
    {
        return base.Tph_entity_owning_a_split_collection_on_middle(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tph_entity_owning_a_split_reference_on_base_with_table_sharing(bool async)
    {
        return base.Tph_entity_owning_a_split_reference_on_base_with_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tph_entity_owning_a_split_reference_on_base_with_table_sharing_querying_sibling(bool async)
    {
        return base.Tph_entity_owning_a_split_reference_on_base_with_table_sharing_querying_sibling(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tph_entity_owning_a_split_reference_on_base_without_table_sharing(bool async)
    {
        return base.Tph_entity_owning_a_split_reference_on_base_without_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tph_entity_owning_a_split_reference_on_leaf_with_table_sharing(bool async)
    {
        return base.Tph_entity_owning_a_split_reference_on_leaf_with_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tph_entity_owning_a_split_reference_on_leaf_with_table_sharing_querying_sibling(bool async)
    {
        return base.Tph_entity_owning_a_split_reference_on_leaf_with_table_sharing_querying_sibling(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tph_entity_owning_a_split_reference_on_leaf_without_table_sharing(bool async)
    {
        return base.Tph_entity_owning_a_split_reference_on_leaf_without_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tph_entity_owning_a_split_reference_on_middle_with_table_sharing(bool async)
    {
        return base.Tph_entity_owning_a_split_reference_on_middle_with_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tph_entity_owning_a_split_reference_on_middle_with_table_sharing_querying_sibling(bool async)
    {
        return base.Tph_entity_owning_a_split_reference_on_middle_with_table_sharing_querying_sibling(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tph_entity_owning_a_split_reference_on_middle_without_table_sharing(bool async)
    {
        return base.Tph_entity_owning_a_split_reference_on_middle_without_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpt_entity_owning_a_split_collection_on_base(bool async)
    {
        return base.Tpt_entity_owning_a_split_collection_on_base(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpt_entity_owning_a_split_collection_on_leaf(bool async)
    {
        return base.Tpt_entity_owning_a_split_collection_on_leaf(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpt_entity_owning_a_split_collection_on_middle(bool async)
    {
        return base.Tpt_entity_owning_a_split_collection_on_middle(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpt_entity_owning_a_split_reference_on_base_with_table_sharing(bool async)
    {
        return base.Tpt_entity_owning_a_split_reference_on_base_with_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpt_entity_owning_a_split_reference_on_base_with_table_sharing_querying_sibling(bool async)
    {
        return base.Tpt_entity_owning_a_split_reference_on_base_with_table_sharing_querying_sibling(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpt_entity_owning_a_split_reference_on_base_without_table_sharing(bool async)
    {
        return base.Tpt_entity_owning_a_split_reference_on_base_without_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpt_entity_owning_a_split_reference_on_leaf_with_table_sharing(bool async)
    {
        return base.Tpt_entity_owning_a_split_reference_on_leaf_with_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpt_entity_owning_a_split_reference_on_leaf_with_table_sharing_querying_sibling(bool async)
    {
        return base.Tpt_entity_owning_a_split_reference_on_leaf_with_table_sharing_querying_sibling(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpt_entity_owning_a_split_reference_on_leaf_without_table_sharing(bool async)
    {
        return base.Tpt_entity_owning_a_split_reference_on_leaf_without_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpt_entity_owning_a_split_reference_on_middle_with_table_sharing(bool async)
    {
        return base.Tpt_entity_owning_a_split_reference_on_middle_with_table_sharing(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpt_entity_owning_a_split_reference_on_middle_with_table_sharing_querying_sibling(bool async)
    {
        return base.Tpt_entity_owning_a_split_reference_on_middle_with_table_sharing_querying_sibling(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Tpt_entity_owning_a_split_reference_on_middle_without_table_sharing(bool async)
    {
        return base.Tpt_entity_owning_a_split_reference_on_middle_without_table_sharing(async);
    }
}
