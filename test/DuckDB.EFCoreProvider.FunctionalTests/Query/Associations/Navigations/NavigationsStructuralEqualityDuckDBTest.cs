using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.Navigations;

public class NavigationsStructuralEqualityDuckDBTest : NavigationsStructuralEqualityRelationalTestBase<NavigationsDuckDBFixture>
{
    public NavigationsStructuralEqualityDuckDBTest(NavigationsDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }
}