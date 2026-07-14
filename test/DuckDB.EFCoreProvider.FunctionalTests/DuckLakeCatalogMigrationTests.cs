using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Metadata;
using System.IO.Compression;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class DuckLakeCatalogMigrationTests
{
    private const string FixtureResourceName =
        "DuckDB.EFCoreProvider.FunctionalTests.TestData.DuckLake.v03.db.gz.b64";

    [Fact]
    public async Task Real_v03_catalog_is_migrated_before_EnsureCreated_checks_existing_tables()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake-v03-migration-{Guid.NewGuid():N}");
        var metadataPath = Path.Combine(root, "catalog.ducklake");
        var dataPath = Path.Combine(root, "data");
        Directory.CreateDirectory(dataPath);

        try
        {
            ExtractFixture(metadataPath);

            var optionsWithoutMigration = CreateOptions(metadataPath, dataPath, automaticMigration: false);
            await using (var context = new LegacyCatalogContext(optionsWithoutMigration))
            {
                Assert.False(await context.Database.CanConnectAsync());
            }

            var migrationOptions = CreateOptions(metadataPath, dataPath, automaticMigration: true);
            await using (var context = new LegacyCatalogContext(migrationOptions))
            {
                Assert.False(await context.Database.EnsureCreatedAsync());
                Assert.Empty(await context.Rows.AsNoTracking().ToListAsync());

                context.Rows.Add(new LegacyCatalogRow { Id = 42 });
                Assert.Equal(1, await context.SaveChangesAsync());
            }

            // A normal profile can reopen the catalog after the upgrade, proving that AUTOMATIC_MIGRATION
            // changed the persisted catalog version rather than only affecting the first connection.
            await using (var context = new LegacyCatalogContext(optionsWithoutMigration))
            {
                Assert.True(await context.Database.CanConnectAsync());
                Assert.Equal(42, Assert.Single(await context.Rows.AsNoTracking().ToListAsync()).Id);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static DbContextOptions<LegacyCatalogContext> CreateOptions(
        string metadataPath,
        string dataPath,
        bool automaticMigration)
        => new DbContextOptionsBuilder<LegacyCatalogContext>()
            .UseDuckLake(
                metadataPath,
                duckLake =>
                {
                    duckLake
                        .CatalogName("legacy")
                        .DataPath(dataPath, overrideForCurrentConnection: true)
                        .CreateIfNotExists(false);
                    if (automaticMigration)
                    {
                        duckLake.AutomaticMigration();
                    }
                })
            .Options;

    private static void ExtractFixture(string metadataPath)
    {
        using var resource = typeof(DuckLakeCatalogMigrationTests).Assembly
            .GetManifestResourceStream(FixtureResourceName)
            ?? throw new InvalidOperationException($"Embedded DuckLake migration fixture '{FixtureResourceName}' was not found.");
        using var reader = new StreamReader(resource);
        var compressedBytes = Convert.FromBase64String(reader.ReadToEnd());
        using var compressed = new MemoryStream(compressedBytes);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var metadata = File.Create(metadataPath);
        gzip.CopyTo(metadata);
    }

    private sealed class LegacyCatalogContext(DbContextOptions<LegacyCatalogContext> options) : DbContext(options)
    {
        public DbSet<LegacyCatalogRow> Rows => Set<LegacyCatalogRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<LegacyCatalogRow>(entity =>
            {
                entity.ToTable("test");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id)
                    .HasColumnName("i")
                    .ValueGeneratedNever();
            });
    }

    private sealed class LegacyCatalogRow
    {
        public int Id { get; set; }
    }
}
