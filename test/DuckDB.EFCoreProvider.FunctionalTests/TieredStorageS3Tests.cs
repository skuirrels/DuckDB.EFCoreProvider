using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data.Common;
using System.Text;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Object-store tiering coverage. MinIO proves the S3 API path locally; the opt-in AWS lane uses a unique
///     disposable prefix and validates the same publication and retry protocol against real S3.
/// </summary>
[Collection("Tiered storage failure injection")]
public sealed class TieredStorageS3Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "duckdb-tier-s3-" + Guid.NewGuid().ToString("N"));

    public TieredStorageS3Tests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        ObjectStoreFailureInjector.Clear();
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

    [Fact]
    public void Purge_on_a_remote_archive_is_rejected_with_guidance()
    {
        using var context = new RemotePurgeContext(
            Path.Combine(_root, "purge.duckdb"),
            "gcs://example-bucket/records");

        var exception = Assert.Throws<NotSupportedException>(
            () => context.Database.PurgeArchiveOlderThan<Record>(DateTime.UtcNow));

        Assert.Contains("lifecycle rule", exception.Message);
    }

    [MinIoFact]
    public Task Archives_and_reads_an_aggregate_on_s3()
        => ArchiveRoundTrip<S3Marker>(ObjectStoreOptions.FromMinIoEnvironment("s3")!, "s3");

    [MinIoFact]
    public Task Archives_and_reads_an_aggregate_through_the_gcs_scheme()
        => ArchiveRoundTrip<GcsMarker>(ObjectStoreOptions.FromMinIoEnvironment("gcs")!, "gcs");

    [MinIoFact]
    public async Task Tiered_view_without_a_read_model_round_trips_through_disposable_object_storage()
    {
        var objectStore = ObjectStoreOptions.FromMinIoEnvironment("s3")!;
        var archivePath = objectStore.CreateArchivePath("s3");
        var dbPath = Path.Combine(_root, "view-only.duckdb");
        using (var context = new ObjectStoreViewOnlyContext<ViewOnlyMarker>(dbPath, archivePath, objectStore))
        {
            context.Database.EnsureCreated();
            context.Records.Add(new Record
            {
                Id = 1,
                ExternalId = "view-only-1",
                EffectiveAt = new DateTime(2024, 1, 15),
                Status = "complete",
            });
            context.Records.Add(new Record
            {
                Id = 2,
                ExternalId = "view-only-2",
                EffectiveAt = new DateTime(2024, 2, 15),
                Status = "complete",
            });
            context.SaveChanges();
            Assert.Equal(
                2,
                context.Database.SqlQueryRaw<long>(
                    "SELECT count(*) AS \"Value\" FROM object_view_only_history").Single());

            await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 3, 1));

            Assert.Empty(context.Records);
            Assert.Equal(
                2,
                context.Database.SqlQueryRaw<long>(
                    "SELECT count(*) AS \"Value\" FROM object_view_only_history").Single());
        }

        using (var restarted = new ObjectStoreViewOnlyContext<ViewOnlyMarker>(dbPath, archivePath, objectStore))
        {
            restarted.Database.EnsureTieredStoresCreated();
            Assert.Equal(
                ["view-only-1", "view-only-2"],
                restarted.Database.SqlQueryRaw<string>(
                        "SELECT \"ExternalId\" AS \"Value\" FROM object_view_only_history ORDER BY \"Id\"")
                    .ToArray());
        }

        using var history = new ObjectStoreHistoryContext<ViewOnlyMarker>(dbPath, objectStore);
        var from = new DateTime(2024, 1, 1);
        var to = new DateTime(2024, 2, 1);
        var january = history.Records.Where(record => record.EffectiveAt >= from && record.EffectiveAt < to);
        var sql = january.ToQueryString();
        Assert.Contains(DuckDBTierPartitionContract.ColumnPrefix, sql);
        Assert.Contains("EffectiveAt_month", sql);
        Assert.Equal(["view-only-1"], january.Select(record => record.ExternalId).ToArray());
    }

    [MinIoFact]
    public Task Minio_archive_failure_retry_and_schema_evolution_matrix()
        => RunFailureMatrix<MinIoMatrixMarker>(ObjectStoreOptions.FromMinIoEnvironment("s3")!, "s3");

    [MinIoFact]
    public async Task Retention_trim_publishes_an_exact_remote_catalogue_without_deleting_the_input_generation()
    {
        var objectStore = ObjectStoreOptions.FromMinIoEnvironment("s3")!;
        var archivePath = objectStore.CreateArchivePath("s3");
        var dbPath = Path.Combine(_root, "retention-minio.duckdb");
        TierArchiveRetentionPlan plan;
        using (var context = new ObjectStoreContext<RetentionMarker, SchemaV1>(dbPath, archivePath, objectStore))
        {
            context.Database.EnsureCreated();
            SeedRecords(context);
            await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 8, 1));
            plan = await context.Database.PlanArchiveRetentionAsync<Record>(
                new TierArchiveRetentionOptions { RetainFrom = new DateTime(2024, 4, 1) });

            Assert.Equal(7, plan.Nodes.Single(node => node.EntityType == typeof(Record)).InputRows);
            Assert.Equal(4, plan.Nodes.Single(node => node.EntityType == typeof(Record)).RetainedRows);
            Assert.Equal(3, plan.Nodes.Single(node => node.EntityType == typeof(Record)).ExcludedRows);

            var result = await context.Database.PublishArchiveRetentionAsync<Record>(plan);

            Assert.Equal(TierArchiveOperation.RetentionTrim, result.Operation);
            Assert.Equal(11, context.RecordHistory.Count());
            Assert.Equal(11, context.PartHistory.Count());
        }

        using var restarted = new ObjectStoreContext<RetentionMarker, SchemaV1>(dbPath, archivePath, objectStore);
        restarted.Database.EnsureTieredStoresCreated();
        Assert.Equal(11, restarted.RecordHistory.Count());
        Assert.Equal(11, restarted.PartHistory.Count());
        var inventory = await restarted.Database.GetArchiveGenerationInventoryAsync<Record>();
        Assert.Equal(plan.ExpectedOutputGenerationId, inventory.ActiveGenerationId);
        Assert.Contains(
            inventory.Generations,
            generation => generation.GenerationId == plan.InputGenerationId
                          && generation.State == TierArchiveGenerationState.Published
                          && generation.FileCount > 0);
        var active = inventory.Generations.Single(generation => generation.State == TierArchiveGenerationState.Active);
        var files = restarted.Database.SqlQueryRaw<string>(
                "SELECT file_path AS \"Value\" FROM __duckdb_tier_generation_files "
                + "WHERE control_key = 'records' AND generation_id = {0} ORDER BY file_path",
                plan.ExpectedOutputGenerationId)
            .ToArray();
        Assert.Equal(active.FileCount, files.Length);
        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.Contains(plan.ExpectedOutputGenerationId, file));
    }

    [MinIoFact]
    public async Task Shared_child_bindings_are_root_scoped_on_disposable_object_storage()
    {
        var objectStore = ObjectStoreOptions.FromMinIoEnvironment("s3")!;
        var archivePath = objectStore.CreateArchivePath("s3");
        var dbPath = Path.Combine(_root, "shared-bindings.duckdb");
        using (var context = new ObjectStoreSharedContext<SharedBindingMarker>(
                   dbPath,
                   archivePath,
                   objectStore))
        {
            context.Database.EnsureCreated();
            context.RootAs.Add(new SharedRootA
            {
                Id = 1,
                ArchivedAt = new DateTime(2024, 1, 10),
                Children = [new ObjectSharedChild { Id = 101, Value = "a" }],
            });
            context.RootBs.Add(new SharedRootB
            {
                Id = 2,
                ArchivedAt = new DateTime(2024, 1, 11),
                Children = [new ObjectSharedChild { Id = 202, Value = "b" }],
            });
            context.SaveChanges();

            var rootB = await context.Database.ArchiveTierAsync<SharedRootB>(new DateTime(2024, 2, 1));
            Assert.Equal("object-root-b", rootB.Binding?.ControlKey);
            Assert.Single(context.SharedChildren);
            Assert.Equal([101, 202], context.SharedHistory.OrderBy(child => child.Id).Select(child => child.Id).ToArray());

            var rootA = await context.Database.ArchiveTierAsync<SharedRootA>(new DateTime(2024, 2, 1));
            Assert.Equal("object-root-a", rootA.Binding?.ControlKey);
            Assert.Empty(context.SharedChildren);
            Assert.Equal([101, 202], context.SharedHistory.OrderBy(child => child.Id).Select(child => child.Id).ToArray());
        }

        using var restarted = new ObjectStoreSharedContext<SharedBindingMarker>(
            dbPath,
            archivePath,
            objectStore);
        restarted.Database.EnsureTieredStoresCreated();
        Assert.Equal([101, 202], restarted.SharedHistory.OrderBy(child => child.Id).Select(child => child.Id).ToArray());
    }

    [RealAwsFact]
    public Task Real_aws_archive_failure_retry_and_schema_evolution_matrix()
        => RunFailureMatrix<RealAwsMatrixMarker>(ObjectStoreOptions.FromAwsEnvironment()!, "s3");

    [RealGcsFact]
    public Task Real_gcs_archive_failure_retry_and_schema_evolution_matrix()
        => RunFailureMatrix<RealGcsMatrixMarker>(ObjectStoreOptions.FromGcsEnvironment()!, "gcs");

    [RealAzureFact]
    public Task Real_azure_archive_failure_retry_and_schema_evolution_matrix()
        => RunFailureMatrix<RealAzureMatrixMarker>(AzureStoreOptions.FromEnvironment()!, "azure");

    private async Task ArchiveRoundTrip<TMarker>(IObjectStoreOptions objectStore, string scheme)
    {
        var dbPath = Path.Combine(_root, $"{scheme}-{typeof(TMarker).Name}.duckdb");
        var archivePath = objectStore.CreateArchivePath(scheme);
        (int Roots, int Children, decimal Total) expected;

        using (var context = new ObjectStoreContext<TMarker, SchemaV1>(dbPath, archivePath, objectStore))
        {
            context.Database.EnsureCreated();
            SeedRecords(context);
            expected = (
                context.RecordHistory.Count(),
                context.PartHistory.Count(),
                context.PartHistory.Sum(part => part.Value));

            var result = await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 8, 1));

            Assert.Equal(7, result.RowsArchived);
            Assert.All(result.Nodes, node => Assert.Equal(node.SelectedRows, node.CopiedRows));
            Assert.Equal(expected, History(context));

            var rerun = await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 8, 1));

            Assert.True(rerun.NoOp);
            Assert.Equal(expected, History(context));
        }

        using var restarted = new ObjectStoreContext<TMarker, SchemaV1>(dbPath, archivePath, objectStore);
        restarted.Database.EnsureTieredStoresCreated();
        Assert.Equal(expected, History(restarted));
    }

    private async Task RunFailureMatrix<TMarker>(IObjectStoreOptions objectStore, string scheme)
    {
        foreach (var scenario in new[]
                 {
                     new FailureScenario(DuckDBTierFailurePoint.AfterCopy, Table: null, TierArchiveStage.Copy),
                     new FailureScenario(DuckDBTierFailurePoint.AfterPublication, Table: null, TierArchiveStage.Publish),
                     new FailureScenario(
                         DuckDBTierFailurePoint.AfterNodeDelete,
                         "record_lines",
                         TierArchiveStage.DeleteHot),
                 })
        {
            var archivePath = objectStore.CreateArchivePath(scheme);
            var dbPath = Path.Combine(_root, $"{typeof(TMarker).Name}-{scenario.Point}.duckdb");
            (int Roots, int Children, decimal Total) expected;
            using (var context = new ObjectStoreContext<TMarker, SchemaV1>(dbPath, archivePath, objectStore))
            {
                context.Database.EnsureCreated();
                SeedRecords(context);
                expected = History(context);
                ObjectStoreFailureInjector.FailOnce(scenario.Point, scenario.Table);

                var failure = await Assert.ThrowsAsync<TierArchiveOperationException>(
                    () => context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 8, 1)));

                Assert.Equal(scenario.Stage, failure.Stage);
                Assert.Equal(expected, History(context));
            }

            using var restarted = new ObjectStoreContext<TMarker, SchemaV1>(dbPath, archivePath, objectStore);
            restarted.Database.EnsureTieredStoresCreated();
            Assert.Equal(expected, History(restarted));

            var retry = await restarted.Database.ArchiveTierAsync<Record>(new DateTime(2024, 8, 1));
            Assert.Equal(TierArchiveStage.Completed, retry.Stage);
            Assert.Equal(expected, History(restarted));
        }

        await VerifyRemoteReconciliation<TMarker>(objectStore, scheme);
        await VerifyRemoteSchemaEvolution<TMarker>(objectStore, scheme);
    }

    private async Task VerifyRemoteReconciliation<TMarker>(IObjectStoreOptions objectStore, string scheme)
    {
        var archivePath = objectStore.CreateArchivePath(scheme);
        var dbPath = Path.Combine(_root, $"{typeof(TMarker).Name}-reconcile.duckdb");
        using (var context = new ObjectStoreContext<TMarker, SchemaV1>(dbPath, archivePath, objectStore))
        {
            context.Database.EnsureCreated();
            context.Records.Add(new Record
            {
                Id = 1,
                ExternalId = "record-1",
                EffectiveAt = new DateTime(2024, 1, 15),
                Status = "complete",
            });
            context.SaveChanges();
            await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));
            context.Database.ExecuteSqlRaw(
                "INSERT INTO records (\"Id\", \"ExternalId\", \"EffectiveAt\", \"Status\") VALUES "
                + "(101, 'record-1', TIMESTAMP '2024-01-15', 'corrected'), "
                + "(102, 'late-unseen', TIMESTAMP '2024-01-20', 'complete');");
            ObjectStoreFailureInjector.FailOnce(DuckDBTierFailurePoint.AfterPublication);

            var failure = await Assert.ThrowsAsync<TierArchiveOperationException>(
                () => context.Database.ReconcileArchiveTierAsync<Record>());

            Assert.Equal(TierArchiveStage.Publish, failure.Stage);
            Assert.Equal(2, context.RecordHistory.Count());
            Assert.Equal(
                "corrected",
                context.RecordHistory.Single(record => record.ExternalId == "record-1").Status);
        }

        using var restarted = new ObjectStoreContext<TMarker, SchemaV1>(dbPath, archivePath, objectStore);
        restarted.Database.EnsureTieredStoresCreated();
        Assert.Equal(2, restarted.RecordHistory.Count());
        Assert.Equal(
            "corrected",
            restarted.RecordHistory.Single(record => record.ExternalId == "record-1").Status);

        var result = await restarted.Database.ReconcileArchiveTierAsync<Record>();
        Assert.NotNull(result.Revision);
        Assert.Contains("/_revisions/", result.ArchivePath);
        Assert.Equal(2, restarted.RecordHistory.Count());
        Assert.Equal(
            "corrected",
            restarted.RecordHistory.Single(record => record.ExternalId == "record-1").Status);
    }

    private async Task VerifyRemoteSchemaEvolution<TMarker>(IObjectStoreOptions objectStore, string scheme)
    {
        var archivePath = objectStore.CreateArchivePath(scheme);
        var dbPath = Path.Combine(_root, $"{typeof(TMarker).Name}-schema.duckdb");
        using (var original = new ObjectStoreContext<TMarker, SchemaV1>(dbPath, archivePath, objectStore))
        {
            original.Database.EnsureCreated();
            original.Records.Add(new Record
            {
                Id = 1,
                ExternalId = "record-1",
                EffectiveAt = new DateTime(2024, 1, 15),
                Status = "complete",
            });
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));
            original.Database.ExecuteSqlRaw("ALTER TABLE records ADD COLUMN \"Note\" TEXT;");
        }

        using var evolved = new ObjectStoreContext<TMarker, SchemaV2>(dbPath, archivePath, objectStore);
        evolved.Database.EnsureTieredStoresCreated();
        evolved.Records.Add(new Record
        {
            Id = 2,
            ExternalId = "record-2",
            EffectiveAt = new DateTime(2024, 2, 15),
            Status = "complete",
            Note = "new column",
        });
        evolved.SaveChanges();
        await evolved.Database.ArchiveTierAsync<Record>(new DateTime(2024, 3, 1));

        Assert.Null(evolved.RecordHistory.Single(record => record.ExternalId == "record-1").Note);
        Assert.Equal(
            "new column",
            evolved.RecordHistory.Single(record => record.ExternalId == "record-2").Note);
    }

    private static void SeedRecords(DbContextWithHistory context)
    {
        var baseDate = new DateTime(2025, 2, 1);
        var lineId = 1;
        for (var month = 13; month >= 0; month--)
        {
            var id = 14 - month;
            var record = new Record
            {
                Id = id,
                ExternalId = "record-" + id,
                EffectiveAt = baseDate.AddMonths(-month),
                Status = "complete",
            };
            record.Parts.Add(new RecordPart
            {
                Id = lineId++,
                RecordExternalKey = record.ExternalId,
                PartCode = "A",
                Value = (month + 1) * 10,
            });
            context.Records.Add(record);
        }

        context.SaveChanges();
    }

    private static (int Roots, int Children, decimal Total) History(DbContextWithHistory context)
        => (
            context.RecordHistory.Count(),
            context.PartHistory.Count(),
            context.PartHistory.Sum(part => part.Value));

    private sealed class Record
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime EffectiveAt { get; set; }
        public string Status { get; set; } = null!;
        public string? Note { get; set; }
        public List<RecordPart> Parts { get; set; } = [];
    }

    private sealed class RecordPart
    {
        public int Id { get; set; }
        public int RecordId { get; set; }
        public Record? Record { get; set; }
        public string RecordExternalKey { get; set; } = null!;
        public string PartCode { get; set; } = null!;
        public decimal Value { get; set; }
    }

    private sealed class RecordRm
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime EffectiveAt { get; set; }
        public string Status { get; set; } = null!;
        public string? Note { get; set; }
    }

    private sealed class RecordPartRm
    {
        public int Id { get; set; }
        public int RecordId { get; set; }
        public string RecordExternalKey { get; set; } = null!;
        public string PartCode { get; set; } = null!;
        public decimal Value { get; set; }
    }

    private sealed class S3Marker;
    private sealed class GcsMarker;
    private sealed class MinIoMatrixMarker;
    private sealed class RetentionMarker;
    private sealed class RealAwsMatrixMarker;
    private sealed class RealGcsMatrixMarker;
    private sealed class RealAzureMatrixMarker;
    private sealed class SharedBindingMarker;
    private sealed class SchemaV1;
    private sealed class SchemaV2;

    private sealed class SharedRootA
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
        public List<ObjectSharedChild> Children { get; set; } = [];
    }

    private sealed class SharedRootB
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
        public List<ObjectSharedChild> Children { get; set; } = [];
    }

    private sealed class ObjectSharedChild
    {
        public int Id { get; set; }
        public int? RootAId { get; set; }
        public SharedRootA? RootA { get; set; }
        public int? RootBId { get; set; }
        public SharedRootB? RootB { get; set; }
        public string Value { get; set; } = null!;
    }

    private sealed class SharedRootARm
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
    }

    private sealed class SharedRootBRm
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
    }

    private sealed class ObjectSharedChildRm
    {
        public int Id { get; set; }
        public int? RootAId { get; set; }
        public int? RootBId { get; set; }
        public string Value { get; set; } = null!;
    }

    private abstract class DbContextWithHistory : DbContext
    {
        public DbSet<Record> Records => Set<Record>();
        public DbSet<RecordRm> RecordHistory => Set<RecordRm>();
        public DbSet<RecordPartRm> PartHistory => Set<RecordPartRm>();
    }

    private sealed class ObjectStoreContext<TMarker, TSchema>(
        string dbPath,
        string archivePath,
        IObjectStoreOptions objectStore) : DbContextWithHistory
    {
        public string ModelKey => archivePath + "|" + typeof(TMarker).Name + "|" + typeof(TSchema).Name;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseDuckDB($"Data Source={dbPath}");
            options.AddInterceptors(new ObjectStoreSetupInterceptor(objectStore));
            options.ReplaceService<IDuckDBTierFailureInjector, ObjectStoreFailureInjector>();
            options.ReplaceService<IModelCacheKeyFactory, ObjectStoreModelCacheKeyFactory>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.HasMany(record => record.Parts)
                    .WithOne(part => part.Record)
                    .HasForeignKey(part => part.RecordId);
                if (typeof(TSchema) == typeof(SchemaV1))
                {
                    builder.Ignore(record => record.Note);
                }
            });
            modelBuilder.Entity<RecordPart>(builder =>
            {
                builder.ToTable("record_lines");
                builder.HasKey(part => part.Id);
            });
            if (typeof(TSchema) == typeof(SchemaV1))
            {
                modelBuilder.Entity<RecordRm>().Ignore(record => record.Note);
            }

            modelBuilder.ToTieredStore<Record>(
                    record => record.EffectiveAt,
                    archivePath,
                    TierGranularity.Month)
                .MatchBy(record => record.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                .WithReadModel<RecordRm>()
                .Including<RecordPart>(record => record.Parts, part => part
                    .MatchBy(
                        item => new { item.RecordExternalKey, item.PartCode },
                        TierMatchKeyUniqueness.ExternallyEnforced)
                    .WithReadModel<RecordPartRm>());
        }
    }

    private sealed class ObjectStoreSharedContext<TMarker>(
        string dbPath,
        string archivePath,
        IObjectStoreOptions objectStore) : DbContext
    {
        public DbSet<SharedRootA> RootAs => Set<SharedRootA>();
        public DbSet<SharedRootB> RootBs => Set<SharedRootB>();
        public DbSet<ObjectSharedChild> SharedChildren => Set<ObjectSharedChild>();
        public DbSet<ObjectSharedChildRm> SharedHistory => Set<ObjectSharedChildRm>();
        public string ModelKey => archivePath + "|" + typeof(TMarker).Name;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseDuckDB($"Data Source={dbPath}");
            options.AddInterceptors(new ObjectStoreSetupInterceptor(objectStore));
            options.ReplaceService<IModelCacheKeyFactory, ObjectStoreModelCacheKeyFactory>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SharedRootA>(builder =>
            {
                builder.ToTable("object_root_a");
                builder.HasKey(root => root.Id);
                builder.HasMany(root => root.Children)
                    .WithOne(child => child.RootA)
                    .HasForeignKey(child => child.RootAId);
            });
            modelBuilder.Entity<SharedRootB>(builder =>
            {
                builder.ToTable("object_root_b");
                builder.HasKey(root => root.Id);
                builder.HasMany(root => root.Children)
                    .WithOne(child => child.RootB)
                    .HasForeignKey(child => child.RootBId);
            });
            modelBuilder.Entity<ObjectSharedChild>(builder =>
            {
                builder.ToTable("object_shared_children");
                builder.HasKey(child => child.Id);
            });
            modelBuilder.ToTieredStore<SharedRootB>(
                    root => root.ArchivedAt,
                    archivePath + "/b",
                    controlKey: "object-root-b")
                .WithReadModel<SharedRootBRm>()
                .Including<ObjectSharedChild>(
                    root => root.Children,
                    child => child.WithReadModel<ObjectSharedChildRm>());
            modelBuilder.ToTieredStore<SharedRootA>(
                    root => root.ArchivedAt,
                    archivePath + "/a",
                    controlKey: "object-root-a")
                .WithReadModel<SharedRootARm>()
                .Including<ObjectSharedChild>(
                    root => root.Children,
                    child => child.WithReadModel<ObjectSharedChildRm>());
        }
    }

    private sealed class ObjectStoreViewOnlyContext<TMarker>(
        string dbPath,
        string archivePath,
        IObjectStoreOptions objectStore) : DbContext
    {
        public DbSet<Record> Records => Set<Record>();
        public string ModelKey => archivePath + "|" + typeof(TMarker).Name;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseDuckDB($"Data Source={dbPath}");
            options.AddInterceptors(new ObjectStoreSetupInterceptor(objectStore));
            options.ReplaceService<IModelCacheKeyFactory, ObjectStoreModelCacheKeyFactory>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("object_view_only_records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
                builder.Ignore(record => record.Note);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .PartitionBy(partitions => partitions.ByMonth(record => record.EffectiveAt))
                .WithTieredView("object_view_only_history");
        }
    }

    private sealed class ObjectStoreHistoryContext<TMarker>(
        string dbPath,
        IObjectStoreOptions objectStore) : DbContext
    {
        public DbSet<Record> Records => Set<Record>();
        public string ModelKey => typeof(TMarker).Name;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseDuckDB($"Data Source={dbPath}");
            options.AddInterceptors(new ObjectStoreSetupInterceptor(objectStore));
            options.ReplaceService<IModelCacheKeyFactory, ObjectStoreModelCacheKeyFactory>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTieredView(
                    "object_view_only_history",
                    partitions => partitions.ByMonth(record => record.EffectiveAt));
                builder.Ignore(record => record.Parts);
                builder.Ignore(record => record.Note);
            });
    }

    private sealed class ObjectStoreModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context.GetType().GetProperty("ModelKey")?.GetValue(context) is string modelKey
                ? (context.GetType(), modelKey, designTime)
                : (object)(context.GetType(), designTime);
    }

    private sealed class RemotePurgeContext(string dbPath, string archivePath) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath, TierGranularity.Month);
    }

    private sealed class ViewOnlyMarker;

    private sealed class ObjectStoreSetupInterceptor(IObjectStoreOptions options) : DbConnectionInterceptor
    {
        private string Sql => options.SetupSql();

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = connection.CreateCommand();
            command.CommandText = Sql;
            command.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private interface IObjectStoreOptions
    {
        string CreateArchivePath(string scheme);
        string SetupSql();
    }

    private sealed record ObjectStoreOptions(
        string SecretType,
        string Bucket,
        string Prefix,
        string Region,
        string? Endpoint,
        string? KeyId,
        string? Secret,
        string? SessionToken,
        bool UseSsl,
        bool UseCredentialChain) : IObjectStoreOptions
    {
        public string CreateArchivePath(string scheme)
            => $"{scheme}://{Bucket}/{Prefix.Trim('/')}/tier-int-{Guid.NewGuid():N}";

        public string SetupSql() => "INSTALL httpfs; LOAD httpfs; " + SecretSql();

        public string SecretSql()
        {
            var builder = new StringBuilder(
                $"CREATE OR REPLACE SECRET objectstoretest (TYPE {SecretType}");
            if (UseCredentialChain)
            {
                builder.Append(", PROVIDER credential_chain");
            }
            else
            {
                builder.Append(", KEY_ID ").Append(SqlLiteral(KeyId!))
                    .Append(", SECRET ").Append(SqlLiteral(Secret!));
                if (!string.IsNullOrEmpty(SessionToken))
                {
                    builder.Append(", SESSION_TOKEN ").Append(SqlLiteral(SessionToken));
                }
            }

            if (!string.IsNullOrEmpty(Region))
            {
                builder.Append(", REGION ").Append(SqlLiteral(Region));
            }

            if (!string.IsNullOrEmpty(Endpoint))
            {
                builder.Append(", ENDPOINT ").Append(SqlLiteral(Endpoint))
                    .Append(", URL_STYLE 'path', USE_SSL ").Append(UseSsl ? "true" : "false");
            }

            return builder.Append(");").ToString();
        }

        public static ObjectStoreOptions? FromMinIoEnvironment(string secretType)
        {
            var endpoint = Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_ENDPOINT");
            var bucket = Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_BUCKET");
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(bucket))
            {
                return null;
            }

            return new ObjectStoreOptions(
                secretType,
                bucket,
                Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_PREFIX") ?? "tiered-provider-tests",
                Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_REGION") ?? "us-east-1",
                endpoint,
                Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_KEY") ?? "minioadmin",
                Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_SECRET") ?? "minioadmin",
                SessionToken: null,
                string.Equals(
                    Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_SSL"),
                    "true",
                    StringComparison.OrdinalIgnoreCase),
                UseCredentialChain: false);
        }

        public static ObjectStoreOptions? FromAwsEnvironment()
        {
            var bucket = Environment.GetEnvironmentVariable("DUCKDB_AWS_S3_TEST_BUCKET");
            if (string.IsNullOrEmpty(bucket))
            {
                return null;
            }

            var keyId = Environment.GetEnvironmentVariable("DUCKDB_AWS_S3_TEST_KEY");
            var secret = Environment.GetEnvironmentVariable("DUCKDB_AWS_S3_TEST_SECRET");
            if (string.IsNullOrEmpty(keyId) != string.IsNullOrEmpty(secret))
            {
                throw new InvalidOperationException(
                    "DUCKDB_AWS_S3_TEST_KEY and DUCKDB_AWS_S3_TEST_SECRET must either both be set or both be omitted.");
            }

            return new ObjectStoreOptions(
                "s3",
                bucket,
                Environment.GetEnvironmentVariable("DUCKDB_AWS_S3_TEST_PREFIX")
                ?? "duckdb-efcore-provider-disposable",
                Environment.GetEnvironmentVariable("DUCKDB_AWS_S3_TEST_REGION") ?? "us-east-1",
                Endpoint: null,
                keyId,
                secret,
                Environment.GetEnvironmentVariable("DUCKDB_AWS_S3_TEST_SESSION_TOKEN"),
                UseSsl: true,
                UseCredentialChain: string.IsNullOrEmpty(keyId));
        }

        public static ObjectStoreOptions? FromGcsEnvironment()
        {
            var bucket = Environment.GetEnvironmentVariable("DUCKDB_GCS_TEST_BUCKET");
            var keyId = Environment.GetEnvironmentVariable("DUCKDB_GCS_TEST_KEY");
            var secret = Environment.GetEnvironmentVariable("DUCKDB_GCS_TEST_SECRET");
            if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(secret))
            {
                return null;
            }

            return new ObjectStoreOptions(
                "gcs",
                bucket,
                Environment.GetEnvironmentVariable("DUCKDB_GCS_TEST_PREFIX")
                ?? "duckdb-efcore-provider-disposable",
                Region: "",
                Endpoint: null,
                keyId,
                secret,
                SessionToken: null,
                UseSsl: true,
                UseCredentialChain: false);
        }

        private static string SqlLiteral(string value) => $"'{value.Replace("'", "''")}'";
    }

    private sealed record AzureStoreOptions(string ConnectionString, string Container, string Prefix)
        : IObjectStoreOptions
    {
        public string CreateArchivePath(string scheme)
            => $"{scheme}://{Container}/{Prefix.Trim('/')}/tier-int-{Guid.NewGuid():N}";

        public string SetupSql()
            => "INSTALL azure; LOAD azure; CREATE OR REPLACE SECRET objectstoretest "
               + $"(TYPE azure, CONNECTION_STRING {SqlLiteral(ConnectionString)});";

        public static AzureStoreOptions? FromEnvironment()
        {
            var connectionString = Environment.GetEnvironmentVariable("DUCKDB_AZURE_TEST_CONNECTION_STRING");
            var container = Environment.GetEnvironmentVariable("DUCKDB_AZURE_TEST_CONTAINER");
            return string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(container)
                ? null
                : new AzureStoreOptions(
                    connectionString,
                    container,
                    Environment.GetEnvironmentVariable("DUCKDB_AZURE_TEST_PREFIX")
                    ?? "duckdb-efcore-provider-disposable");
        }

        private static string SqlLiteral(string value) => $"'{value.Replace("'", "''")}'";
    }

    private sealed class ObjectStoreFailureInjector : IDuckDBTierFailureInjector
    {
        private static readonly Lock Sync = new();
        private static FailureScenario? _current;

        public static void FailOnce(DuckDBTierFailurePoint point, string? table = null)
        {
            lock (Sync)
            {
                _current = new FailureScenario(point, table, TierArchiveStage.Preflight);
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
                if (_current is not { } plan
                    || plan.Point != point
                    || plan.Table is not null && !string.Equals(plan.Table, table, StringComparison.Ordinal))
                {
                    return;
                }

                _current = null;
            }

            throw new InvalidOperationException($"Injected object-store tier failure at {point}.");
        }
    }

    private sealed record FailureScenario(
        DuckDBTierFailurePoint Point,
        string? Table,
        TierArchiveStage Stage);
}

/// <summary>A <see cref="FactAttribute" /> that skips unless a live S3-compatible endpoint is configured.</summary>
public sealed class MinIoFactAttribute : FactAttribute
{
    public MinIoFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_ENDPOINT"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUCKDB_S3_TEST_BUCKET")))
        {
            Skip = "Set DUCKDB_S3_TEST_ENDPOINT and DUCKDB_S3_TEST_BUCKET (plus optional _KEY/_SECRET/_SSL) "
                   + "to run MinIO S3/GCS interoperability tests.";
        }
    }
}

/// <summary>A <see cref="FactAttribute" /> that skips unless a disposable real-AWS S3 bucket is configured.</summary>
public sealed class RealAwsFactAttribute : FactAttribute
{
    public RealAwsFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUCKDB_AWS_S3_TEST_BUCKET")))
        {
            Skip = "Set DUCKDB_AWS_S3_TEST_BUCKET and optionally _PREFIX/_REGION/_KEY/_SECRET/_SESSION_TOKEN "
                   + "to run the disposable real-AWS S3 failure matrix.";
        }
    }
}

/// <summary>A <see cref="FactAttribute" /> that skips unless a disposable real-GCS bucket is configured.</summary>
public sealed class RealGcsFactAttribute : FactAttribute
{
    public RealGcsFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUCKDB_GCS_TEST_BUCKET"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUCKDB_GCS_TEST_KEY"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUCKDB_GCS_TEST_SECRET")))
        {
            Skip = "Set DUCKDB_GCS_TEST_BUCKET, DUCKDB_GCS_TEST_KEY, and DUCKDB_GCS_TEST_SECRET "
                   + "(plus optional _PREFIX) to run the disposable real-GCS failure matrix.";
        }
    }
}

/// <summary>A <see cref="FactAttribute" /> that skips unless a disposable Azure Blob container is configured.</summary>
public sealed class RealAzureFactAttribute : FactAttribute
{
    public RealAzureFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUCKDB_AZURE_TEST_CONNECTION_STRING"))
            || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUCKDB_AZURE_TEST_CONTAINER")))
        {
            Skip = "Set DUCKDB_AZURE_TEST_CONNECTION_STRING and DUCKDB_AZURE_TEST_CONTAINER "
                   + "(plus optional _PREFIX) to run the disposable real-Azure Blob failure matrix.";
        }
    }
}
