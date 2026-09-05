using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

public class SeedingDuckDBTest : SeedingTestBase
{
    protected override TestStore TestStore
        => DuckDBTestStore.Create("SeedingTest");

    protected override SeedingContext CreateContextWithEmptyDatabase(string testId)
        => new SeedingDuckDBContext(testId);

    protected override KeylessSeedingContext CreateKeylessContextWithEmptyDatabase()
        => new(TestStore.AddProviderOptions(new DbContextOptionsBuilder())
            .EnableServiceProviderCaching(false)
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options);

    protected class SeedingDuckDBContext(string testId) : SeedingContext(testId)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseDuckDB(($"Data Source = Seeds{TestId}.db"))
                .EnableServiceProviderCaching(false)
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
    }
}
