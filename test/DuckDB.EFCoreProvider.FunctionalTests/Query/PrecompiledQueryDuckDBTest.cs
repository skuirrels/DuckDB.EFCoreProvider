using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class PrecompiledQueryDuckDBTest : PrecompiledQueryRelationalTestBase, IClassFixture<PrecompiledQueryDuckDBTest.PrecompiledQueryDuckDBFixture>
{
    public PrecompiledQueryDuckDBTest(PrecompiledQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task BinaryExpression()
    {
        return base.BinaryExpression();
    }

    public class PrecompiledQueryDuckDBFixture : PrecompiledQueryRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public override PrecompiledQueryTestHelpers PrecompiledQueryTestHelpers
            => DuckDBPrecompiledQueryTestHelpers.Instance;
    }
}
