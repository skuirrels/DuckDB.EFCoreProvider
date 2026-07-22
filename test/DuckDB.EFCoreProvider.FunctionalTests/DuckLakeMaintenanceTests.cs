using DuckDB.EFCoreProvider.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class DuckLakeMaintenanceTests
{
    [ConditionalFact]
    public async Task Exposes_snapshots_and_runs_scoped_maintenance()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);
        try
        {
            var options = CreateOptions(metadataPath, dataPath);
            await using var context = new MaintenanceContext(options);
            await context.Database.EnsureCreatedAsync();
            context.Items.Add(new MaintenanceItem { Id = 1, Value = "one" });
            await context.SaveChangesAsync();

            var lake = context.Database.DuckLake();
            var snapshots = await lake.GetSnapshotsAsync();
            Assert.NotEmpty(snapshots);
            Assert.Equal(snapshots.OrderBy(snapshot => snapshot.SnapshotId), snapshots);

            var flushed = await lake.FlushInlinedDataAsync(
                new DuckLakeFlushOptions { TableName = "items", SchemaName = "main" });
            Assert.All(flushed, result => Assert.Equal("items", result.TableName));

            var merged = await lake.MergeAdjacentFilesAsync(
                new DuckLakeMergeOptions { TableName = "items", MaximumCompactedFiles = 1 });
            Assert.All(merged, result => Assert.Equal("items", result.TableName));

            var rewritten = await lake.RewriteDataFilesAsync(
                new DuckLakeRewriteOptions { TableName = "items", DeleteThreshold = 0.5 });
            Assert.All(rewritten, result => Assert.Equal("items", result.TableName));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [ConditionalFact]
    public async Task Destructive_maintenance_defaults_to_dry_run()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);
        try
        {
            var options = CreateOptions(metadataPath, dataPath);
            await using var context = new MaintenanceContext(options);
            await context.Database.EnsureCreatedAsync();
            context.Items.Add(new MaintenanceItem { Id = 1, Value = "one" });
            await context.SaveChangesAsync();

            var lake = context.Database.DuckLake();
            var before = await lake.GetSnapshotsAsync();
            var candidates = await lake.ExpireSnapshotsAsync(DateTimeOffset.UtcNow.AddDays(1));
            var after = await lake.GetSnapshotsAsync();

            Assert.NotEmpty(candidates);
            Assert.Equal(before.Select(snapshot => snapshot.SnapshotId), after.Select(snapshot => snapshot.SnapshotId));
            Assert.Empty(await lake.CleanupOldFilesAsync(DateTimeOffset.UtcNow.AddDays(1)));
            Assert.Empty(await lake.DeleteOrphanedFilesAsync(DateTimeOffset.UtcNow.AddDays(1)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [ConditionalFact]
    public async Task Commit_message_is_attached_to_the_current_transaction_snapshot()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);
        try
        {
            var options = CreateOptions(metadataPath, dataPath);
            await using var context = new MaintenanceContext(options);
            await context.Database.EnsureCreatedAsync();

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                context.Items.Add(new MaintenanceItem { Id = 1, Value = "documented" });
                await context.SaveChangesAsync();
                await context.Database.DuckLake().SetCommitMessageAsync(
                    "provider-test",
                    "Insert maintenance item",
                    "{\"source\":\"functional-test\"}");
                await transaction.CommitAsync();
            }

            var snapshot = Assert.Single(
                (await context.Database.DuckLake().GetSnapshotsAsync())
                .Where(candidate => candidate.CommitMessage is not null));
            Assert.Equal("provider-test", snapshot.Author);
            Assert.Equal("Insert maintenance item", snapshot.CommitMessage);
            Assert.Equal("{\"source\":\"functional-test\"}", snapshot.CommitExtraInfo);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [ConditionalFact]
    public async Task Commit_message_requires_an_explicit_transaction()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);
        try
        {
            await using var context = new MaintenanceContext(CreateOptions(metadataPath, dataPath));
            await context.Database.EnsureCreatedAsync();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.Database.DuckLake().SetCommitMessageAsync("author", "message"));

            Assert.Contains("requires an active transaction", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [ConditionalFact]
    public async Task Rejects_mutation_through_read_only_profile()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);
        try
        {
            await using (var writeContext = new MaintenanceContext(CreateOptions(metadataPath, dataPath)))
            {
                await writeContext.Database.EnsureCreatedAsync();
            }

            var readOptions = new DbContextOptionsBuilder<MaintenanceContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.ReadOnly())
                .Options;
            await using var readContext = new MaintenanceContext(readOptions);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => readContext.Database.DuckLake().FlushInlinedDataAsync());
            Assert.Equal("DuckLake maintenance operations cannot run through a read-only profile.", exception.Message);

            var commitException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => readContext.Database.DuckLake().SetCommitMessageAsync("author", "message"));
            Assert.Equal(
                "DuckLake commit messages cannot be set through a read-only profile.",
                commitException.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [ConditionalFact]
    public void Rejects_native_DuckDB_context()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ducklake_maintenance_native_{Guid.NewGuid():N}.db");
        try
        {
            using var context = new MaintenanceContext(
                new DbContextOptionsBuilder<MaintenanceContext>().UseDuckDB($"Data Source={path}").Options);
            var exception = Assert.Throws<InvalidOperationException>(() => context.Database.DuckLake());
            Assert.Equal("DuckLake operations require a context configured with UseDuckLake(...).", exception.Message);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static DbContextOptions<MaintenanceContext> CreateOptions(string metadataPath, string dataPath)
        => new DbContextOptionsBuilder<MaintenanceContext>()
            .UseDuckLake(metadataPath, duckLake => duckLake.CatalogName("analytics").DataPath(dataPath))
            .Options;

    private static string CreateDirectories(out string metadataPath, out string dataPath)
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake_maintenance_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        metadataPath = Path.Combine(root, "metadata.ducklake");
        dataPath = Path.Combine(root, "data");
        Directory.CreateDirectory(dataPath);
        return root;
    }

    private sealed class MaintenanceContext(DbContextOptions<MaintenanceContext> options) : DbContext(options)
    {
        public DbSet<MaintenanceItem> Items => Set<MaintenanceItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<MaintenanceItem>(entity =>
            {
                entity.ToTable("items");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedNever();
            });
    }

    private sealed class MaintenanceItem
    {
        public int Id { get; set; }
        public required string Value { get; set; }
    }
}