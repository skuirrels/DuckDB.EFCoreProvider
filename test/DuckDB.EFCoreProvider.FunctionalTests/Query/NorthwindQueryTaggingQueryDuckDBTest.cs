using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindQueryTaggingQueryDuckDBTest : NorthwindQueryTaggingQueryTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindQueryTaggingQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture) : base(fixture)
    {
    }
}
