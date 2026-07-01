using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class FunkyDataQueryDuckDBTest : FunkyDataQueryTestBase<FunkyDataQueryDuckDBTest.FunkyDataQueryDuckDBFixture>
{
    public FunkyDataQueryDuckDBTest(FunkyDataQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper) :
        base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public class FunkyDataQueryDuckDBFixture : FunkyDataQueryFixtureBase, ITestSqlLoggerFactory
    {
        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
    }
}
