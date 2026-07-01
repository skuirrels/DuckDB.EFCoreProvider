using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.Navigations;

public class NavigationsCollectionDuckDBTest: NavigationsCollectionRelationalTestBase<NavigationsDuckDBFixture>
{
    public NavigationsCollectionDuckDBTest(NavigationsDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture, testOutputHelper)
    {
    }
}
