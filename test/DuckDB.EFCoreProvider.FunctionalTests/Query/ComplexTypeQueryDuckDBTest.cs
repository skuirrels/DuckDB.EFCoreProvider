using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class ComplexTypeQueryDuckDBTest : ComplexTypeQueryRelationalTestBase<ComplexTypeQueryDuckDBTest.ComplexTypeQueryDuckDBFixture>
{
    public class ComplexTypeQueryDuckDBFixture : ComplexTypeQueryRelationalFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }

    public ComplexTypeQueryDuckDBTest(ComplexTypeQueryDuckDBFixture fixture) : base(fixture)
    {
    }
}
