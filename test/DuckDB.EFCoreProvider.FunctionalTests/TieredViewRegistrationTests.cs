using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Xunit;
using static Microsoft.EntityFrameworkCore.TieredStorageTestHelpers;

namespace Microsoft.EntityFrameworkCore;

public sealed class TieredViewRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "duckdb-tier-view-" + Guid.NewGuid().ToString("N"));

    public TieredViewRegistrationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
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
    public async Task Root_and_nested_descendant_views_work_without_duplicate_clr_models()
    {
        var dbPath = Path.Combine(_root, "aggregate.duckdb");
        var archivePath = Path.Combine(_root, "aggregate-archive");
        using (var owner = new AggregateOwnerContext(dbPath, archivePath))
        {
            owner.Database.EnsureCreated();

            var order = owner.Model.FindEntityType(typeof(TieredOrder))!;
            var line = owner.Model.FindEntityType(typeof(TieredLine))!;
            var allocation = owner.Model.FindEntityType(typeof(TieredAllocation))!;
            Assert.Equal("order_history", order.GetTieredStoreView());
            Assert.Equal("tiered_lines_tiered", line.GetTieredStoreView());
            Assert.Equal("allocation_history", allocation.GetTieredStoreView());
            Assert.Equal(3, owner.Model.GetEntityTypes().Count());
            Assert.All(owner.Model.GetEntityTypes(), entity => Assert.NotNull(entity.GetTieredStoreRole()));

            owner.Orders.AddRange(
                CreateOrder(1, "order-cold", new DateTime(2024, 1, 15), "cold", 11m),
                CreateOrder(2, "order-hot", new DateTime(2024, 3, 15), "hot", 22m));
            owner.SaveChanges();

            Assert.Equal(
                2,
                owner.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM order_history").Single());
            Assert.Equal(
                2,
                owner.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM tiered_lines_tiered").Single());
            Assert.Equal(
                2,
                owner.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM allocation_history").Single());

            await owner.Database.ArchiveTierAsync<TieredOrder>(new DateTime(2024, 2, 1));

            Assert.Single(owner.Orders);
            Assert.Single(owner.Lines);
            Assert.Single(owner.Allocations);
            Assert.Equal(
                2,
                owner.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM order_history").Single());
            Assert.Equal(
                2,
                owner.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM tiered_lines_tiered").Single());
            Assert.Equal(
                2,
                owner.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM allocation_history").Single());
        }

        using (var history = new AggregateHistoryContext(dbPath))
        {
            var coldOrders = history.Orders.Where(order => order.CompletedAt < new DateTime(2024, 2, 1));
            var pruningSql = coldOrders.ToQueryString();
            Assert.Contains("CompletedAt_month", pruningSql);
            Assert.Contains("date_trunc('month'", pruningSql);
            Assert.Equal([1], coldOrders.Select(order => order.Id).ToArray());

            var orders = history.Orders.OrderBy(order => order.Id).ToArray();
            Assert.Equal([1, 2], orders.Select(order => order.Id).ToArray());
            Assert.Equal(["order-cold", "order-hot"], orders.Select(order => order.ExternalId).ToArray());
            Assert.Equal(
                [new DateTime(2024, 1, 15), new DateTime(2024, 3, 15)],
                orders.Select(order => order.CompletedAt).ToArray());
            Assert.Equal(["cold", "hot"], orders.Select(order => order.Description).ToArray());

            var lines = history.Lines.OrderBy(line => line.Id).ToArray();
            Assert.Equal([10, 20], lines.Select(line => line.Id).ToArray());
            Assert.Equal([1, 2], lines.Select(line => line.OrderId).ToArray());
            Assert.Equal(["line-order-cold", "line-order-hot"], lines.Select(line => line.ExternalId).ToArray());
            Assert.Equal([11m, 22m], lines.Select(line => line.Amount).ToArray());

            var allocations = history.Allocations.OrderBy(allocation => allocation.Id).ToArray();
            Assert.Equal([100, 200], allocations.Select(allocation => allocation.Id).ToArray());
            Assert.Equal([10, 20], allocations.Select(allocation => allocation.LineId).ToArray());
            Assert.Equal(
                ["allocation-order-cold", "allocation-order-hot"],
                allocations.Select(allocation => allocation.Code).ToArray());
        }

        using var maintenance = new AggregateOwnerContext(dbPath, archivePath);
        maintenance.Database.EnsureTieredStoresCreated();
        Assert.True(maintenance.Database.PurgeArchiveOlderThan<TieredOrder>(new DateTime(2024, 2, 1)) > 0);
        Assert.Equal(
            1,
            maintenance.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM order_history").Single());
        Assert.Equal(
            1,
            maintenance.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM tiered_lines_tiered").Single());
        Assert.Equal(
            1,
            maintenance.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM allocation_history").Single());
    }

    [Fact]
    public async Task Separate_read_context_prunes_the_same_parquet_files_as_the_tier_owner()
    {
        var dbPath = Path.Combine(_root, "pruning.duckdb");
        var archivePath = Path.Combine(_root, "pruning-archive");
        var from = new DateTime(2024, 2, 1);
        var to = new DateTime(2024, 3, 1);

        using (var owner = new PruningOwnerContext<PruningEvidenceMarker>(dbPath, archivePath))
        {
            owner.Database.EnsureCreated();
            owner.Records.AddRange(
                new PruningRecord { Id = 1, CustomerId = 10, CompletedAt = new DateTime(2024, 1, 10) },
                new PruningRecord { Id = 2, CustomerId = 10, CompletedAt = new DateTime(2024, 2, 10) },
                new PruningRecord { Id = 3, CustomerId = 20, CompletedAt = new DateTime(2024, 1, 10) },
                new PruningRecord { Id = 4, CustomerId = 20, CompletedAt = new DateTime(2024, 2, 10) });
            owner.SaveChanges();
            await owner.Database.ArchiveTierAsync<PruningRecord>(to);

            var ownerQuery = owner.History.Where(record =>
                record.CustomerId == 10 && record.CompletedAt >= from && record.CompletedAt < to);
            AssertFilesPruned(Explain(owner, ownerQuery), "1/4");
            Assert.Equal([2], ownerQuery.Select(record => record.Id).ToArray());
        }

        using var history = new PruningHistoryContext(dbPath);
        var historyQuery = history.Records.Where(record =>
            record.CustomerId == 10 && record.CompletedAt >= from && record.CompletedAt < to);
        var historySql = historyQuery.ToQueryString();
        Assert.Contains(DuckDBTierPartitionContract.ColumnPrefix, historySql);
        Assert.Contains("CompletedAt_month", historySql);
        AssertFilesPruned(Explain(history, historyQuery), "1/4");
        Assert.Equal([2], historyQuery.Select(record => record.Id).ToArray());
    }

    [Fact]
    public async Task Separate_read_context_rejects_a_partition_contract_that_drifted_from_the_owner()
    {
        var dbPath = Path.Combine(_root, "drift.duckdb");
        var archivePath = Path.Combine(_root, "drift-archive");
        using (var owner = new PruningOwnerContext<PruningDriftMarker>(dbPath, archivePath))
        {
            owner.Database.EnsureCreated();
            owner.Records.Add(new PruningRecord
            {
                Id = 1,
                CustomerId = 10,
                CompletedAt = new DateTime(2024, 1, 20),
            });
            owner.SaveChanges();
            await owner.Database.ArchiveTierAsync<PruningRecord>(new DateTime(2024, 2, 1));
        }

        using var history = new DriftedPruningHistoryContext(dbPath);
        var query = history.Records.Where(record => record.CompletedAt >= new DateTime(2024, 1, 15));
        var sql = query.ToQueryString();
        Assert.Contains(DuckDBTierPartitionContract.ColumnPrefix, sql);

        var exception = Assert.Throws<DuckDBException>(() => query.ToArray());
        Assert.Contains(DuckDBTierPartitionContract.ColumnPrefix, exception.Message);
    }

    [Fact]
    public async Task View_only_registration_refreshes_after_reconciliation_compaction_and_restoration()
    {
        var dbPath = Path.Combine(_root, "maintenance.duckdb");
        var archivePath = Path.Combine(_root, "maintenance-archive");
        using var context = new MaintenanceContext(dbPath, archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(new MaintenanceRecord
        {
            Id = 1,
            ExternalId = "record-1",
            CompletedAt = new DateTime(2024, 1, 15),
            Payload = "original",
        });
        context.SaveChanges();

        await context.Database.ArchiveTierAsync<MaintenanceRecord>(new DateTime(2024, 2, 1));
        context.Records.Add(new MaintenanceRecord
        {
            Id = 2,
            ExternalId = "record-1",
            CompletedAt = new DateTime(2024, 1, 15),
            Payload = "corrected",
        });
        context.SaveChanges();

        var reconciliation = await context.Database.ReconcileArchiveTierAsync<MaintenanceRecord>();
        Assert.Equal(TierArchiveOperation.Reconcile, reconciliation.Operation);
        Assert.Equal("corrected", ReadMaintenancePayload(context));

        var compaction = await context.Database.CompactArchiveTierAsync<MaintenanceRecord>();
        Assert.Equal(TierArchiveOperation.Compact, compaction.Operation);
        Assert.Equal("corrected", ReadMaintenancePayload(context));

        var restoration = await context.Database.RestoreArchiveTierAsync<MaintenanceRecord>(
            new TierRestoreOptions
            {
                Scope = TierMaintenanceScope.ForRootMatchKeys(
                    TierRowIdentity.For<MaintenanceRecord>(
                        new Dictionary<string, object?>
                        {
                            [nameof(MaintenanceRecord.ExternalId)] = "record-1",
                        })),
            });

        Assert.Equal(TierArchiveOperation.Restore, restoration.Publication.Operation);
        Assert.Single(context.Records);
        Assert.Equal("corrected", ReadMaintenancePayload(context));
    }

    [Fact]
    public async Task View_only_registration_refreshes_after_archive_contract_rewrite()
    {
        var dbPath = Path.Combine(_root, "contract.duckdb");
        var archivePath = Path.Combine(_root, "contract-archive");
        using (var original = new ContractV1Context(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            original.Records.Add(new ContractRecordV1
            {
                Id = 1,
                CompletedAt = new DateTime(2024, 1, 15),
            });
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<ContractRecordV1>(new DateTime(2024, 2, 1));
        }

        using var evolved = new ContractV2Context(dbPath, archivePath);
        evolved.Database.ExecuteSqlRaw("ALTER TABLE contract_records ADD COLUMN \"Note\" TEXT;");
        var plan = await evolved.Database.PlanArchiveContractRewriteAsync<ContractRecordV2>(
            new TierArchiveRewriteOptions());

        var rewrite = await evolved.Database.RewriteArchiveContractAsync<ContractRecordV2>(plan);

        Assert.Equal(TierArchiveOperation.RewriteContract, rewrite.Operation);
        Assert.NotNull(rewrite.Revision);
        Assert.Equal(
            1,
            evolved.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM contract_history").Single());
        Assert.Null(
            evolved.Database.SqlQueryRaw<string?>("SELECT \"Note\" AS \"Value\" FROM contract_history").Single());
    }

    [Fact]
    public async Task Shared_descendant_has_one_view_across_multiple_root_bindings()
    {
        var dbPath = Path.Combine(_root, "shared.duckdb");
        var archivePath = Path.Combine(_root, "shared-archive");
        using var context = new SharedOwnerContext(dbPath, archivePath);
        context.Database.EnsureCreated();
        context.RootAs.Add(new SharedRootA
        {
            Id = 1,
            ArchivedAt = new DateTime(2024, 1, 10),
            Children = [new SharedDescendant { Id = 101, Value = "a" }],
        });
        context.RootBs.Add(new SharedRootB
        {
            Id = 2,
            ArchivedAt = new DateTime(2024, 1, 11),
            Children = [new SharedDescendant { Id = 202, Value = "b" }],
        });
        context.SaveChanges();

        var child = context.Model.FindEntityType(typeof(SharedDescendant))!;
        Assert.Equal("shared_descendant_history", child.GetTieredStoreView());
        Assert.Equal(2, child.GetTieredStoreBindings().Count);
        Assert.Equal(
            2,
            context.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM shared_descendant_history").Single());

        await context.Database.ArchiveTierAsync<SharedRootB>(new DateTime(2024, 2, 1));
        Assert.Equal(
            2,
            context.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM shared_descendant_history").Single());

        await context.Database.ArchiveTierAsync<SharedRootA>(new DateTime(2024, 2, 1));
        Assert.Equal(
            ["a", "b"],
            context.Database.SqlQueryRaw<string>(
                    "SELECT \"Value\" AS \"Value\" FROM shared_descendant_history ORDER BY \"Id\"")
                .ToArray());
    }

    [Fact]
    public void Existing_read_models_remain_compatible_and_can_share_an_explicit_view_registration()
    {
        using var existing = new DefaultReadModelContext(
            Path.Combine(_root, "existing.duckdb"),
            Path.Combine(_root, "existing-archive"));
        var existingHot = existing.Model.FindEntityType(typeof(CompatibilityRecord))!;
        var existingRead = existing.Model.FindEntityType(typeof(CompatibilityRecordReadModel))!;
        Assert.Equal("compatibility_records_tiered", existingHot.GetTieredStoreView());
        Assert.Equal("compatibility_records_tiered", existingRead.GetViewName());
        Assert.Null(existingRead.FindPrimaryKey());

        using var custom = new CustomReadModelContext(
            Path.Combine(_root, "custom.duckdb"),
            Path.Combine(_root, "custom-archive"));
        var customHot = custom.Model.FindEntityType(typeof(CompatibilityRecord))!;
        var customRead = custom.Model.FindEntityType(typeof(CompatibilityRecordReadModel))!;
        Assert.Equal("custom_compatibility_history", customHot.GetTieredStoreView());
        Assert.Equal("custom_compatibility_history", customRead.GetViewName());
    }

    [Fact]
    public void Conflicting_or_colliding_view_names_are_rejected()
    {
        using var conflictingShared = new ConflictingSharedViewContext(
            Path.Combine(_root, "conflicting-shared.duckdb"),
            Path.Combine(_root, "conflicting-shared-archive"));
        var sharedException = Assert.Throws<InvalidOperationException>(() => _ = conflictingShared.Model);
        Assert.Contains("one entity-wide view", sharedException.Message);

        using var duplicate = new DuplicateViewContext(
            Path.Combine(_root, "duplicate.duckdb"),
            Path.Combine(_root, "duplicate-archive"));
        var duplicateException = Assert.Throws<InvalidOperationException>(() => _ = duplicate.Model);
        Assert.Contains("same physical view", duplicateException.Message);

        using var tableCollision = new TableCollisionContext(
            Path.Combine(_root, "table-collision.duckdb"),
            Path.Combine(_root, "table-collision-archive"));
        var tableException = Assert.Throws<InvalidOperationException>(() => _ = tableCollision.Model);
        Assert.Contains("already used by a mapped table", tableException.Message);

        using var qualified = new QualifiedViewNameContext(
            Path.Combine(_root, "qualified.duckdb"),
            Path.Combine(_root, "qualified-archive"));
        var qualifiedException = Assert.Throws<ArgumentException>(() => _ = qualified.Model);
        Assert.Contains("unqualified", qualifiedException.Message);

        using var invalidPruning = new InvalidReadPruningContext(Path.Combine(_root, "invalid-pruning.duckdb"));
        var invalidPruningException = Assert.Throws<InvalidOperationException>(() => _ = invalidPruning.Model);
        Assert.Contains("is not DateTime or DateOnly", invalidPruningException.Message);
    }

    private static TieredOrder CreateOrder(int id, string externalId, DateTime completedAt, string description, decimal amount)
        => new()
        {
            Id = id,
            ExternalId = externalId,
            CompletedAt = completedAt,
            Description = description,
            Lines =
            [
                new TieredLine
                {
                    Id = id * 10,
                    ExternalId = "line-" + externalId,
                    Amount = amount,
                    Allocations =
                    [
                        new TieredAllocation
                        {
                            Id = id * 100,
                            Code = "allocation-" + externalId,
                        },
                    ],
                },
            ],
        };

    private static string ReadMaintenancePayload(MaintenanceContext context)
        => context.Database.SqlQueryRaw<string>(
                "SELECT \"Payload\" AS \"Value\" FROM maintenance_history WHERE \"ExternalId\" = 'record-1'")
            .Single();

    private abstract class FileContext(string dbPath) : DbContext
    {
        protected string DbPath { get; } = dbPath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={DbPath}");
    }

    private sealed class AggregateOwnerContext(string dbPath, string archivePath) : FileContext(dbPath)
    {
        public DbSet<TieredOrder> Orders => Set<TieredOrder>();
        public DbSet<TieredLine> Lines => Set<TieredLine>();
        public DbSet<TieredAllocation> Allocations => Set<TieredAllocation>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TieredOrder>(builder =>
            {
                builder.ToTable("tiered_orders");
                builder.HasKey(order => order.Id);
                builder.HasMany(order => order.Lines).WithOne(line => line.Order).HasForeignKey(line => line.OrderId);
            });
            modelBuilder.Entity<TieredLine>(builder =>
            {
                builder.ToTable("tiered_lines");
                builder.HasKey(line => line.Id);
                builder.HasMany(line => line.Allocations).WithOne(allocation => allocation.Line)
                    .HasForeignKey(allocation => allocation.LineId);
            });
            modelBuilder.Entity<TieredAllocation>(builder =>
            {
                builder.ToTable("tiered_allocations");
                builder.HasKey(allocation => allocation.Id);
            });
            modelBuilder.ToTieredStore<TieredOrder>(order => order.CompletedAt, archivePath)
                .PartitionBy(ConfigureOrderPartitions)
                .WithTieredView("order_history")
                .Including<TieredLine>(order => order.Lines, line => line
                    .WithTieredView()
                    .Including<TieredAllocation>(item => item.Allocations, allocation => allocation
                        .WithTieredView("allocation_history")));
        }
    }

    private sealed class AggregateHistoryContext(string dbPath) : FileContext(dbPath)
    {
        public DbSet<TieredOrder> Orders => Set<TieredOrder>();
        public DbSet<TieredLine> Lines => Set<TieredLine>();
        public DbSet<TieredAllocation> Allocations => Set<TieredAllocation>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TieredOrder>(builder =>
            {
                builder.ToTieredView("order_history", ConfigureOrderPartitions);
                builder.Ignore(order => order.Lines);
            });
            modelBuilder.Entity<TieredLine>(builder =>
            {
                builder.HasNoKey();
                builder.ToView("tiered_lines_tiered");
                builder.Ignore(line => line.Order);
                builder.Ignore(line => line.Allocations);
            });
            modelBuilder.Entity<TieredAllocation>(builder =>
            {
                builder.HasNoKey();
                builder.ToView("allocation_history");
                builder.Ignore(allocation => allocation.Line);
            });
        }
    }

    private static void ConfigureOrderPartitions(TieredPartitionBuilder<TieredOrder> partitions)
        => partitions.ByMonth(order => order.CompletedAt);

    private sealed class PruningOwnerContext<TMarker>(string dbPath, string archivePath) : FileContext(dbPath)
    {
        public DbSet<PruningRecord> Records => Set<PruningRecord>();
        public DbSet<PruningRecordReadModel> History => Set<PruningRecordReadModel>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PruningRecord>(builder =>
            {
                builder.ToTable("pruning_records");
                builder.HasKey(record => record.Id);
            });
            modelBuilder.ToTieredStore<PruningRecord>(record => record.CompletedAt, archivePath)
                .PartitionBy(ConfigurePruningPartitions)
                .WithReadModel<PruningRecordReadModel>();
        }
    }

    private sealed class PruningHistoryContext(string dbPath) : FileContext(dbPath)
    {
        public DbSet<PruningRecord> Records => Set<PruningRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<PruningRecord>(builder => builder.ToTieredView(
                "pruning_records_tiered",
                ConfigurePruningPartitions));
    }

    private sealed class DriftedPruningHistoryContext(string dbPath) : FileContext(dbPath)
    {
        public DbSet<PruningRecord> Records => Set<PruningRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<PruningRecord>(builder => builder.ToTieredView(
                "pruning_records_tiered",
                partitions => partitions
                    .By(record => record.CustomerId)
                    .ByDay(record => record.CompletedAt)));
    }

    private static void ConfigurePruningPartitions(TieredPartitionBuilder<PruningRecord> partitions)
        => partitions
            .By(record => record.CustomerId)
            .ByMonth(record => record.CompletedAt);

    private sealed class MaintenanceContext(string dbPath, string archivePath) : FileContext(dbPath)
    {
        public DbSet<MaintenanceRecord> Records => Set<MaintenanceRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MaintenanceRecord>(builder =>
            {
                builder.ToTable("maintenance_records");
                builder.HasKey(record => record.Id);
            });
            modelBuilder.ToTieredStore<MaintenanceRecord>(record => record.CompletedAt, archivePath)
                .MatchBy(record => record.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                .WithTieredView("maintenance_history");
        }
    }

    private sealed class ContractV1Context(string dbPath, string archivePath) : FileContext(dbPath)
    {
        public DbSet<ContractRecordV1> Records => Set<ContractRecordV1>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ContractRecordV1>(builder =>
            {
                builder.ToTable("contract_records");
                builder.HasKey(record => record.Id);
            });
            modelBuilder.ToTieredStore<ContractRecordV1>(record => record.CompletedAt, archivePath)
                .WithTieredView("contract_history");
        }
    }

    private sealed class ContractV2Context(string dbPath, string archivePath) : FileContext(dbPath)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ContractRecordV2>(builder =>
            {
                builder.ToTable("contract_records");
                builder.HasKey(record => record.Id);
            });
            modelBuilder.ToTieredStore<ContractRecordV2>(record => record.CompletedAt, archivePath)
                .WithTieredView("contract_history");
        }
    }

    private sealed class SharedOwnerContext(string dbPath, string archivePath) : FileContext(dbPath)
    {
        public DbSet<SharedRootA> RootAs => Set<SharedRootA>();
        public DbSet<SharedRootB> RootBs => Set<SharedRootB>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureSharedTables(modelBuilder);
            modelBuilder.ToTieredStore<SharedRootB>(
                    root => root.ArchivedAt,
                    Path.Combine(archivePath, "b"),
                    controlKey: "view-root-b")
                .WithTieredView()
                .Including<SharedDescendant>(root => root.Children, child => child
                    .WithTieredView("shared_descendant_history"));
            modelBuilder.ToTieredStore<SharedRootA>(
                    root => root.ArchivedAt,
                    Path.Combine(archivePath, "a"),
                    controlKey: "view-root-a")
                .WithTieredView()
                .Including<SharedDescendant>(root => root.Children, child => child
                    .WithTieredView("shared_descendant_history"));
        }
    }

    private abstract class ExistingReadModelContext(
        string dbPath,
        string archivePath,
        string? customView) : FileContext(dbPath)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CompatibilityRecord>(builder =>
            {
                builder.ToTable("compatibility_records");
                builder.HasKey(record => record.Id);
            });
            var tiered = modelBuilder.ToTieredStore<CompatibilityRecord>(record => record.CompletedAt, archivePath);
            if (customView is not null)
            {
                tiered.WithTieredView(customView);
            }

            tiered.WithReadModel<CompatibilityRecordReadModel>();
        }
    }

    private sealed class DefaultReadModelContext(string dbPath, string archivePath)
        : ExistingReadModelContext(dbPath, archivePath, customView: null);

    private sealed class CustomReadModelContext(string dbPath, string archivePath)
        : ExistingReadModelContext(dbPath, archivePath, "custom_compatibility_history");

    private sealed class ConflictingSharedViewContext(
        string dbPath,
        string archivePath) : FileContext(dbPath)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureSharedTables(modelBuilder);
            modelBuilder.ToTieredStore<SharedRootA>(
                    root => root.ArchivedAt,
                    Path.Combine(archivePath, "a"),
                    controlKey: "conflict-a")
                .Including<SharedDescendant>(root => root.Children, child => child.WithTieredView("history_a"));
            modelBuilder.ToTieredStore<SharedRootB>(
                    root => root.ArchivedAt,
                    Path.Combine(archivePath, "b"),
                    controlKey: "conflict-b")
                .Including<SharedDescendant>(root => root.Children, child => child.WithTieredView("history_b"));
        }
    }

    private sealed class DuplicateViewContext(string dbPath, string archivePath) : FileContext(dbPath)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DuplicateRootA>().ToTable("duplicate_root_a").HasKey(root => root.Id);
            modelBuilder.Entity<DuplicateRootB>().ToTable("duplicate_root_b").HasKey(root => root.Id);
            modelBuilder.ToTieredStore<DuplicateRootA>(
                    root => root.CompletedAt,
                    Path.Combine(archivePath, "a"),
                    controlKey: "duplicate-a")
                .WithTieredView("duplicate_history");
            modelBuilder.ToTieredStore<DuplicateRootB>(
                    root => root.CompletedAt,
                    Path.Combine(archivePath, "b"),
                    controlKey: "duplicate-b")
                .WithTieredView("duplicate_history");
        }
    }

    private sealed class TableCollisionContext(string dbPath, string archivePath) : FileContext(dbPath)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DuplicateRootA>().ToTable("collision").HasKey(root => root.Id);
            modelBuilder.ToTieredStore<DuplicateRootA>(root => root.CompletedAt, archivePath)
                .WithTieredView("collision");
        }
    }

    private sealed class QualifiedViewNameContext(string dbPath, string archivePath) : FileContext(dbPath)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DuplicateRootA>().ToTable("qualified").HasKey(root => root.Id);
            modelBuilder.ToTieredStore<DuplicateRootA>(root => root.CompletedAt, archivePath)
                .WithTieredView("analytics.qualified_history");
        }
    }

    private sealed class InvalidReadPruningContext(string dbPath) : FileContext(dbPath)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<MaintenanceRecord>(builder => builder.ToTieredView(
                "invalid_pruning_history",
                partitions => partitions.ByMonth(record => record.Payload)));
    }

    private static void ConfigureSharedTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SharedRootA>(builder =>
        {
            builder.ToTable("view_root_a");
            builder.HasKey(root => root.Id);
            builder.HasMany(root => root.Children).WithOne(child => child.RootA).HasForeignKey(child => child.RootAId);
        });
        modelBuilder.Entity<SharedRootB>(builder =>
        {
            builder.ToTable("view_root_b");
            builder.HasKey(root => root.Id);
            builder.HasMany(root => root.Children).WithOne(child => child.RootB).HasForeignKey(child => child.RootBId);
        });
        modelBuilder.Entity<SharedDescendant>(builder =>
        {
            builder.ToTable("shared_descendants");
            builder.HasKey(child => child.Id);
        });
    }

    private sealed class TieredOrder
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime CompletedAt { get; set; }
        public string Description { get; set; } = null!;
        public List<TieredLine> Lines { get; set; } = [];
    }

    private sealed class TieredLine
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public TieredOrder? Order { get; set; }
        public string ExternalId { get; set; } = null!;
        public decimal Amount { get; set; }
        public List<TieredAllocation> Allocations { get; set; } = [];
    }

    private sealed class TieredAllocation
    {
        public int Id { get; set; }
        public int LineId { get; set; }
        public TieredLine? Line { get; set; }
        public string Code { get; set; } = null!;
    }

    private sealed class MaintenanceRecord
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime CompletedAt { get; set; }
        public string Payload { get; set; } = null!;
    }

    private sealed class PruningRecord
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public DateTime CompletedAt { get; set; }
    }

    private sealed class PruningRecordReadModel
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public DateTime CompletedAt { get; set; }
    }

    private sealed class PruningEvidenceMarker;

    private sealed class PruningDriftMarker;

    private sealed class ContractRecordV1
    {
        public int Id { get; set; }
        public DateTime CompletedAt { get; set; }
    }

    private sealed class ContractRecordV2
    {
        public int Id { get; set; }
        public DateTime CompletedAt { get; set; }
        public string? Note { get; set; }
    }

    private sealed class SharedRootA
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
        public List<SharedDescendant> Children { get; set; } = [];
    }

    private sealed class SharedRootB
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
        public List<SharedDescendant> Children { get; set; } = [];
    }

    private sealed class SharedDescendant
    {
        public int Id { get; set; }
        public int? RootAId { get; set; }
        public SharedRootA? RootA { get; set; }
        public int? RootBId { get; set; }
        public SharedRootB? RootB { get; set; }
        public string Value { get; set; } = null!;
    }

    private sealed class CompatibilityRecord
    {
        public int Id { get; set; }
        public DateTime CompletedAt { get; set; }
    }

    private sealed class CompatibilityRecordReadModel
    {
        public int Id { get; set; }
        public DateTime CompletedAt { get; set; }
    }

    private sealed class DuplicateRootA
    {
        public int Id { get; set; }
        public DateTime CompletedAt { get; set; }
    }

    private sealed class DuplicateRootB
    {
        public int Id { get; set; }
        public DateTime CompletedAt { get; set; }
    }
}