#if NET11_0_OR_GREATER
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class TPHInheritanceJsonQueryDuckDBTest(
    TPHInheritanceJsonQueryDuckDBTest.TPHInheritanceJsonQueryDuckDBFixture fixture,
    ITestOutputHelper testOutputHelper)
    : TPHInheritanceJsonQueryRelationalTestBase<TPHInheritanceJsonQueryDuckDBTest.TPHInheritanceJsonQueryDuckDBFixture>(
        fixture,
        testOutputHelper)
{
    public class TPHInheritanceJsonQueryDuckDBFixture : TPHInheritanceJsonQueryRelationalFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
#endif