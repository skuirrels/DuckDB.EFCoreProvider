using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindAsTrackingQueryDuckDBTest : NorthwindAsTrackingQueryTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindAsTrackingQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture) : base(fixture)
    {
    }
}
