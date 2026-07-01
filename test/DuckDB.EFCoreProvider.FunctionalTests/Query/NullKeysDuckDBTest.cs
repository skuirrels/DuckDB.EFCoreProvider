using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class NullKeysDuckDBTest : NullKeysTestBase<NullKeysDuckDBTest.NullKeysDuckDBFixture>
{
    public NullKeysDuckDBTest(NullKeysDuckDBFixture fixture) : base(fixture)
    {
    }

    public class NullKeysDuckDBFixture : NullKeysFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}