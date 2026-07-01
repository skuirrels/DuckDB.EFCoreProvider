using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

public class SeedingDuckDBTest : SeedingTestBase
{
    protected override TestStore TestStore
        => DuckDBTestStore.Create("SeedingTest");

    protected override SeedingContext CreateContextWithEmptyDatabase(string testId)
        => new SeedingDuckDBContext(testId);

    protected class SeedingDuckDBContext(string testId) : SeedingContext(testId)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseDuckDB(($"Data Source = Seeds{TestId}.db"));
    }
}
