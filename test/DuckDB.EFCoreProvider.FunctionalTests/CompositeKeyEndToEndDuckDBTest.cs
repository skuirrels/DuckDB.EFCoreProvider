using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

public class CompositeKeyEndToEndDuckDBTest : CompositeKeyEndToEndTestBase<CompositeKeyEndToEndDuckDBTest.CompositeKeyEndToEndDuckDBFixture>
{
    public CompositeKeyEndToEndDuckDBTest(CompositeKeyEndToEndDuckDBFixture fixture) : base(fixture)
    {
    }

    public class CompositeKeyEndToEndDuckDBFixture : CompositeKeyEndToEndFixtureBase
    {
        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder.ConfigureWarnings(b =>
            {
                // b.Ignore(DuckDBEventId.CompositeKeyWithValueGeneration)
            }));

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
