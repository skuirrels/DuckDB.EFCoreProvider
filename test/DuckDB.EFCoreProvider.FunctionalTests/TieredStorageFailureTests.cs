using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

[CollectionDefinition("Tiered storage failure injection", DisableParallelization = true)]
public sealed class TieredStorageFailureCollection;

[Collection("Tiered storage failure injection")]
public sealed class TieredStorageFailureTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "duckdb-tier-failure-" + Guid.NewGuid().ToString("N"));

    public TieredStorageFailureTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        TestTierFailureInjector.Clear();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    [Theory]
    [InlineData((int)DuckDBTierFailurePoint.AfterCopy, (int)TierArchiveStage.Copy, null)]
    [InlineData((int)DuckDBTierFailurePoint.AfterPublication, (int)TierArchiveStage.Publish, null)]
    [InlineData((int)DuckDBTierFailurePoint.AfterNodeDelete, (int)TierArchiveStage.DeleteHot, "failure_record_parts")]
    public async Task Forward_archive_recovers_from_each_injected_failure(
        int failurePointValue,
        int expectedStageValue,
        string? table)
    {
        var failurePoint = (DuckDBTierFailurePoint)failurePointValue;
        var expectedStage = (TierArchiveStage)expectedStageValue;
        var archivePath = Path.Combine(_root, failurePoint.ToString());
        using var context = new FailureContext(Path.Combine(_root, failurePoint + ".duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(new FailureRecord
        {
            ExternalId = "record-1",
            EffectiveAt = new DateTime(2024, 1, 15),
            Status = "complete",
            Parts =
            [
                new FailureRecordPart
                {
                    RecordExternalKey = "record-1",
                    PartCode = "A",
                    Value = 12m,
                },
            ],
        });
        context.SaveChanges();
        TestTierFailureInjector.FailOnce(failurePoint, table);

        var exception = await Assert.ThrowsAsync<TierArchiveOperationException>(
            () => context.Database.ArchiveTierAsync<FailureRecord>(new DateTime(2024, 2, 1)));

        Assert.Equal(expectedStage, exception.Stage);
        Assert.Equal(expectedStage, exception.PartialResult.Stage);
        Assert.Equal(1, exception.PartialResult.Nodes.Single(node => node.Table == "failure_records").SelectedRows);
        Assert.Single(context.RecordHistory);
        Assert.Single(context.PartHistory);
        if (failurePoint == DuckDBTierFailurePoint.AfterCopy)
        {
            Assert.Equal(
                1,
                exception.PartialResult.Nodes.Single(node => node.Table == "failure_records").CopiedRows);
        }

        if (failurePoint == DuckDBTierFailurePoint.AfterNodeDelete)
        {
            Assert.Equal(
                1,
                exception.PartialResult.Nodes.Single(node => node.Table == "failure_record_parts").DeletedRows);
        }

        var retry = await context.Database.ArchiveTierAsync<FailureRecord>(new DateTime(2024, 2, 1));

        Assert.Single(context.RecordHistory);
        Assert.Single(context.PartHistory);
        Assert.Empty(context.Records);
        Assert.Empty(context.Parts);
        Assert.Equal(TierArchiveStage.Completed, retry.Stage);
    }

    [Fact]
    public async Task Reconciliation_recovers_after_generation_publication_failure()
    {
        var archivePath = Path.Combine(_root, "reconcile");
        using var context = new FailureContext(Path.Combine(_root, "reconcile.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(new FailureRecord
        {
            ExternalId = "record-1",
            EffectiveAt = new DateTime(2024, 1, 15),
            Status = "complete",
        });
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<FailureRecord>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO failure_records (\"Id\", \"EffectiveAt\", \"ExternalId\", \"Status\") "
            + "VALUES (101, TIMESTAMP '2024-01-15', 'record-1', 'corrected');");
        TestTierFailureInjector.FailOnce(DuckDBTierFailurePoint.AfterPublication);

        var exception = await Assert.ThrowsAsync<TierArchiveOperationException>(
            () => context.Database.ReconcileArchiveTierAsync<FailureRecord>());

        Assert.Equal(TierArchiveStage.Publish, exception.Stage);
        Assert.NotNull(exception.PartialResult.Revision);
        Assert.Equal("corrected", context.RecordHistory.Single().Status);

        var retry = await context.Database.ReconcileArchiveTierAsync<FailureRecord>();

        Assert.Equal("corrected", context.RecordHistory.Single().Status);
        Assert.Empty(context.Records);
        Assert.Equal(TierArchiveStage.Completed, retry.Stage);
    }

    [Theory]
    [InlineData((int)DuckDBTierFailurePoint.BeforeCopy, (int)TierArchiveStage.Copy, false)]
    [InlineData((int)DuckDBTierFailurePoint.AfterCopy, (int)TierArchiveStage.Copy, false)]
    [InlineData((int)DuckDBTierFailurePoint.BeforeVerify, (int)TierArchiveStage.Verify, false)]
    [InlineData((int)DuckDBTierFailurePoint.AfterVerify, (int)TierArchiveStage.Verify, false)]
    [InlineData((int)DuckDBTierFailurePoint.BeforePublication, (int)TierArchiveStage.Publish, false)]
    [InlineData((int)DuckDBTierFailurePoint.AfterPublication, (int)TierArchiveStage.Publish, true)]
    public async Task Retention_trim_is_restart_safe_at_every_publication_stage(
        int failurePointValue,
        int expectedStageValue,
        bool publishedBeforeFailure)
    {
        var failurePoint = (DuckDBTierFailurePoint)failurePointValue;
        var expectedStage = (TierArchiveStage)expectedStageValue;
        var dbPath = Path.Combine(_root, "retention-" + failurePoint + ".duckdb");
        var archivePath = Path.Combine(_root, "retention-" + failurePoint);
        TierArchiveRetentionPlan plan;
        using (var context = new FailureContext(dbPath, archivePath))
        {
            context.Database.EnsureCreated();
            context.Records.Add(new FailureRecord
            {
                ExternalId = "record-1",
                EffectiveAt = new DateTime(2024, 1, 15),
                Status = "complete",
                Parts =
                [
                    new FailureRecordPart
                    {
                        RecordExternalKey = "record-1",
                        PartCode = "A",
                        Value = 12m,
                    },
                ],
            });
            await context.SaveChangesAsync();
            await context.Database.ArchiveTierAsync<FailureRecord>(new DateTime(2024, 2, 1));
            plan = await context.Database.PlanArchiveRetentionAsync<FailureRecord>(
                new TierArchiveRetentionOptions { RetainFrom = new DateTime(2024, 2, 1) });
            TestTierFailureInjector.FailOnce(failurePoint);

            var exception = await Assert.ThrowsAsync<TierArchiveOperationException>(
                () => context.Database.PublishArchiveRetentionAsync<FailureRecord>(plan));

            Assert.Equal(expectedStage, exception.Stage);
            var inventory = await context.Database.GetArchiveGenerationInventoryAsync<FailureRecord>();
            Assert.Equal(
                publishedBeforeFailure ? plan.ExpectedOutputGenerationId : plan.InputGenerationId,
                inventory.ActiveGenerationId);
            Assert.Equal(publishedBeforeFailure ? 0 : 1, context.RecordHistory.Count());
        }

        using var restarted = new FailureContext(dbPath, archivePath);
        restarted.Database.EnsureTieredStoresCreated();
        Assert.Equal(publishedBeforeFailure ? 0 : 1, restarted.RecordHistory.Count());

        var retry = await restarted.Database.PublishArchiveRetentionAsync<FailureRecord>(plan);

        Assert.Equal(TierArchiveStage.Completed, retry.Stage);
        Assert.Equal(plan.ExpectedOutputGenerationId, retry.Revision);
        Assert.Empty(restarted.RecordHistory);
        Assert.Empty(restarted.PartHistory);
    }

    [Fact]
    public async Task Restore_rolls_back_hot_rows_when_generation_publication_fails()
    {
        var archivePath = Path.Combine(_root, "restore");
        using var context = new FailureContext(Path.Combine(_root, "restore.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(new FailureRecord
        {
            ExternalId = "record-1",
            EffectiveAt = new DateTime(2024, 1, 15),
            Status = "complete",
            Parts =
            [
                new FailureRecordPart
                {
                    RecordExternalKey = "record-1",
                    PartCode = "A",
                    Value = 12m,
                },
            ],
        });
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<FailureRecord>(new DateTime(2024, 2, 1));
        TestTierFailureInjector.FailOnce(DuckDBTierFailurePoint.AfterPublication);
        var options = new TierRestoreOptions
        {
            Scope = TierMaintenanceScope.ForRootMatchKeys(
                TierRowIdentity.For<FailureRecord>(
                    new Dictionary<string, object?> { ["ExternalId"] = "record-1" })),
        };

        var exception = await Assert.ThrowsAsync<TierArchiveOperationException>(
            () => context.Database.RestoreArchiveTierAsync<FailureRecord>(options));

        Assert.Equal(TierArchiveStage.Publish, exception.Stage);
        context.ChangeTracker.Clear();
        Assert.Empty(context.Records);
        Assert.Empty(context.Parts);
        Assert.Single(context.RecordHistory);
        Assert.Single(context.PartHistory);

        var retry = await context.Database.RestoreArchiveTierAsync<FailureRecord>(options);

        Assert.Equal(1, retry.RootsInserted);
        Assert.Single(context.Records);
        Assert.Single(context.Parts);
        Assert.Single(context.RecordHistory);
        Assert.Single(context.PartHistory);
    }

    private sealed class FailureRecord
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime EffectiveAt { get; set; }
        public string Status { get; set; } = null!;
        public List<FailureRecordPart> Parts { get; set; } = [];
    }

    private sealed class FailureRecordPart
    {
        public int Id { get; set; }
        public int RecordId { get; set; }
        public FailureRecord? Record { get; set; }
        public string RecordExternalKey { get; set; } = null!;
        public string PartCode { get; set; } = null!;
        public decimal Value { get; set; }
    }

    private sealed class FailureRecordHistory
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime EffectiveAt { get; set; }
        public string Status { get; set; } = null!;
    }

    private sealed class FailureRecordPartHistory
    {
        public int Id { get; set; }
        public int RecordId { get; set; }
        public string RecordExternalKey { get; set; } = null!;
        public string PartCode { get; set; } = null!;
        public decimal Value { get; set; }
    }

    private sealed class FailureContext(string dbPath, string archivePath) : DbContext
    {
        public DbSet<FailureRecord> Records => Set<FailureRecord>();
        public DbSet<FailureRecordPart> Parts => Set<FailureRecordPart>();
        public DbSet<FailureRecordHistory> RecordHistory => Set<FailureRecordHistory>();
        public DbSet<FailureRecordPartHistory> PartHistory => Set<FailureRecordPartHistory>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IDuckDBTierFailureInjector, TestTierFailureInjector>()
                .ReplaceService<IModelCacheKeyFactory, FailureModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FailureRecord>(builder =>
            {
                builder.ToTable("failure_records");
                builder.HasKey(record => record.Id);
                builder.HasMany(record => record.Parts)
                    .WithOne(part => part.Record)
                    .HasForeignKey(part => part.RecordId);
            });
            modelBuilder.Entity<FailureRecordPart>(builder =>
            {
                builder.ToTable("failure_record_parts");
                builder.HasKey(part => part.Id);
            });
            modelBuilder.ToTieredStore<FailureRecord>(
                    record => record.EffectiveAt,
                    archivePath,
                    TierGranularity.Month)
                .MatchBy(record => record.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                .WithReadModel<FailureRecordHistory>()
                .Including<FailureRecordPart>(record => record.Parts, parts => parts
                    .MatchBy(
                        part => new { part.RecordExternalKey, part.PartCode },
                        TierMatchKeyUniqueness.ExternallyEnforced)
                    .WithReadModel<FailureRecordPartHistory>());
        }
    }

    private sealed class FailureModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is FailureContext failureContext
                ? (context.GetType(), failureContext.ArchivePath, designTime)
                : (object)(context.GetType(), designTime);
    }

    private sealed class TestTierFailureInjector : IDuckDBTierFailureInjector
    {
        private static readonly Lock Sync = new();
        private static FailurePlan? _current;

        public static void FailOnce(DuckDBTierFailurePoint point, string? table = null)
        {
            lock (Sync)
            {
                _current = new FailurePlan(point, table);
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                _current = null;
            }
        }

        public void ThrowIfRequested(DuckDBTierFailurePoint point, string? table)
        {
            lock (Sync)
            {
                var plan = _current;
                if (plan is null
                    || plan.Point != point
                    || plan.Table is not null && !string.Equals(plan.Table, table, StringComparison.Ordinal))
                {
                    return;
                }

                _current = null;
            }

            throw new InvalidOperationException($"Injected tiered-storage failure at {point} for {table ?? "aggregate"}.");
        }

        private sealed record FailurePlan(DuckDBTierFailurePoint Point, string? Table);
    }
}
