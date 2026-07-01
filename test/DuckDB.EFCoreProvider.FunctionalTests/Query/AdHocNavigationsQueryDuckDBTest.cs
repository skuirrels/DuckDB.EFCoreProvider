using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class AdHocNavigationsQueryDuckDBTest : AdHocNavigationsQueryRelationalTestBase
{
    public AdHocNavigationsQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
}
