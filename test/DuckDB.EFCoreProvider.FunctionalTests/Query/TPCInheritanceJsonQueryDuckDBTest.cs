#if NET11_0_OR_GREATER
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class TPCInheritanceJsonQueryDuckDBTest(
    TPCInheritanceJsonQueryDuckDBTest.TPCInheritanceJsonQueryDuckDBFixture fixture,
    ITestOutputHelper testOutputHelper)
    : TPCInheritanceJsonQueryRelationalTestBase<TPCInheritanceJsonQueryDuckDBTest.TPCInheritanceJsonQueryDuckDBFixture>(
        fixture,
        testOutputHelper)
{
    public class TPCInheritanceJsonQueryDuckDBFixture : TPCInheritanceJsonQueryRelationalFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public override bool UseGeneratedKeys
            => false;
    }
}
#endif