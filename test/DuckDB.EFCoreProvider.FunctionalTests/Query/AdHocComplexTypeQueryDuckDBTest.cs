using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class AdHocComplexTypeQueryDuckDBTest : AdHocComplexTypeQueryRelationalTestBase
{
    public AdHocComplexTypeQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }
    
    protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
}
