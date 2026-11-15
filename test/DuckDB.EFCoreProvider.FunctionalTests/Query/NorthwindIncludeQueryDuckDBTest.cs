using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindIncludeQueryDuckDBTest : NorthwindIncludeQueryRelationalTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindIncludeQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_with_multiple_ordering(bool async)
    {
        return base.Filtered_include_with_multiple_ordering(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_cross_apply_with_filter(bool async)
    {
        return base.Include_collection_with_cross_apply_with_filter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_multiple_conditional_order_by(bool async)
    {
        return base.Include_collection_with_multiple_conditional_order_by(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_outer_apply_with_filter(bool async)
    {
        return base.Include_collection_with_outer_apply_with_filter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_outer_apply_with_filter_non_equality(bool async)
    {
        return base.Include_collection_with_outer_apply_with_filter_non_equality(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Repro9735(bool async)
    {
        return base.Repro9735(async);
    }
}