using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindSplitIncludeQueryDuckDBTest : NorthwindSplitIncludeQueryTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindSplitIncludeQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture) : base(fixture)
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
    public override Task Include_collection_with_outer_apply_with_filter(bool async)
    {
        return base.Include_collection_with_outer_apply_with_filter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_outer_apply_with_filter_non_equality(bool async)
    {
        return base.Include_collection_with_outer_apply_with_filter_non_equality(async);
    }
}
