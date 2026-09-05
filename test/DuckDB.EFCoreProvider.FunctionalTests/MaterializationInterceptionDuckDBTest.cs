using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class MaterializationInterceptionDuckDBTest : MaterializationInterceptionTestBase<MaterializationInterceptionDuckDBTest.DuckDBLibraryContext>
{
    public MaterializationInterceptionDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Intercept_query_materialization_with_owned_types(bool async, bool usePooling)
    {
        await base.Intercept_query_materialization_with_owned_types(async, usePooling);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Intercept_query_materialization_with_owned_types_projecting_collection(bool async, bool usePooling)
    {
        await base.Intercept_query_materialization_with_owned_types_projecting_collection(async, usePooling);
    }

    public class DuckDBLibraryContext(DbContextOptions options) : LibraryContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TestEntity30244>().OwnsMany(e => e.Settings, b => b.ToJson());
        }
    }

#if NET11_0_OR_GREATER
    protected override DbContextOptionsBuilder AddNonSharedOptions(DbContextOptionsBuilder builder)
        => base.AddNonSharedOptions(builder).ConfigureWarnings(
            warnings => warnings
                .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)
                // These inherited tests intentionally exercise the legacy owned JSON model.
                .Log(RelationalEventId.OwnedEntityMappedToJsonCollectionWarning));
#else
    protected override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder).ConfigureWarnings(
            warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
#endif

#if NET11_0_OR_GREATER
    protected override ITestStoreFactory NonSharedTestStoreFactory
#else
    protected override ITestStoreFactory TestStoreFactory
#endif
        => DuckDBTestStoreFactory.Instance;
}
