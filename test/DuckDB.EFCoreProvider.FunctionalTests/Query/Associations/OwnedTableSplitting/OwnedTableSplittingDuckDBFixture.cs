using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query.Associations.OwnedTableSplitting;

public class OwnedTableSplittingDuckDBFixture : OwnedTableSplittingRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;
}
