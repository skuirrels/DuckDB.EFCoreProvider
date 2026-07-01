using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.Navigations;

public class NavigationsPrimitiveCollectionDuckDBTest : NavigationsPrimitiveCollectionRelationalTestBase<NavigationsDuckDBFixture>
{
    public NavigationsPrimitiveCollectionDuckDBTest(NavigationsDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }
}
