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
    [InlineData((int)DuckDBTierFailurePoint.AfterNodeDelete, (int)TierArchiveStage.DeleteHot, "failure_order_items")]
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
        context.Orders.Add(new FailureOrder
        {
            ExternalId = "order-1",
            CompletedDate = new DateTime(2024, 1, 15),
            Status = "complete",
            Items =
            [
                new FailureOrderItem
                {
                    ExternalOrderId = "order-1",
                    LineCode = "A",
                    Amount = 12m,
                },
            ],
        });
        context.SaveChanges();
        TestTierFailureInjector.FailOnce(failurePoint, table);

        var exception = await Assert.ThrowsAsync<TierArchiveOperationException>(
            () => context.Database.ArchiveTierAsync<FailureOrder>(new DateTime(2024, 2, 1)));

        Assert.Equal(expectedStage, exception.Stage);
        Assert.Equal(expectedStage, exception.PartialResult.Stage);
        Assert.Equal(1, exception.PartialResult.Nodes.Single(node => node.Table == "failure_orders").SelectedRows);
        Assert.Single(context.OrderHistory);
        Assert.Single(context.ItemHistory);
        if (failurePoint == DuckDBTierFailurePoint.AfterCopy)
        {
            Assert.Equal(
                1,
                exception.PartialResult.Nodes.Single(node => node.Table == "failure_orders").CopiedRows);
        }

        if (failurePoint == DuckDBTierFailurePoint.AfterNodeDelete)
        {
            Assert.Equal(
                1,
                exception.PartialResult.Nodes.Single(node => node.Table == "failure_order_items").DeletedRows);
        }

        var retry = await context.Database.ArchiveTierAsync<FailureOrder>(new DateTime(2024, 2, 1));

        Assert.Single(context.OrderHistory);
        Assert.Single(context.ItemHistory);
        Assert.Empty(context.Orders);
        Assert.Empty(context.Items);
        Assert.Equal(TierArchiveStage.Completed, retry.Stage);
    }

    [Fact]
    public async Task Reconciliation_recovers_after_generation_publication_failure()
    {
        var archivePath = Path.Combine(_root, "reconcile");
        using var context = new FailureContext(Path.Combine(_root, "reconcile.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(new FailureOrder
        {
            ExternalId = "order-1",
            CompletedDate = new DateTime(2024, 1, 15),
            Status = "complete",
        });
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<FailureOrder>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO failure_orders (\"Id\", \"CompletedDate\", \"ExternalId\", \"Status\") "
            + "VALUES (101, TIMESTAMP '2024-01-15', 'order-1', 'corrected');");
        TestTierFailureInjector.FailOnce(DuckDBTierFailurePoint.AfterPublication);

        var exception = await Assert.ThrowsAsync<TierArchiveOperationException>(
            () => context.Database.ReconcileArchiveTierAsync<FailureOrder>());

        Assert.Equal(TierArchiveStage.Publish, exception.Stage);
        Assert.NotNull(exception.PartialResult.Revision);
        Assert.Equal("corrected", context.OrderHistory.Single().Status);

        var retry = await context.Database.ReconcileArchiveTierAsync<FailureOrder>();

        Assert.Equal("corrected", context.OrderHistory.Single().Status);
        Assert.Empty(context.Orders);
        Assert.Equal(TierArchiveStage.Completed, retry.Stage);
    }

    private sealed class FailureOrder
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime CompletedDate { get; set; }
        public string Status { get; set; } = null!;
        public List<FailureOrderItem> Items { get; set; } = [];
    }

    private sealed class FailureOrderItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public FailureOrder? Order { get; set; }
        public string ExternalOrderId { get; set; } = null!;
        public string LineCode { get; set; } = null!;
        public decimal Amount { get; set; }
    }

    private sealed class FailureOrderHistory
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime CompletedDate { get; set; }
        public string Status { get; set; } = null!;
    }

    private sealed class FailureOrderItemHistory
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string ExternalOrderId { get; set; } = null!;
        public string LineCode { get; set; } = null!;
        public decimal Amount { get; set; }
    }

    private sealed class FailureContext(string dbPath, string archivePath) : DbContext
    {
        public DbSet<FailureOrder> Orders => Set<FailureOrder>();
        public DbSet<FailureOrderItem> Items => Set<FailureOrderItem>();
        public DbSet<FailureOrderHistory> OrderHistory => Set<FailureOrderHistory>();
        public DbSet<FailureOrderItemHistory> ItemHistory => Set<FailureOrderItemHistory>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IDuckDBTierFailureInjector, TestTierFailureInjector>()
                .ReplaceService<IModelCacheKeyFactory, FailureModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FailureOrder>(builder =>
            {
                builder.ToTable("failure_orders");
                builder.HasKey(order => order.Id);
                builder.HasMany(order => order.Items)
                    .WithOne(item => item.Order)
                    .HasForeignKey(item => item.OrderId);
            });
            modelBuilder.Entity<FailureOrderItem>(builder =>
            {
                builder.ToTable("failure_order_items");
                builder.HasKey(item => item.Id);
            });
            modelBuilder.ToTieredStore<FailureOrder>(
                    order => order.CompletedDate,
                    archivePath,
                    TierGranularity.Month)
                .MatchBy(order => order.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                .WithReadModel<FailureOrderHistory>()
                .Including<FailureOrderItem>(order => order.Items, items => items
                    .MatchBy(
                        item => new { item.ExternalOrderId, item.LineCode },
                        TierMatchKeyUniqueness.ExternallyEnforced)
                    .WithReadModel<FailureOrderItemHistory>());
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
