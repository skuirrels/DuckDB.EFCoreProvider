using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class AdHocQueryFiltersQueryDuckDBTest : AdHocQueryFiltersQueryRelationalTestBase
{
    public AdHocQueryFiltersQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;
}
