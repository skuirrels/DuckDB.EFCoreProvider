using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore;

public class EntitySplittingDuckDBTest : EntitySplittingTestBase
{
    public EntitySplittingDuckDBTest(NonSharedFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture, testOutputHelper)
    {
    }

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => base.OnModelCreating(modelBuilder);
}
