using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindCompiledQueryDuckDBTest : NorthwindCompiledQueryTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindCompiledQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture) : base(fixture)
    {
    }
}
