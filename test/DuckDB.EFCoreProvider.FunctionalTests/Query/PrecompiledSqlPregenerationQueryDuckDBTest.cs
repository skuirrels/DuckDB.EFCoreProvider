using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class PrecompiledSqlPregenerationQueryDuckDBTest : PrecompiledSqlPregenerationQueryRelationalTestBase,
    IClassFixture<PrecompiledSqlPregenerationQueryDuckDBTest.PrecompiledSqlPregenerationQueryDuckDBFixture>
{
    public PrecompiledSqlPregenerationQueryDuckDBTest(PrecompiledSqlPregenerationQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    public class PrecompiledSqlPregenerationQueryDuckDBFixture : PrecompiledSqlPregenerationQueryRelationalFixture
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public override PrecompiledQueryTestHelpers PrecompiledQueryTestHelpers
            => DuckDBPrecompiledQueryTestHelpers.Instance;
    }
}