using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.Navigations;

public class NavigationsMiscellaneousDuckDBTest: NavigationsMiscellaneousRelationalTestBase<NavigationsDuckDBFixture>
{
    public NavigationsMiscellaneousDuckDBTest(NavigationsDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }
}