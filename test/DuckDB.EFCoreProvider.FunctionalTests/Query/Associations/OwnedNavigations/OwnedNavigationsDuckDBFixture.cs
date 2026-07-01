using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query.Associations.OwnedNavigations;

public class OwnedNavigationsDuckDBFixture : OwnedNavigationsRelationalFixtureBase
{
    protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
}
