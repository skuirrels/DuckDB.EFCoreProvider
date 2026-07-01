using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Update;

public class ComplexCollectionJsonUpdateDuckDBTest : ComplexCollectionJsonUpdateTestBase<ComplexCollectionJsonUpdateDuckDBTest.ComplexCollectionJsonUpdateDuckDBFixture>
{
    public ComplexCollectionJsonUpdateDuckDBTest(ComplexCollectionJsonUpdateDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public class ComplexCollectionJsonUpdateDuckDBFixture : ComplexCollectionJsonUpdateFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
