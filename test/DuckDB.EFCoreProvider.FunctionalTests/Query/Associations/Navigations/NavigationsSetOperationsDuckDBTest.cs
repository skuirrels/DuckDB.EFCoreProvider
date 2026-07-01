using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.Navigations;

public class NavigationsSetOperationsDuckDBTest : NavigationsSetOperationsRelationalTestBase<NavigationsDuckDBFixture>
{
    public NavigationsSetOperationsDuckDBTest(NavigationsDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }
}