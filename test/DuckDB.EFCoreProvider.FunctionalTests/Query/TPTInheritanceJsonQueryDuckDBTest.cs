#if NET11_0_OR_GREATER
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class TPTInheritanceJsonQueryDuckDBTest(
    TPTInheritanceJsonQueryDuckDBTest.TPTInheritanceJsonQueryDuckDBFixture fixture,
    ITestOutputHelper testOutputHelper)
    : TPTInheritanceJsonQueryRelationalTestBase<TPTInheritanceJsonQueryDuckDBTest.TPTInheritanceJsonQueryDuckDBFixture>(
        fixture,
        testOutputHelper)
{
    public class TPTInheritanceJsonQueryDuckDBFixture : TPTInheritanceJsonQueryRelationalFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
#endif