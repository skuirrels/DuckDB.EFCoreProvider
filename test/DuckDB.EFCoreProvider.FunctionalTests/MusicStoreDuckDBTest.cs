using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

public class MusicStoreDuckDBTest : MusicStoreTestBase<MusicStoreDuckDBTest.MusicStoreDuckDBFixture>
{
    public MusicStoreDuckDBTest(MusicStoreDuckDBFixture fixture) : base(fixture)
    {
    }

    public class MusicStoreDuckDBFixture : MusicStoreFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
