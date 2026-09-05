using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class AdHocAdvancedMappingsQueryDuckDBTest : AdHocAdvancedMappingsQueryRelationalTestBase
{
    public AdHocAdvancedMappingsQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

#if NET11_0_OR_GREATER
    protected override ITestStoreFactory NonSharedTestStoreFactory
#else
    protected override ITestStoreFactory TestStoreFactory
#endif
        => DuckDBTestStoreFactory.Instance;
}
