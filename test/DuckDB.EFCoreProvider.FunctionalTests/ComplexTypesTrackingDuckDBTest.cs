using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore;

public class ComplexTypesTrackingDuckDBTest : ComplexTypesTrackingRelationalTestBase<ComplexTypesTrackingDuckDBTest.DuckDBFixture>
{
    public ComplexTypesTrackingDuckDBTest(DuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    public class DuckDBFixture : RelationalFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
