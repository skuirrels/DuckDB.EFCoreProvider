using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class AdHocManyToManyQueryDuckDBTest : AdHocManyToManyQueryRelationalTestBase
{
    public AdHocManyToManyQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }
    
    protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
}
