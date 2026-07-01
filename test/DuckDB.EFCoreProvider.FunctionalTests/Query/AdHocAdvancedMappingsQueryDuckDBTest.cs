using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class AdHocAdvancedMappingsQueryDuckDBTest : AdHocAdvancedMappingsQueryRelationalTestBase
{
    public AdHocAdvancedMappingsQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
}
