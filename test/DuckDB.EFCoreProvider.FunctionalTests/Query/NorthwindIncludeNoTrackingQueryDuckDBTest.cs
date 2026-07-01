using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindIncludeNoTrackingQueryDuckDBTest : NorthwindIncludeNoTrackingQueryTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindIncludeNoTrackingQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_last_no_orderby(bool async)
    {
        return base.Include_collection_with_last_no_orderby(async);
    }
}
