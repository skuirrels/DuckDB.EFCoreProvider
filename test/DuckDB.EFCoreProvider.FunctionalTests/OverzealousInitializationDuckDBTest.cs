using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

public class OverzealousInitializationDuckDBTest : OverzealousInitializationTestBase<OverzealousInitializationDuckDBTest.OverzealousInitializationDuckDBFixture>
{
    public OverzealousInitializationDuckDBTest(OverzealousInitializationDuckDBFixture fixture) : base(fixture)
    {
    }

    public class OverzealousInitializationDuckDBFixture : OverzealousInitializationFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
