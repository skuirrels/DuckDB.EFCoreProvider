using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class DuckLakeTimeTravelTests
{
    [ConditionalFact]
    public async Task Snapshot_profile_queries_a_catalog_wide_historical_view_with_LINQ()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);
        try
        {
            var currentOptions = CreateOptions(metadataPath, dataPath);
            long insertedSnapshot;
            await using (var context = new TimeTravelContext(currentOptions))
            {
                await context.Database.EnsureCreatedAsync();
                context.Items.Add(new TimeTravelItem { Id = 1, Value = "first" });
                await context.SaveChangesAsync();
                insertedSnapshot = (await context.Database.DuckLake().GetSnapshotsAsync()).Max(snapshot => snapshot.SnapshotId);

                context.Items.Single().Value = "current";
                await context.SaveChangesAsync();

                Assert.Equal(
                    "first",
                    await context.Items.AsOfSnapshot(insertedSnapshot).Select(item => item.Value).SingleAsync());
            }

            var historicalOptions = new DbContextOptionsBuilder<TimeTravelContext>()
                .UseDuckLake(
                    metadataPath,
                    duckLake => duckLake
                        .CatalogName("analytics")
                        .DataPath(dataPath)
                        .AsOfSnapshot(insertedSnapshot))
                .Options;
            await using var historicalContext = new TimeTravelContext(historicalOptions);

            Assert.Equal("first", await historicalContext.Items.Select(item => item.Value).SingleAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => historicalContext.Database.DuckLake().FlushInlinedDataAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [ConditionalFact]
    public async Task Timestamp_profile_queries_the_snapshot_selected_by_DuckLake()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);
        try
        {
            var currentOptions = CreateOptions(metadataPath, dataPath);
            DateTimeOffset insertedAt;
            await using (var context = new TimeTravelContext(currentOptions))
            {
                await context.Database.EnsureCreatedAsync();
                context.Items.Add(new TimeTravelItem { Id = 1, Value = "first" });
                await context.SaveChangesAsync();
                insertedAt = (await context.Database.DuckLake().GetSnapshotsAsync()).MaxBy(snapshot => snapshot.SnapshotId)!.SnapshotTime;

                context.Items.Single().Value = "current";
                await context.SaveChangesAsync();

                Assert.Equal(
                    "first",
                    await context.Items.AsOfTimestamp(insertedAt).Select(item => item.Value).SingleAsync());
            }

            var historicalOptions = new DbContextOptionsBuilder<TimeTravelContext>()
                .UseDuckLake(
                    metadataPath,
                    duckLake => duckLake
                        .CatalogName("analytics")
                        .DataPath(dataPath)
                        .AsOfTimestamp(insertedAt))
                .Options;
            await using var historicalContext = new TimeTravelContext(historicalOptions);

            Assert.Equal("first", await historicalContext.Items.Select(item => item.Value).SingleAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Historical_attach_options_are_explicit_and_read_only()
    {
        var timestamp = new DateTimeOffset(2026, 7, 20, 12, 34, 56, TimeSpan.Zero);
        var byVersion = DuckLakeAttachCommandBuilder.Build(new DuckLakeOptions
        {
            MetadataSource = "catalog.ducklake",
            IsReadOnly = true,
            CreateIfNotExists = false,
            SnapshotVersion = long.MaxValue
        });
        var byTime = DuckLakeAttachCommandBuilder.Build(new DuckLakeOptions
        {
            MetadataSource = "catalog.ducklake",
            IsReadOnly = true,
            CreateIfNotExists = false,
            SnapshotTime = timestamp
        });

        Assert.Contains("READ_ONLY", byVersion);
        Assert.Contains($"SNAPSHOT_VERSION {long.MaxValue}", byVersion);
        Assert.Contains("SNAPSHOT_TIME '2026-07-20T12:34:56.0000000+00:00'", byTime);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DbContextOptionsBuilder().UseDuckLake("catalog.ducklake", duckLake => duckLake.AsOfSnapshot(-1)));

        using var nativeContext = new TimeTravelContext(
            new DbContextOptionsBuilder<TimeTravelContext>().UseDuckDB("Data Source=:memory:").Options);
        Assert.Throws<InvalidOperationException>(() => nativeContext.Items.AsOfSnapshot(0));
    }

    private static DbContextOptions<TimeTravelContext> CreateOptions(string metadataPath, string dataPath)
        => new DbContextOptionsBuilder<TimeTravelContext>()
            .UseDuckLake(metadataPath, duckLake => duckLake.CatalogName("analytics").DataPath(dataPath))
            .Options;

    private static string CreateDirectories(out string metadataPath, out string dataPath)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake_time_travel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        metadataPath = Path.Combine(root, "metadata.ducklake");
        dataPath = Path.Combine(root, "data");
        Directory.CreateDirectory(dataPath);
        return root;
    }

    private sealed class TimeTravelContext(DbContextOptions<TimeTravelContext> options) : DbContext(options)
    {
        public DbSet<TimeTravelItem> Items => Set<TimeTravelItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<TimeTravelItem>(entity =>
            {
                entity.ToTable("items");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedNever();
            });
    }

    private sealed class TimeTravelItem
    {
        public int Id { get; set; }
        public required string Value { get; set; }
    }
}