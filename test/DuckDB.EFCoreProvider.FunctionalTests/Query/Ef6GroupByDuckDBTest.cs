using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class Ef6GroupByDuckDBTest : Ef6GroupByTestBase<Ef6GroupByDuckDBTest.Ef6GroupByDuckDBFixture>
{
    public Ef6GroupByDuckDBTest(Ef6GroupByDuckDBFixture fixture) : base(fixture)
    {
    }

    public class Ef6GroupByDuckDBFixture : Ef6GroupByFixtureBase, ITestSqlLoggerFactory
    {
        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Average_Grouped_from_LINQ_101(bool async)
    {
        return base.Average_Grouped_from_LINQ_101(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Whats_new_2021_sample_3(bool async)
    {
        return base.Whats_new_2021_sample_3(async);
    }
}
