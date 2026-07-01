using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

public class ConcurrencyDetectorDisabledDuckDBTest : ConcurrencyDetectorDisabledRelationalTestBase<
    ConcurrencyDetectorDisabledDuckDBTest.ConcurrencyDetectorDuckDBFixture>
{
    public ConcurrencyDetectorDisabledDuckDBTest(ConcurrencyDetectorDuckDBFixture fixture) : base(fixture)
    {
    }

    public class ConcurrencyDetectorDuckDBFixture : ConcurrencyDetectorFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => builder.EnableThreadSafetyChecks(enableChecks: false);
    }
}
