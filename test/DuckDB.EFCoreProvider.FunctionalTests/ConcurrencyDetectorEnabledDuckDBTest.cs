using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

public class ConcurrencyDetectorEnabledDuckDBTest : ConcurrencyDetectorEnabledRelationalTestBase<
    ConcurrencyDetectorEnabledDuckDBTest.ConcurrencyDetectorDuckDBFixture>
{
    public ConcurrencyDetectorEnabledDuckDBTest(ConcurrencyDetectorDuckDBFixture fixture) : base(fixture)
    {
    }

    public class ConcurrencyDetectorDuckDBFixture : ConcurrencyDetectorFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;
    }
}
