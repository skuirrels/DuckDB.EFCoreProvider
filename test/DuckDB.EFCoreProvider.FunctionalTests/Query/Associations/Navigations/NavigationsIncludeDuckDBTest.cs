using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.Navigations;

public class NavigationsIncludeDuckDBTest: NavigationsIncludeRelationalTestBase<NavigationsDuckDBFixture>
{
    public NavigationsIncludeDuckDBTest(NavigationsDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }
}