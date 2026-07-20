using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class DuckLakeAdditionalAttachmentTests
{
    [ConditionalFact]
    public async Task Additional_read_only_catalog_is_available_to_catalog_qualified_dynamic_SQL()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake_attachments_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var referenceMetadata = Path.Combine(root, "reference.ducklake");
        var referenceData = Path.Combine(root, "reference-data");
        var primaryMetadata = Path.Combine(root, "primary.ducklake");
        var primaryData = Path.Combine(root, "primary-data");
        Directory.CreateDirectory(referenceData);
        Directory.CreateDirectory(primaryData);

        try
        {
            var referenceOptions = new DbContextOptionsBuilder<ReferenceContext>()
                .UseDuckLake(
                    referenceMetadata,
                    duckLake => duckLake.CatalogName("reference").DataPath(referenceData))
                .Options;
            await using (var reference = new ReferenceContext(referenceOptions))
            {
                await reference.Database.EnsureCreatedAsync();
                reference.Items.Add(new ReferenceItem { Id = 1, Value = "shared" });
                await reference.SaveChangesAsync();
            }

            var primaryOptions = new DbContextOptionsBuilder<PrimaryContext>()
                .UseDuckLake(
                    primaryMetadata,
                    duckLake => duckLake
                        .CatalogName("analytics")
                        .DataPath(primaryData)
                        .AlsoAttach("reference", referenceMetadata))
                .Options;
            await using var primary = new PrimaryContext(primaryOptions);
            await primary.Database.EnsureCreatedAsync();

            await using var result = await primary.Database.SqlQueryDynamicRawAsync(
                "SELECT Value FROM reference.main.reference_items");
            var values = new List<string>();
            await foreach (var row in result.ReadRowsAsync())
            {
                values.Add((string)row.ToArray()[0]!);
            }

            Assert.Equal(["shared"], values);
            Assert.True(await primary.Database.CanConnectAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Additional_attachment_is_safely_quoted_and_duplicate_aliases_are_rejected()
    {
        var command = DuckLakeAttachCommandBuilder.Build(new DuckLakeOptions
        {
            MetadataSource = "primary.ducklake",
            CatalogName = "analytics",
            AdditionalCatalogs =
            [
                new DuckLakeOptions
                {
                    MetadataSource = "reference.ducklake",
                    CatalogName = "reference",
                    IsReadOnly = true,
                    CreateIfNotExists = false
                }
            ]
        });

        Assert.Contains("AS \"analytics\"", command);
        Assert.Contains("AS \"reference\" (CREATE_IF_NOT_EXISTS false, READ_ONLY)", command);
        Assert.EndsWith("USE \"analytics\";", command);

        var options = new DbContextOptionsBuilder<PrimaryContext>()
            .UseDuckLake(
                "primary.ducklake",
                duckLake => duckLake.CatalogName("analytics").AlsoAttach("analytics", "other.ducklake"))
            .Options;
        var exception = Assert.Throws<InvalidOperationException>(
            () => options.FindExtension<DuckDBOptionsExtension>()!.Validate(options));
        Assert.Contains("configured more than once", exception.Message);
    }

    [ConditionalFact]
    public async Task Existing_additional_alias_with_a_different_metadata_source_is_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake_attachment_identity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var configuredMetadata = Path.Combine(root, "configured.ducklake");
        var configuredData = Path.Combine(root, "configured-data");
        var existingMetadata = Path.Combine(root, "existing.ducklake");
        var existingData = Path.Combine(root, "existing-data");
        var primaryMetadata = Path.Combine(root, "primary.ducklake");
        var primaryData = Path.Combine(root, "primary-data");
        Directory.CreateDirectory(configuredData);
        Directory.CreateDirectory(existingData);
        Directory.CreateDirectory(primaryData);

        try
        {
            await CreateReferenceCatalogAsync(configuredMetadata, configuredData, "configured");
            await CreateReferenceCatalogAsync(existingMetadata, existingData, "existing");

            using (var connection = new DuckDBConnection("Data Source=:memory:"))
            {
                connection.Open();
                ExecuteNonQuery(
                    connection,
                    $"INSTALL ducklake; LOAD ducklake; ATTACH 'ducklake:{EscapeSqlLiteral(existingMetadata)}' "
                    + "AS reference (READ_ONLY);");
                var options = new DbContextOptionsBuilder<PrimaryContext>()
                    .UseDuckDB(
                        connection,
                        duckDB => duckDB.UseDuckLake(
                            primaryMetadata,
                            lake => lake
                                .CatalogName("analytics")
                                .DataPath(primaryData)
                                .AlsoAttach("reference", configuredMetadata)))
                    .Options;
                using var context = new PrimaryContext(options);

                var exception = Assert.Throws<InvalidOperationException>(() => context.Database.OpenConnection());

                Assert.Contains("different DuckLake metadata source", exception.Message);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [ConditionalFact]
    public async Task Existing_writable_additional_alias_is_rejected_when_the_profile_requires_read_only()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake_attachment_mode_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var referenceMetadata = Path.Combine(root, "reference.ducklake");
        var referenceData = Path.Combine(root, "reference-data");
        var primaryMetadata = Path.Combine(root, "primary.ducklake");
        var primaryData = Path.Combine(root, "primary-data");
        Directory.CreateDirectory(referenceData);
        Directory.CreateDirectory(primaryData);

        try
        {
            await CreateReferenceCatalogAsync(referenceMetadata, referenceData, "reference");

            await using (var connection = new DuckDBConnection("Data Source=:memory:"))
            {
                await connection.OpenAsync();
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        $"INSTALL ducklake; LOAD ducklake; ATTACH 'ducklake:{EscapeSqlLiteral(referenceMetadata)}' "
                        + "AS reference;";
                    await command.ExecuteNonQueryAsync();
                }

                var options = new DbContextOptionsBuilder<PrimaryContext>()
                    .UseDuckDB(
                        connection,
                        duckDB => duckDB.UseDuckLake(
                            primaryMetadata,
                            lake => lake
                                .CatalogName("analytics")
                                .DataPath(primaryData)
                                .AlsoAttach("reference", referenceMetadata)))
                    .Options;
                await using var context = new PrimaryContext(options);

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => context.Database.OpenConnectionAsync());

                Assert.Contains("profile requires a read-only attachment", exception.Message);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreateReferenceCatalogAsync(string metadataPath, string dataPath, string value)
    {
        var options = new DbContextOptionsBuilder<ReferenceContext>()
            .UseDuckLake(metadataPath, lake => lake.CatalogName("reference_source").DataPath(dataPath))
            .Options;
        await using var context = new ReferenceContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Items.Add(new ReferenceItem { Id = 1, Value = value });
        await context.SaveChangesAsync();
    }

    private static void ExecuteNonQuery(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string EscapeSqlLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed class PrimaryContext(DbContextOptions<PrimaryContext> options) : DbContext(options)
    {
        public DbSet<PrimaryItem> Items => Set<PrimaryItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<PrimaryItem>(entity =>
            {
                entity.ToTable("primary_items");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedNever();
            });
    }

    private sealed class ReferenceContext(DbContextOptions<ReferenceContext> options) : DbContext(options)
    {
        public DbSet<ReferenceItem> Items => Set<ReferenceItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ReferenceItem>(entity =>
            {
                entity.ToTable("reference_items");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedNever();
            });
    }

    private sealed class PrimaryItem
    {
        public int Id { get; set; }
    }

    private sealed class ReferenceItem
    {
        public int Id { get; set; }
        public required string Value { get; set; }
    }
}