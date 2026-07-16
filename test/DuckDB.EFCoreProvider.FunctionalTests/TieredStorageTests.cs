using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data;
using System.Text;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class TieredStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "duckdb-tier-" + Guid.NewGuid().ToString("N"));

    public TieredStorageTests() => Directory.CreateDirectory(_root);

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
    public async Task Archives_whole_aggregate_and_tiered_reads_equal_full_history()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        var (invoices, lines, allocations, allocSum) = TieredTotals(context);

        var result = await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2025, 7, 1).AddMonths(-12));

        Assert.False(result.NoOp);
        Assert.Equal(5, result.RowsArchived);
        // Hot tables shrank; each aggregate table has its own Parquet subdirectory.
        Assert.True(context.Invoices.Count() < invoices);
        Assert.True(Directory.Exists(Path.Combine(_root, "archive", "invoices", "year=2024")));
        Assert.True(Directory.Exists(Path.Combine(_root, "archive", "invoice_lines", "year=2024")));
        Assert.True(Directory.Exists(Path.Combine(_root, "archive", "line_allocations", "year=2024")));
        // Tiered read-models reproduce the full history exactly — no duplicates, no gaps, at every level.
        var (tInvoices, tLines, tAllocations, tAllocSum) = TieredTotals(context);
        Assert.Equal((invoices, lines, allocations, allocSum), (tInvoices, tLines, tAllocations, tAllocSum));
    }

    [Fact]
    public async Task Additional_partition_is_declared_on_the_root_and_inherited_by_child_archives()
    {
        var dbPath = Path.Combine(_root, "customer.duckdb");
        var archivePath = Path.Combine(_root, "customer-archive");
        using var context = new CustomerPartitionContext(dbPath, archivePath);
        context.Database.EnsureCreated();
        context.Invoices.AddRange(
            new Invoice
            {
                CustomerId = 10,
                InvoiceDate = new DateTime(2024, 1, 10),
                Lines = { new InvoiceLine { Amount = 10 } },
            },
            new Invoice
            {
                CustomerId = 20,
                InvoiceDate = new DateTime(2024, 1, 20),
                Lines = { new InvoiceLine { Amount = 20 } },
            },
            new Invoice
            {
                CustomerId = 30,
                InvoiceDate = new DateTime(2024, 2, 10),
                Lines = { new InvoiceLine { Amount = 30 } },
            });
        context.SaveChanges();

        var result = await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2024, 2, 1));

        Assert.Equal(2, result.RowsArchived);
        foreach (var customerId in new[] { 10, 20 })
        {
            Assert.True(Directory.Exists(Path.Combine(
                archivePath, "invoices", $"CustomerId={customerId}", "InvoiceDate_month=2024-01-01")));
            Assert.True(Directory.Exists(Path.Combine(
                archivePath, "invoice_lines", $"CustomerId={customerId}", "InvoiceDate_month=2024-01-01")));
        }

        Assert.Equal([10, 20, 30], context.InvoiceHistory.OrderBy(invoice => invoice.CustomerId).Select(invoice => invoice.CustomerId).ToList());
        Assert.Equal(3, context.LineHistory.Count());
        Assert.Equal(["CustomerId", "InvoiceDate"], context.Model.FindEntityType(typeof(Invoice))!.GetTieredStorePartitionProperties());
        Assert.Empty(context.Model.FindEntityType(typeof(InvoiceLine))!.GetTieredStorePartitionProperties());
    }

    [Fact]
    public async Task Aliased_partitions_archive_and_preserve_hot_cold_reads_with_pruning()
    {
        var archivePath = Path.Combine(_root, "owner-alias-archive");
        using var context = new OwnerAliasPartitionContext(
            Path.Combine(_root, "owner-alias.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        context.Orders.AddRange(
            new OwnerOrder
            {
                OwnerId = 10,
                CompletedAt = new DateTime(2024, 1, 10),
                Items = { new OwnerOrderItem { OwnerId = 101 } },
            },
            new OwnerOrder
            {
                OwnerId = 10,
                CompletedAt = new DateTime(2024, 2, 10),
                Items = { new OwnerOrderItem { OwnerId = 102 } },
            },
            new OwnerOrder
            {
                OwnerId = 20,
                CompletedAt = new DateTime(2024, 1, 10),
                Items = { new OwnerOrderItem { OwnerId = 201 } },
            },
            new OwnerOrder
            {
                OwnerId = 20,
                CompletedAt = new DateTime(2024, 2, 10),
                Items = { new OwnerOrderItem { OwnerId = 202 } },
            },
            new OwnerOrder
            {
                OwnerId = 30,
                CompletedAt = new DateTime(2024, 3, 10),
                Items = { new OwnerOrderItem { OwnerId = 301 } },
            });
        context.SaveChanges();

        var result = await context.Database.ArchiveTierAsync<OwnerOrder>(new DateTime(2024, 3, 1));

        Assert.Equal(4, result.RowsArchived);
        Assert.True(Directory.Exists(Path.Combine(
            archivePath, "owner_orders", "root_owner_id=10", "completed_month=2024-01-01")));
        Assert.True(Directory.Exists(Path.Combine(
            archivePath, "owner_order_items", "root_owner_id=10", "completed_month=2024-01-01")));
        Assert.Equal(30, Assert.Single(context.Orders).OwnerId);
        Assert.Equal(301, Assert.Single(context.Items).OwnerId);
        Assert.Equal([10, 10, 20, 20, 30], context.OrderHistory.OrderBy(order => order.Id).Select(order => order.OwnerId).ToList());
        Assert.Equal([101, 102, 201, 202, 301], context.ItemHistory.OrderBy(item => item.Id).Select(item => item.OwnerId).ToList());

        var from = new DateTime(2024, 2, 1);
        var to = new DateTime(2024, 3, 1);
        var query = context.OrderHistory.Where(
            order => order.OwnerId == 10 && order.CompletedAt >= from && order.CompletedAt < to);
        var sql = query.ToQueryString();
        Assert.Contains("root_owner_id", sql);
        Assert.Contains("completed_month", sql);
        AssertFilesPruned(Explain(context, query), "1/4");
        Assert.Single(query);

        var partitionSpec = context.Database.SqlQueryRaw<string>(
            "SELECT partition_spec AS \"Value\" FROM __duckdb_tier_control WHERE name = 'owner_orders'").Single();
        Assert.Contains("\"Name\":\"root_owner_id\"", partitionSpec);
        Assert.Contains("\"Name\":\"completed_month\"", partitionSpec);
    }

    [Fact]
    public async Task Restoration_by_an_aliased_partition_moves_the_exact_graph_back_to_hot()
    {
        var archivePath = Path.Combine(_root, "owner-alias-restore-archive");
        using var context = new OwnerAliasPartitionContext(
            Path.Combine(_root, "owner-alias-restore.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        context.Orders.AddRange(
            new OwnerOrder
            {
                OwnerId = 10,
                CompletedAt = new DateTime(2024, 1, 10),
                Items = { new OwnerOrderItem { OwnerId = 101 } },
            },
            new OwnerOrder
            {
                OwnerId = 20,
                CompletedAt = new DateTime(2024, 1, 20),
                Items = { new OwnerOrderItem { OwnerId = 201 } },
            });
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<OwnerOrder>(new DateTime(2024, 2, 1));

        var result = await context.Database.RestoreArchiveTierAsync<OwnerOrder>(
            new TierRestoreOptions
            {
                Scope = TierMaintenanceScope.ForPartitionValues(
                    new Dictionary<string, object?> { ["OwnerId"] = 10 }),
            });

        Assert.Equal(TierArchiveOperation.Restore, result.Publication.Operation);
        Assert.Equal(1, result.RootsSelected);
        Assert.Equal(1, result.RootsInserted);
        Assert.Equal(2, result.RowsInserted);
        Assert.Equal(10, Assert.Single(context.Orders).OwnerId);
        Assert.Equal(101, Assert.Single(context.Items).OwnerId);
        Assert.Equal([10, 20], context.OrderHistory.OrderBy(order => order.OwnerId).Select(order => order.OwnerId).ToList());
        Assert.Equal([101, 201], context.ItemHistory.OrderBy(item => item.OwnerId).Select(item => item.OwnerId).ToList());
        Assert.True(Directory.Exists(Path.Combine(
            result.Publication.ArchivePath, "root_owner_id=20", "completed_month=2024-01-01")));
        Assert.False(Directory.Exists(Path.Combine(
            result.Publication.ArchivePath, "root_owner_id=10", "completed_month=2024-01-01")));
        var child = Assert.Single(result.Publication.Nodes, node => node.Table == "owner_order_items");
        Assert.True(Directory.Exists(Path.Combine(
            child.ArchivePath, "root_owner_id=20", "completed_month=2024-01-01")));
    }

    [Fact]
    public async Task Reconciliation_repartitions_corrected_rows_using_aliased_partition_names()
    {
        var archivePath = Path.Combine(_root, "owner-alias-reconcile-archive");
        using var context = new OwnerAliasPartitionContext(
            Path.Combine(_root, "owner-alias-reconcile.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        var order = new OwnerOrder
        {
            OwnerId = 10,
            CompletedAt = new DateTime(2024, 1, 10),
            Items = { new OwnerOrderItem { OwnerId = 101 } },
        };
        context.Orders.Add(order);
        context.SaveChanges();
        var orderId = order.Id;
        var itemId = order.Items[0].Id;
        await context.Database.ArchiveTierAsync<OwnerOrder>(new DateTime(2024, 2, 1));
        context.ChangeTracker.Clear();
        context.Database.ExecuteSqlInterpolated(
            $"""
             INSERT INTO owner_orders ("Id", "CompletedAt", "OwnerId")
             VALUES ({orderId}, {new DateTime(2024, 1, 10)}, {20});
             """);
        context.Database.ExecuteSqlInterpolated(
            $"""
             INSERT INTO owner_order_items ("Id", "OwnerId", "OwnerOrderId")
             VALUES ({itemId}, {999}, {orderId});
             """);

        var result = await context.Database.ReconcileArchiveTierAsync<OwnerOrder>();

        Assert.Equal(TierArchiveOperation.Reconcile, result.Operation);
        Assert.False(result.NoOp);
        Assert.Equal(1, result.RowsArchived);
        Assert.Empty(context.Orders);
        Assert.Empty(context.Items);
        Assert.Equal(20, context.OrderHistory.Single().OwnerId);
        Assert.Equal(999, context.ItemHistory.Single().OwnerId);
        Assert.True(Directory.Exists(Path.Combine(
            result.ArchivePath, "root_owner_id=20", "completed_month=2024-01-01")));
        Assert.False(Directory.Exists(Path.Combine(
            result.ArchivePath, "root_owner_id=10", "completed_month=2024-01-01")));
        var child = Assert.Single(result.Nodes, node => node.Table == "owner_order_items");
        Assert.True(Directory.Exists(Path.Combine(
            child.ArchivePath, "root_owner_id=20", "completed_month=2024-01-01")));
    }

    [Fact]
    public async Task Exact_partition_alias_shorthand_retains_the_implicit_lifecycle_bucket()
    {
        var archivePath = Path.Combine(_root, "shorthand-alias-archive");
        using var context = new ShorthandAliasPartitionContext(
            Path.Combine(_root, "shorthand-alias.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        context.Invoices.Add(new Invoice { CustomerId = 10, InvoiceDate = new DateTime(2024, 1, 10) });
        context.SaveChanges();

        await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2024, 2, 1));

        Assert.True(Directory.Exists(Path.Combine(
            archivePath, "invoices", "customer_key=10", "InvoiceDate_month=2024-01-01")));
    }

    [Fact]
    public async Task Declared_partition_order_controls_the_directory_hierarchy()
    {
        var archivePath = Path.Combine(_root, "month-first-archive");
        using var context = new MonthFirstPartitionContext(Path.Combine(_root, "month-first.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Invoices.Add(new Invoice { CustomerId = 10, InvoiceDate = new DateTime(2024, 1, 10) });
        context.SaveChanges();

        await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2024, 2, 1));

        Assert.True(Directory.Exists(Path.Combine(
            archivePath, "invoices", "InvoiceDate_month=2024-01-01", "CustomerId=10")));
    }

    [Fact]
    public async Task Date_partition_transforms_create_application_defined_year_month_and_day_buckets()
    {
        var archivePath = Path.Combine(_root, "date-buckets-archive");
        using var context = new DateBucketPartitionContext(Path.Combine(_root, "date-buckets.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(new DateBucketRecord
        {
            CustomerId = 10,
            CreatedAt = new DateTime(2023, 12, 20),
            ReviewedAt = new DateTime(2024, 1, 5),
            CompletedAt = new DateTime(2024, 1, 10, 15, 30, 0),
        });
        context.SaveChanges();

        await context.Database.ArchiveTierAsync<DateBucketRecord>(new DateTime(2024, 1, 11));

        Assert.True(Directory.Exists(Path.Combine(
            archivePath,
            "date_bucket_records",
            "CustomerId=10",
            "CreatedAt_year=2023-01-01",
            "ReviewedAt_month=2024-01-01",
            "CompletedAt_day=2024-01-10")));
    }

    [Fact]
    public async Task Customer_and_completed_month_predicates_prune_the_corresponding_parquet_partitions()
    {
        var archivePath = Path.Combine(_root, "pruning-archive");
        using var context = new CustomerPartitionContext(Path.Combine(_root, "pruning.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Invoices.AddRange(
            new Invoice { CustomerId = 10, InvoiceDate = new DateTime(2024, 1, 10), Lines = { new InvoiceLine { Amount = 10 } } },
            new Invoice { CustomerId = 10, InvoiceDate = new DateTime(2024, 2, 10), Lines = { new InvoiceLine { Amount = 10 } } },
            new Invoice { CustomerId = 20, InvoiceDate = new DateTime(2024, 1, 10), Lines = { new InvoiceLine { Amount = 20 } } },
            new Invoice { CustomerId = 20, InvoiceDate = new DateTime(2024, 2, 10), Lines = { new InvoiceLine { Amount = 20 } } });
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2024, 3, 1));

        var from = new DateTime(2024, 2, 1);
        var to = new DateTime(2024, 2, 29);
        var customerAndMonth = context.InvoiceHistory.Where(
            invoice => invoice.CustomerId == 10 && invoice.InvoiceDate >= from && invoice.InvoiceDate < to);
        var customerOnly = context.InvoiceHistory.Where(invoice => invoice.CustomerId == 10);
        var monthOnly = context.InvoiceHistory.Where(invoice => invoice.InvoiceDate >= from && invoice.InvoiceDate < to);

        var sql = customerAndMonth.ToQueryString();
        Assert.Contains("InvoiceDate_month", sql);
        Assert.Contains("date_trunc('month'", sql);
        AssertFilesPruned(Explain(context, customerAndMonth), "1/4");
        AssertFilesPruned(Explain(context, customerOnly), "2/4");
        AssertFilesPruned(Explain(context, monthOnly), "2/4");
        Assert.Equal([2], customerAndMonth.Select(invoice => invoice.Id).ToList());
    }

    [Fact]
    public async Task Typed_partition_properties_preserve_store_types_across_hot_and_cold()
    {
        var dbPath = Path.Combine(_root, "typed.duckdb");
        var archivePath = Path.Combine(_root, "typed-archive");
        var coldTenant = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var hotTenant = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
        using var context = new TypedPartitionContext(dbPath, archivePath);
        context.Database.EnsureCreated();
        var preCopyPartitionSpec = context.Database.SqlQueryRaw<string>(
            "SELECT partition_spec AS \"Value\" FROM __duckdb_tier_control WHERE name = 'typed_roots'").Single();
        Assert.Contains("\"Version\":2", preCopyPartitionSpec);
        context.Roots.AddRange(
            new TypedPartitionRoot
            {
                ArchivedAt = new DateTime(2024, 1, 10),
                CustomerId = 10,
                Region = "EU/West",
                IsPriority = true,
                AmountBand = 12.34m,
                SnapshotAt = new DateTime(2024, 1, 10, 12, 34, 56),
                EffectiveDate = new DateOnly(2024, 1, 10),
                TenantId = coldTenant,
            },
            new TypedPartitionRoot
            {
                ArchivedAt = new DateTime(2024, 2, 10),
                CustomerId = 20,
                Region = "US",
                IsPriority = false,
                AmountBand = 56.78m,
                SnapshotAt = new DateTime(2024, 2, 10, 8, 0, 0),
                EffectiveDate = new DateOnly(2024, 2, 10),
                TenantId = hotTenant,
            });
        context.SaveChanges();

        await context.Database.ArchiveTierAsync<TypedPartitionRoot>(new DateTime(2024, 2, 1));

        Assert.Equal(69.12m, context.History.Sum(row => row.AmountBand));
        Assert.Single(context.History.Where(row => row.IsPriority));
        Assert.Single(context.History.Where(row => row.SnapshotAt < new DateTime(2024, 2, 1)));
        Assert.Single(context.History.Where(row => row.EffectiveDate < new DateOnly(2024, 2, 1)));
        Assert.Single(context.History.Where(row => row.TenantId == coldTenant));
        Assert.Equal(["EU/West", "US"], context.History.OrderBy(row => row.CustomerId).Select(row => row.Region).ToList());

        var coldFile = Assert.Single(Directory.GetFiles(archivePath, "*.parquet", SearchOption.AllDirectories));
        Assert.Contains(Path.Combine("customer_id=10", "region_code=EU%2FWest"), coldFile);
        Assert.Contains("ArchivedAt_month=2024-01-01", coldFile);
        Assert.Equal(
            ["CustomerId", "Region", "IsPriority", "AmountBand", "SnapshotAt", "EffectiveDate", "TenantId"],
            context.Model.FindEntityType(typeof(TypedPartitionRoot))!.GetTieredStorePartitionProperties());

        var partitionSpec = context.Database.SqlQueryRaw<string>(
            "SELECT partition_spec AS \"Value\" FROM __duckdb_tier_control WHERE name = 'typed_roots'").Single();
        Assert.Contains("\"Version\":2", partitionSpec);
        Assert.Contains("\"Granularity\":0", partitionSpec);
        Assert.Contains("\"Name\":\"AmountBand\",\"StoreType\":\"DECIMAL(10,2)\"", partitionSpec);
        Assert.Contains("\"Name\":\"EffectiveDate\",\"StoreType\":\"DATE\"", partitionSpec);
    }

    [Fact]
    public void Child_builder_does_not_expose_partition_configuration()
        => Assert.DoesNotContain(
            typeof(TieredChildBuilder<InvoiceLine>).GetMethods(),
            method => method.Name == nameof(TieredStoreBuilder<Invoice>.PartitionBy));

    [Fact]
    public void Repeated_partition_calls_reject_duplicate_properties()
    {
        using var context = new DuplicatePartitionContext(Path.Combine(_root, "duplicate.duckdb"), Path.Combine(_root, "duplicate-archive"));
        var exception = Assert.Throws<ArgumentException>(() => _ = context.Model);

        Assert.Contains("can only be declared once", exception.Message);
    }

    [Fact]
    public void Exact_value_shorthand_does_not_duplicate_the_lifecycle_property()
    {
        using var context = new ExactLifecyclePartitionContext(
            Path.Combine(_root, "exact-lifecycle.duckdb"),
            Path.Combine(_root, "exact-lifecycle-archive"));
        using var repeated = new RepeatedExactLifecyclePartitionContext(
            Path.Combine(_root, "repeated-exact-lifecycle.duckdb"),
            Path.Combine(_root, "repeated-exact-lifecycle-archive"));

        Assert.Equal(
            ["InvoiceDate"],
            context.Model.FindEntityType(typeof(Invoice))!.GetTieredStorePartitionProperties());
        Assert.Equal(
            ["CustomerId", "InvoiceDate"],
            repeated.Model.FindEntityType(typeof(Invoice))!.GetTieredStorePartitionProperties());
    }

    [Fact]
    public void Partition_metadata_on_a_child_is_rejected_at_model_validation()
    {
        using var context = new BadChildPartitionContext(Path.Combine(_root, "s.duckdb"), Path.Combine(_root, "a"));
        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("only be declared on an aggregate root", exception.Message);
    }

    [Fact]
    public void Ordered_partition_plan_must_include_a_safe_lifecycle_bucket()
    {
        using var context = new MissingLifecyclePartitionContext(
            Path.Combine(_root, "missing-lifecycle.duckdb"),
            Path.Combine(_root, "missing-lifecycle-archive"));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("lifecycle property", exception.Message);
        Assert.Contains("ByMonth", exception.Message);
    }

    [Fact]
    public void Date_partition_transform_rejects_a_non_date_property()
    {
        using var context = new NonDateTransformContext(
            Path.Combine(_root, "non-date.duckdb"),
            Path.Combine(_root, "non-date-archive"));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("is not DateTime or DateOnly", exception.Message);
    }

    [Fact]
    public void Partition_alias_must_be_non_empty()
    {
        using var context = new EmptyPartitionAliasContext(
            Path.Combine(_root, "empty-alias.duckdb"),
            Path.Combine(_root, "empty-alias-archive"));

        var exception = Assert.Throws<ArgumentException>(() => _ = context.Model);

        Assert.Contains("cannot be empty or whitespace", exception.Message);
    }

    [Fact]
    public void Partition_alias_must_not_collide_with_a_mapped_root_column()
    {
        using var context = new RootColumnAliasCollisionContext(
            Path.Combine(_root, "root-column-alias.duckdb"),
            Path.Combine(_root, "root-column-alias-archive"));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("collides with a mapped root column", exception.Message);
    }

    [Fact]
    public void Partition_aliases_must_be_physically_distinct()
    {
        using var context = new DuplicatePartitionAliasContext(
            Path.Combine(_root, "duplicate-alias.duckdb"),
            Path.Combine(_root, "duplicate-alias-archive"));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("distinct physical column", exception.Message);
    }

    [Fact]
    public async Task Archive_is_idempotent_on_rerun()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        var before = TieredTotals(context);
        var cutoff = new DateTime(2025, 7, 1).AddMonths(-12);

        var first = await context.Database.ArchiveTierAsync<Invoice>(cutoff);
        var second = await context.Database.ArchiveTierAsync<Invoice>(cutoff.AddMonths(-1));

        Assert.True(second.NoOp);
        Assert.Equal(first.Watermark, second.WindowStart);
        Assert.Equal(first.Watermark, second.WindowEnd);
        Assert.All(second.Nodes, node => Assert.NotEmpty(node.Files));
        Assert.Equal(before, TieredTotals(context));
    }

    [Fact]
    public async Task Multi_table_archive_rejects_an_existing_transaction_before_copying()
    {
        using var context = CreateContext();
        Seed(context, months: 2, baseDate: new DateTime(2025, 7, 1));
        await using var transaction = await context.Database.BeginTransactionAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Database.ArchiveTierAsync<Invoice>(new DateTime(2025, 7, 1)));

        Assert.Contains("outside the caller transaction", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(_root, "archive", "invoices")));
    }

    [Fact]
    public async Task Single_table_archive_rejects_an_existing_transaction_before_copying()
    {
        var archivePath = Path.Combine(_root, "single-transaction-archive");
        using var context = new MonthFirstPartitionContext(
            Path.Combine(_root, "single-transaction.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        context.Invoices.Add(new Invoice { CustomerId = 10, InvoiceDate = new DateTime(2024, 1, 15) });
        context.SaveChanges();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Database.ArchiveTierAsync<Invoice>(new DateTime(2024, 2, 1)));

        Assert.Contains("cannot be rolled back", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(archivePath, "invoices")));
    }

    [Fact]
    public async Task Reporting_join_across_read_models_spans_hot_and_cold()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2025, 7, 1).AddMonths(-12));

        var revenueByYear =
            (from l in context.LineHistory
             join i in context.InvoiceHistory on l.InvoiceId equals i.Id
             group l.Amount by i.InvoiceDate.Year into g
             select new { Year = g.Key, Revenue = g.Sum() }).ToList();

        // Both the cold (2024) and hot (2025) years must appear in one query.
        Assert.Contains(revenueByYear, r => r.Year == 2024);
        Assert.Contains(revenueByYear, r => r.Year == 2025);
        Assert.Equal(context.LineHistory.Sum(l => l.Amount), revenueByYear.Sum(r => r.Revenue));
    }

    [Fact]
    public void Hot_writes_and_include_are_unaffected_by_tiering()
    {
        using var context = CreateContext();

        // Normal EF: write a root with a child graph, then Include across the aggregate.
        var invoice = new Invoice { InvoiceDate = new DateTime(2025, 6, 1) };
        var line = new InvoiceLine { Amount = 42 };
        line.Allocations.Add(new LineAllocation { Amount = 42 });
        invoice.Lines.Add(line);
        context.Invoices.Add(invoice);
        context.SaveChanges();

        using var reader = CreateContext();
        var loaded = reader.Invoices.Include(i => i.Lines).ThenInclude(l => l.Allocations).Single();
        Assert.Single(loaded.Lines);
        Assert.Single(loaded.Lines[0].Allocations);
        Assert.Equal(42, loaded.Lines[0].Allocations[0].Amount);
    }

    [Fact]
    public void Ensure_created_alone_creates_all_aggregate_views()
    {
        using var context = CreateContext(); // EnsureCreated only; no explicit EnsureTieredStoresCreated
        context.Invoices.Add(new Invoice { InvoiceDate = new DateTime(2025, 6, 1), Lines = { new InvoiceLine { Amount = 1 } } });
        context.SaveChanges();

        // Querying every tiered view must not raise a "view does not exist" error.
        Assert.Equal(1, context.InvoiceHistory.Count());
        Assert.Equal(1, context.LineHistory.Count());
    }

    [Fact]
    public void Existing_control_table_is_upgraded_with_partition_metadata()
    {
        using var context = CreateContext();
        context.Database.ExecuteSqlRaw("DROP VIEW line_allocations_tiered;");
        context.Database.ExecuteSqlRaw("DROP VIEW invoice_lines_tiered;");
        context.Database.ExecuteSqlRaw("DROP VIEW invoices_tiered;");
        context.Database.ExecuteSqlRaw("DROP TABLE __duckdb_tier_control;");
        context.Database.ExecuteSqlRaw(
            "CREATE TABLE __duckdb_tier_control (name TEXT PRIMARY KEY, watermark TIMESTAMP, archive_path TEXT, granularity TEXT);");

        context.Database.EnsureTieredStoresCreated();

        var partitionColumns = context.Database
            .SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM pragma_table_info('__duckdb_tier_control') WHERE name = 'partition_spec'")
            .Single();
        Assert.Equal(1, partitionColumns);
        var archiveColumns = context.Database
            .SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM pragma_table_info('__duckdb_tier_control') WHERE name = 'archive_spec'")
            .Single();
        Assert.Equal(1, archiveColumns);
    }

    [Fact]
    public async Task Purge_drops_a_period_across_every_aggregate_table()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2025, 7, 1).AddMonths(-12));
        var beforeInvoices = context.InvoiceHistory.Count();

        var purged = context.Database.PurgeArchiveOlderThan<Invoice>(new DateTime(2024, 4, 1));

        Assert.Equal(6, purged); // 2 months × 3 tables
        Assert.Equal(beforeInvoices - 2, context.InvoiceHistory.Count());
    }

    [Fact]
    public async Task Purge_that_empties_the_archive_falls_back_to_a_hot_only_view()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2025, 7, 1).AddMonths(-12));

        // Purge past all cold data: every archived partition is removed for every aggregate table.
        var purged = context.Database.PurgeArchiveOlderThan<Invoice>(new DateTime(2025, 7, 1));

        Assert.True(purged > 0);
        // The views must fall back to hot-only rather than error on an empty read_parquet glob.
        Assert.Equal(context.Invoices.Count(), context.InvoiceHistory.Count());
        Assert.Equal(context.Set<InvoiceLine>().Count(), context.LineHistory.Count());
    }

    [Fact]
    public void Reserved_partition_column_name_is_rejected_at_model_validation()
    {
        using var context = new ReservedColumnContext(Path.Combine(_root, "s.duckdb"), Path.Combine(_root, "a"));
        var ex = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("collides with the hive partition column", ex.Message);
    }

    [Fact]
    public void Overlapping_aggregate_archive_paths_are_rejected_at_model_validation()
    {
        using var context = new OverlapContext(Path.Combine(_root, "s.duckdb"), Path.Combine(_root, "shared"));
        var ex = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("overlapping", ex.Message);
    }

    [Fact]
    public void Missing_child_navigation_is_rejected_at_model_validation()
    {
        using var context = new BadNavigationContext(Path.Combine(_root, "s.duckdb"), Path.Combine(_root, "a"));
        var ex = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("was not found", ex.Message);
    }

    [Fact]
    public void Read_model_column_not_on_source_is_rejected_at_model_validation()
    {
        using var context = new BadReadModelContext(Path.Combine(_root, "s.duckdb"), Path.Combine(_root, "a"));
        var ex = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("must mirror", ex.Message);
    }

    [Fact]
    public async Task Changing_partition_layout_with_existing_cold_files_is_rejected()
    {
        var dbPath = Path.Combine(_root, "layout.duckdb");
        var archivePath = Path.Combine(_root, "layout-archive");
        using (var original = new InvoiceContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            Seed(original, months: 2, baseDate: new DateTime(2024, 2, 1));
            await original.Database.ArchiveTierAsync<Invoice>(new DateTime(2024, 2, 1));
        }

        using var changed = new CustomerPartitionContext(dbPath, archivePath);
        var exception = Assert.Throws<InvalidOperationException>(() => changed.Database.EnsureTieredStoresCreated());

        Assert.Contains("different partition layout", exception.Message);
        Assert.Contains("Rewrite or clear", exception.Message);
    }

    [Fact]
    public async Task Changing_an_aliased_partition_name_is_rejected_as_partition_layout_drift()
    {
        var dbPath = Path.Combine(_root, "alias-layout.duckdb");
        var archivePath = Path.Combine(_root, "alias-layout-archive");
        using (var original = new OwnerAliasPartitionContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            original.Orders.Add(new OwnerOrder
            {
                OwnerId = 10,
                CompletedAt = new DateTime(2024, 1, 10),
                Items = { new OwnerOrderItem { OwnerId = 101 } },
            });
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<OwnerOrder>(new DateTime(2024, 2, 1));
            var partitionSpec = original.Database.SqlQueryRaw<string>(
                "SELECT partition_spec AS \"Value\" FROM __duckdb_tier_control WHERE name = 'owner_orders'").Single();
            Assert.Contains("\"Name\":\"root_owner_id\"", partitionSpec);
        }

        using var changed = new ChangedOwnerAliasPartitionContext(dbPath, archivePath);
        var exception = Assert.Throws<InvalidOperationException>(
            () => changed.Database.EnsureTieredStoresCreated());

        Assert.Contains("root_owner_key", exception.Message);
        Assert.Contains("different partition layout", exception.Message);
        Assert.Contains("Rewrite or clear", exception.Message);
    }

    [Fact]
    public async Task Unrecorded_cold_files_reject_a_new_partition_layout()
    {
        var dbPath = Path.Combine(_root, "orphan.duckdb");
        var archivePath = Path.Combine(_root, "orphan-archive");
        using (var original = new InvoiceContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            Seed(original, months: 2, baseDate: new DateTime(2024, 2, 1));
            await original.Database.ArchiveTierAsync<Invoice>(new DateTime(2024, 2, 1));
            original.Database.ExecuteSqlRaw("DELETE FROM __duckdb_tier_control WHERE name = 'invoices';");
        }

        using var changed = new CustomerPartitionContext(dbPath, archivePath);
        var exception = Assert.Throws<InvalidOperationException>(() => changed.Database.EnsureTieredStoresCreated());

        Assert.Contains("unrecorded partition layout", exception.Message);
        Assert.Contains("Rewrite or clear", exception.Message);
    }

    [Fact]
    public async Task Changing_granularity_with_existing_cold_files_is_rejected()
    {
        var dbPath = Path.Combine(_root, "granularity.duckdb");
        var archivePath = Path.Combine(_root, "granularity-archive");
        using (var original = new InvoiceContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            Seed(original, months: 2, baseDate: new DateTime(2024, 2, 1));
            await original.Database.ArchiveTierAsync<Invoice>(new DateTime(2024, 2, 1));
        }

        using var changed = new DayGranularityContext(dbPath, archivePath);
        var exception = Assert.Throws<InvalidOperationException>(() => changed.Database.EnsureTieredStoresCreated());

        Assert.Contains("different partition layout", exception.Message);
        Assert.Contains("granularity", exception.Message);
    }

    [Fact]
    public async Task Legacy_temporal_layout_is_backfilled_with_a_versioned_signature()
    {
        using var context = CreateContext();
        Seed(context, months: 2, baseDate: new DateTime(2024, 2, 1));
        await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "UPDATE __duckdb_tier_control SET partition_spec = NULL WHERE name = 'invoices';");

        context.Database.EnsureTieredStoresCreated();

        var partitionSpec = context.Database.SqlQueryRaw<string>(
            "SELECT partition_spec AS \"Value\" FROM __duckdb_tier_control WHERE name = 'invoices'").Single();
        Assert.Contains("\"Version\":2", partitionSpec);
        Assert.Contains("\"Columns\":[]", partitionSpec);
    }

    [Fact]
    public async Task Crash_between_copy_and_delete_self_heals_without_double_counting()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        var expected = TieredTotals(context);
        var cutoff = new DateTime(2025, 7, 1).AddMonths(-12);

        var result = await context.Database.ArchiveTierAsync<Invoice>(cutoff);

        // Simulate a crash after COPY but before DELETE: the archived rows are back in the hot tables.
        ReinsertArchivedIntoHot(context);
        Assert.Equal(expected, TieredTotals(context)); // views still exact — no double counting

        var heal = await context.Database.ArchiveTierAsync<Invoice>(cutoff);
        Assert.True(heal.NoOp);
        Assert.Equal(expected, TieredTotals(context));
        Assert.Equal(result.Watermark, heal.Watermark);
    }

    [Fact]
    public async Task Late_hot_rows_before_existing_watermark_remain_visible_and_are_not_deleted()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        var cutoff = new DateTime(2025, 7, 1).AddMonths(-12);

        await context.Database.ArchiveTierAsync<Invoice>(cutoff);
        var before = TieredTotals(context);

        context.Invoices.Add(new Invoice { InvoiceDate = cutoff.AddMonths(-1) });
        context.SaveChanges();

        Assert.Equal(before.Invoices + 1, context.InvoiceHistory.Count());

        var rerun = await context.Database.ArchiveTierAsync<Invoice>(cutoff);

        Assert.True(rerun.NoOp);
        Assert.Equal(before.Invoices + 1, context.InvoiceHistory.Count());
    }

    [Fact]
    public async Task Noop_archive_rerun_does_not_delete_late_hot_rows_before_existing_watermark()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        var cutoff = new DateTime(2025, 7, 1).AddMonths(-12);

        await context.Database.ArchiveTierAsync<Invoice>(cutoff);
        var hotRowsAfterArchive = context.Invoices.Count();

        context.Invoices.Add(new Invoice { InvoiceDate = cutoff.AddMonths(-1) });
        context.SaveChanges();

        Assert.Equal(hotRowsAfterArchive + 1, context.Invoices.Count());

        var rerun = await context.Database.ArchiveTierAsync<Invoice>(cutoff);

        Assert.True(rerun.NoOp);
        Assert.Equal(hotRowsAfterArchive + 1, context.Invoices.Count());
    }

    [Fact]
    public void Cold_files_without_a_watermark_do_not_hide_hot_rows_when_views_are_regenerated()
    {
        using var context = CreateContext();
        Seed(context, months: 3, baseDate: new DateTime(2025, 7, 1));
        var archive = Path.Combine(_root, "archive", "invoices");
        Directory.CreateDirectory(archive);

#pragma warning disable EF1002, EF1003 // archive is a test-owned temp path, not user input
        context.Database.ExecuteSqlRaw(
            $"""
             COPY (
                 SELECT "Id", "InvoiceDate", year("InvoiceDate") AS "year", month("InvoiceDate") AS "month"
                   FROM invoices
             )
             TO '{archive.Replace("'", "''")}'
             (FORMAT PARQUET, PARTITION_BY ("year", "month"), OVERWRITE_OR_IGNORE);
             """);
#pragma warning restore EF1002, EF1003

        context.Database.EnsureTieredStoresCreated();

        Assert.Equal(context.Invoices.Count(), context.InvoiceHistory.Count());
    }

    [Fact]
    public void Tiered_storage_honors_schema_mapped_hot_tables()
    {
        using var context = new SchemaContext(Path.Combine(_root, "schema.duckdb"), Path.Combine(_root, "schema-archive"));

        context.Database.EnsureCreated();
        context.Invoices.Add(new Invoice { InvoiceDate = new DateTime(2025, 6, 1) });
        context.SaveChanges();

        Assert.Equal(1, context.InvoiceHistory.Count());
    }

    [Fact]
    public void Purge_skips_malformed_partition_directories()
    {
        using var context = CreateContext();
        Directory.CreateDirectory(Path.Combine(_root, "archive", "invoices", "year=2024", "month=99"));

        var purged = context.Database.PurgeArchiveOlderThan<Invoice>(new DateTime(2025, 1, 1));

        Assert.Equal(0, purged);
    }

    [Fact]
    public async Task Column_added_after_archiving_reads_null_for_cold_rows()
    {
        var dbPath = Path.Combine(_root, "store.duckdb");
        var archivePath = Path.Combine(_root, "archive");

        using (var context = CreateContext())
        {
            Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
            await context.Database.ArchiveTierAsync<Invoice>(new DateTime(2025, 7, 1).AddMonths(-12));
        }

        using (var evolved = new EvolvedContext(dbPath, archivePath))
        {
            evolved.Database.ExecuteSqlRaw("ALTER TABLE invoices ADD COLUMN \"Note\" TEXT;");
            evolved.Database.EnsureTieredStoresCreated(); // regenerate the view over the new schema

            var all = evolved.InvoiceHistory.OrderBy(i => i.Id).ToList();
            Assert.Equal(18, all.Count);
            Assert.All(all.Where(i => i.Id <= 5), i => Assert.Null(i.Note)); // the 5 archived (cold) invoices
        }
    }

    [Fact]
    public async Task Nullable_column_contract_change_can_be_planned_and_rewritten_immutably()
    {
        var dbPath = Path.Combine(_root, "contract-rewrite.duckdb");
        var archivePath = Path.Combine(_root, "contract-rewrite-archive");
        using (var original = new InvoiceContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            original.Invoices.Add(new Invoice
            {
                CustomerId = 7,
                InvoiceDate = new DateTime(2024, 1, 15),
            });
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<Invoice>(new DateTime(2024, 2, 1));
        }

        using var evolved = new EvolvedContext(dbPath, archivePath);
        evolved.Database.ExecuteSqlRaw("ALTER TABLE invoices ADD COLUMN \"Note\" TEXT;");
        var inspection = await evolved.Database.InspectArchiveContractAsync<InvoiceV2>();

        var difference = Assert.Single(inspection.Differences, item =>
            item.Kind == TierArchiveContractDifferenceKind.ColumnAdded);
        Assert.Equal("Note", difference.Column);
        Assert.True(inspection.IsCompatible);

        var plan = await evolved.Database.PlanArchiveContractRewriteAsync<InvoiceV2>(
            new TierArchiveRewriteOptions());
        var result = await evolved.Database.RewriteArchiveContractAsync<InvoiceV2>(plan);

        Assert.Equal(TierArchiveOperation.RewriteContract, result.Operation);
        Assert.NotNull(result.Revision);
        Assert.Null(evolved.InvoiceHistory.Single().Note);
        var afterRewrite = await evolved.Database.InspectArchiveContractAsync<InvoiceV2>();
        Assert.True(afterRewrite.IsCompatible);
        Assert.Empty(afterRewrite.Differences);
        var laterMaintenance = await evolved.Database.CompactArchiveTierAsync<InvoiceV2>();
        Assert.Equal(TierArchiveOperation.Compact, laterMaintenance.Operation);
    }

    [Fact]
    public async Task Aliased_partitions_survive_contract_drift_inspection_and_rewrite()
    {
        var dbPath = Path.Combine(_root, "aliased-contract-rewrite.duckdb");
        var archivePath = Path.Combine(_root, "aliased-contract-rewrite-archive");
        using (var original = new AliasedContractContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            original.Invoices.Add(new Invoice
            {
                CustomerId = 7,
                InvoiceDate = new DateTime(2024, 1, 15),
            });
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<Invoice>(new DateTime(2024, 2, 1));
        }

        using var evolved = new AliasedContractEvolvedContext(dbPath, archivePath);
        evolved.Database.ExecuteSqlRaw("ALTER TABLE invoices ADD COLUMN \"Note\" TEXT;");
        var inspection = await evolved.Database.InspectArchiveContractAsync<InvoiceV2>();

        var difference = Assert.Single(inspection.Differences, item =>
            item.Kind == TierArchiveContractDifferenceKind.ColumnAdded);
        Assert.Equal("Note", difference.Column);
        Assert.True(inspection.IsCompatible);

        var plan = await evolved.Database.PlanArchiveContractRewriteAsync<InvoiceV2>(
            new TierArchiveRewriteOptions());
        var result = await evolved.Database.RewriteArchiveContractAsync<InvoiceV2>(plan);

        Assert.Equal(TierArchiveOperation.RewriteContract, result.Operation);
        Assert.True(Directory.Exists(Path.Combine(
            result.ArchivePath, "customer_key=7", "invoice_month=2024-01-01")));
        Assert.Null(evolved.InvoiceHistory.Single().Note);
        var afterRewrite = await evolved.Database.InspectArchiveContractAsync<InvoiceV2>();
        Assert.True(afterRewrite.IsCompatible);
        Assert.Empty(afterRewrite.Differences);
    }

    private void ReinsertArchivedIntoHot(InvoiceContext context)
    {
        var archive = Path.Combine(_root, "archive");

        void Copy(string table, string columns)
        {
            var glob = Path.Combine(archive, table).Replace("'", "''") + "/**/*.parquet";
#pragma warning disable EF1002, EF1003 // table/columns/glob are test constants, not user input
            context.Database.ExecuteSqlRaw(
                $"INSERT INTO {table} ({columns}) SELECT {columns} FROM read_parquet('{glob}', hive_partitioning=true, union_by_name=true);");
#pragma warning restore EF1002, EF1003
        }

        // Foreign-key order: parents before children.
        Copy("invoices", "\"Id\", \"CustomerId\", \"InvoiceDate\"");
        Copy("invoice_lines", "\"Id\", \"InvoiceId\", \"Amount\"");
        Copy("line_allocations", "\"Id\", \"InvoiceLineId\", \"Amount\"");
    }

    private static (int Invoices, int Lines, int Allocations, decimal AllocSum) TieredTotals(InvoiceContext c)
        => (c.InvoiceHistory.Count(), c.LineHistory.Count(), c.AllocationHistory.Count(), c.AllocationHistory.Sum(a => a.Amount));

    private static void Seed(InvoiceContext context, int months, DateTime baseDate)
    {
        for (var m = months - 1; m >= 0; m--)
        {
            var invoice = new Invoice { InvoiceDate = baseDate.AddMonths(-m) };
            for (var line = 0; line < 2; line++)
            {
                var amount = (m + 1) * 10 + line;
                invoice.Lines.Add(new InvoiceLine { Amount = amount, Allocations = { new LineAllocation { Amount = amount } } });
            }

            context.Invoices.Add(invoice);
        }

        context.SaveChanges();
    }

    private static string Explain<T>(DbContext context, IQueryable<T> query)
    {
        using var command = query.CreateDbCommand();
        command.CommandText = "EXPLAIN ANALYZE " + command.CommandText;
        var openedHere = command.Connection!.State != ConnectionState.Open;
        if (openedHere)
        {
            context.Database.OpenConnection();
        }

        try
        {
            using var reader = command.ExecuteReader();
            var plan = new StringBuilder();
            while (reader.Read())
            {
                for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    plan.AppendLine(reader.GetValue(ordinal)?.ToString());
                }
            }

            return plan.ToString();
        }
        finally
        {
            if (openedHere)
            {
                context.Database.CloseConnection();
            }
        }
    }

    private static void AssertFilesPruned(string plan, string expectedFraction)
    {
        Assert.Contains("Scanning Files:", plan);
        Assert.Contains(expectedFraction, plan);
    }

    private InvoiceContext CreateContext()
    {
        var context = new InvoiceContext(Path.Combine(_root, "store.duckdb"), Path.Combine(_root, "archive"));
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class Invoice
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public DateTime InvoiceDate { get; set; }
        public List<InvoiceLine> Lines { get; set; } = [];
    }

    private sealed class InvoiceLine
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        public Invoice? Invoice { get; set; }
        public decimal Amount { get; set; }
        public List<LineAllocation> Allocations { get; set; } = [];
    }

    private sealed class LineAllocation
    {
        public int Id { get; set; }
        public int InvoiceLineId { get; set; }
        public InvoiceLine? InvoiceLine { get; set; }
        public decimal Amount { get; set; }
    }

    private sealed class InvoiceRm { public int Id { get; set; } public DateTime InvoiceDate { get; set; } }
    private sealed class InvoiceLineRm { public int Id { get; set; } public int InvoiceId { get; set; } public decimal Amount { get; set; } }
    private sealed class LineAllocationRm { public int Id { get; set; } public int InvoiceLineId { get; set; } public decimal Amount { get; set; } }
    private sealed class CustomerInvoiceRm { public int Id { get; set; } public int CustomerId { get; set; } public DateTime InvoiceDate { get; set; } }

    private sealed class OwnerOrder
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public DateTime CompletedAt { get; set; }
        public List<OwnerOrderItem> Items { get; set; } = [];
    }

    private sealed class OwnerOrderItem
    {
        public int Id { get; set; }
        public int OwnerOrderId { get; set; }
        public int OwnerId { get; set; }
        public OwnerOrder? Order { get; set; }
    }

    private sealed class OwnerOrderRm
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public DateTime CompletedAt { get; set; }
    }

    private sealed class OwnerOrderItemRm
    {
        public int Id { get; set; }
        public int OwnerOrderId { get; set; }
        public int OwnerId { get; set; }
    }

    private sealed class DateBucketRecord
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ReviewedAt { get; set; }
        public DateTime CompletedAt { get; set; }
    }

    private sealed class DateBucketRecordRm
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ReviewedAt { get; set; }
        public DateTime CompletedAt { get; set; }
    }

    private sealed class TypedPartitionRoot
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
        public int CustomerId { get; set; }
        public string Region { get; set; } = null!;
        public bool IsPriority { get; set; }
        public decimal AmountBand { get; set; }
        public DateTime SnapshotAt { get; set; }
        public DateOnly EffectiveDate { get; set; }
        public Guid TenantId { get; set; }
    }

    private sealed class TypedPartitionRm
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
        public int CustomerId { get; set; }
        public string Region { get; set; } = null!;
        public bool IsPriority { get; set; }
        public decimal AmountBand { get; set; }
        public DateTime SnapshotAt { get; set; }
        public DateOnly EffectiveDate { get; set; }
        public Guid TenantId { get; set; }
    }

    private interface IArchivePathContext
    {
        string ArchivePath { get; }
    }

    private sealed class InvoiceContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<InvoiceRm> InvoiceHistory => Set<InvoiceRm>();
        public DbSet<InvoiceLineRm> LineHistory => Set<InvoiceLineRm>();
        public DbSet<LineAllocationRm> AllocationHistory => Set<LineAllocationRm>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(b =>
            {
                b.ToTable("invoices");
                b.HasKey(i => i.Id);
                b.HasMany(i => i.Lines).WithOne(l => l.Invoice).HasForeignKey(l => l.InvoiceId);
            });
            modelBuilder.Entity<InvoiceLine>(b =>
            {
                b.ToTable("invoice_lines");
                b.HasKey(l => l.Id);
                b.HasMany(l => l.Allocations).WithOne(a => a.InvoiceLine).HasForeignKey(a => a.InvoiceLineId);
            });
            modelBuilder.Entity<LineAllocation>(b => { b.ToTable("line_allocations"); b.HasKey(a => a.Id); });

            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath, TierGranularity.Month)
                .WithReadModel<InvoiceRm>()
                .Including<InvoiceLine>(i => i.Lines, line => line
                    .WithReadModel<InvoiceLineRm>()
                    .Including<LineAllocation>(l => l.Allocations, alloc => alloc.WithReadModel<LineAllocationRm>()));
        }
    }

    private sealed class CustomerPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<CustomerInvoiceRm> InvoiceHistory => Set<CustomerInvoiceRm>();
        public DbSet<InvoiceLineRm> LineHistory => Set<InvoiceLineRm>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.HasMany(invoice => invoice.Lines).WithOne(line => line.Invoice).HasForeignKey(line => line.InvoiceId);
            });
            modelBuilder.Entity<InvoiceLine>(builder =>
            {
                builder.ToTable("invoice_lines");
                builder.HasKey(line => line.Id);
                builder.Ignore(line => line.Allocations);
            });

            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath, TierGranularity.Month)
                .PartitionBy(partitions => partitions
                    .By(invoice => invoice.CustomerId)
                    .ByMonth(invoice => invoice.InvoiceDate))
                .WithReadModel<CustomerInvoiceRm>()
                .Including<InvoiceLine>(invoice => invoice.Lines, line => line.WithReadModel<InvoiceLineRm>());
        }
    }

    private sealed class OwnerAliasPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<OwnerOrder> Orders => Set<OwnerOrder>();
        public DbSet<OwnerOrderItem> Items => Set<OwnerOrderItem>();
        public DbSet<OwnerOrderRm> OrderHistory => Set<OwnerOrderRm>();
        public DbSet<OwnerOrderItemRm> ItemHistory => Set<OwnerOrderItemRm>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => ConfigureOwnerAliasModel(modelBuilder, archivePath, "root_owner_id");
    }

    private sealed class ChangedOwnerAliasPartitionContext(
        string dbPath,
        string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => ConfigureOwnerAliasModel(modelBuilder, archivePath, "root_owner_key");
    }

    private sealed class ShorthandAliasPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
                .PartitionBy(invoice => invoice.CustomerId, "customer_key");
        }
    }

    private sealed class AliasedContractContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public string ArchivePath => archivePath + "|aliased-contract";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
                .PartitionBy(partitions => partitions
                    .By(invoice => invoice.CustomerId, "customer_key")
                    .ByMonth(invoice => invoice.InvoiceDate, "invoice_month"))
                .WithReadModel<InvoiceRm>();
        }
    }

    private sealed class MonthFirstPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
                .PartitionBy(partitions => partitions
                    .ByMonth(invoice => invoice.InvoiceDate)
                    .By(invoice => invoice.CustomerId));
        }
    }

    private sealed class DateBucketPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<DateBucketRecord> Records => Set<DateBucketRecord>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DateBucketRecord>(builder =>
            {
                builder.ToTable("date_bucket_records");
                builder.HasKey(record => record.Id);
            });
            modelBuilder.ToTieredStore<DateBucketRecord>(record => record.CompletedAt, archivePath, TierGranularity.Day)
                .PartitionBy(partitions => partitions
                    .By(record => record.CustomerId)
                    .ByYear(record => record.CreatedAt)
                    .ByMonth(record => record.ReviewedAt)
                    .ByDay(record => record.CompletedAt))
                .WithReadModel<DateBucketRecordRm>();
        }
    }

    private sealed class TypedPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<TypedPartitionRoot> Roots => Set<TypedPartitionRoot>();
        public DbSet<TypedPartitionRm> History => Set<TypedPartitionRm>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TypedPartitionRoot>(builder =>
            {
                builder.ToTable("typed_roots");
                builder.HasKey(root => root.Id);
                builder.Property(root => root.CustomerId).HasColumnName("customer_id");
                builder.Property(root => root.Region).HasColumnName("region_code");
                builder.Property(root => root.AmountBand).HasPrecision(10, 2);
            });
            modelBuilder.Entity<TypedPartitionRm>(builder =>
            {
                builder.Property(root => root.CustomerId).HasColumnName("customer_id");
                builder.Property(root => root.Region).HasColumnName("region_code");
                builder.Property(root => root.AmountBand).HasPrecision(10, 2);
            });

            modelBuilder.ToTieredStore<TypedPartitionRoot>(root => root.ArchivedAt, archivePath)
                .PartitionBy(root => root.CustomerId, root => root.Region)
                .PartitionBy(
                    root => root.IsPriority,
                    root => root.AmountBand,
                    root => root.SnapshotAt,
                    root => root.EffectiveDate,
                    root => root.TenantId)
                .WithReadModel<TypedPartitionRm>();
        }
    }

    private sealed class DayGranularityContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath, TierGranularity.Day)
                .WithReadModel<InvoiceRm>();
        }
    }

    private sealed class DuplicatePartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
                .PartitionBy(invoice => invoice.CustomerId)
                .PartitionBy(invoice => invoice.CustomerId);
        }
    }

    private sealed class ExactLifecyclePartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
                .PartitionBy(invoice => invoice.InvoiceDate);
        }
    }

    private sealed class RepeatedExactLifecyclePartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
                .PartitionBy(invoice => invoice.CustomerId)
                .PartitionBy(invoice => invoice.InvoiceDate);
        }
    }

    private sealed class MissingLifecyclePartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
                .PartitionBy(partitions => partitions.By(invoice => invoice.CustomerId));
        }
    }

    private sealed class NonDateTransformContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
                .PartitionBy(partitions => partitions
                    .ByMonth(invoice => invoice.CustomerId)
                    .ByMonth(invoice => invoice.InvoiceDate));
        }
    }

    private sealed class EmptyPartitionAliasContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
                .PartitionBy(partitions => partitions
                    .By(invoice => invoice.CustomerId, " ")
                    .ByMonth(invoice => invoice.InvoiceDate));
        }
    }

    private sealed class RootColumnAliasCollisionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
                .PartitionBy(partitions => partitions
                    .By(invoice => invoice.CustomerId, "Id")
                    .ByMonth(invoice => invoice.InvoiceDate));
        }
    }

    private sealed class DuplicatePartitionAliasContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
                .PartitionBy(partitions => partitions
                    .By(invoice => invoice.CustomerId, "bucket")
                    .ByMonth(invoice => invoice.InvoiceDate, "BUCKET"));
        }
    }

    private sealed class BadNavigationContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Invoice has no relationship to InvoiceLine, so the .Including navigation has no foreign key.
            modelBuilder.Entity<Invoice>(b => { b.ToTable("invoices"); b.HasKey(i => i.Id); b.Ignore(i => i.Lines); });
            modelBuilder.Entity<InvoiceLine>(b => { b.ToTable("invoice_lines"); b.HasKey(l => l.Id); b.Ignore(l => l.Allocations); });
            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath)
                .Including<InvoiceLine>(i => i.Lines);
        }
    }

    private sealed class BadChildPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.HasMany(invoice => invoice.Lines).WithOne(line => line.Invoice).HasForeignKey(line => line.InvoiceId);
            });
            modelBuilder.Entity<InvoiceLine>(builder =>
            {
                builder.ToTable("invoice_lines");
                builder.HasKey(line => line.Id);
                builder.Ignore(line => line.Allocations);
            });
            modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
                .Including<InvoiceLine>(invoice => invoice.Lines);

            // Simulates malformed metadata from a manually-authored convention or compiled model.
            modelBuilder.Entity<InvoiceLine>().HasAnnotation("DuckDB:TieredStore:PartitionProperties", "Amount");
        }
    }

    private sealed class BadReadModelContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(b => { b.ToTable("invoices"); b.HasKey(i => i.Id); b.Ignore(i => i.Lines); });
            // MismatchRm has a column the invoices table does not.
            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath).WithReadModel<MismatchRm>();
        }

        private sealed class MismatchRm { public int Id { get; set; } public DateTime InvoiceDate { get; set; } public string? Nonexistent { get; set; } }
    }

    private sealed class Ledger { public int Id { get; set; } public DateTime PostedAt { get; set; } }

    // Maps a property to the reserved hive partition column name "year".
    private sealed class ReservedColumnContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(b =>
            {
                b.ToTable("invoices");
                b.HasKey(i => i.Id);
                b.Ignore(i => i.Lines);
                b.Property(i => i.Id).HasColumnName("year");
            });
            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath);
        }
    }

    // Two aggregate roots sharing the same archive directory.
    private sealed class OverlapContext(string dbPath, string archiveRoot) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archiveRoot;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(b => { b.ToTable("invoices"); b.HasKey(i => i.Id); b.Ignore(i => i.Lines); });
            modelBuilder.Entity<Ledger>(b => { b.ToTable("ledger"); b.HasKey(l => l.Id); });
            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archiveRoot);
            modelBuilder.ToTieredStore<Ledger>(l => l.PostedAt, archiveRoot);
        }
    }

    // Second-generation root model over the same "invoices" table/archive, with an added column.
    private sealed class InvoiceV2
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public DateTime InvoiceDate { get; set; }
        public string? Note { get; set; }
        public List<InvoiceLineV2> Lines { get; set; } = [];
    }

    private sealed class InvoiceLineV2
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        public InvoiceV2? Invoice { get; set; }
        public decimal Amount { get; set; }
        public List<LineAllocationV2> Allocations { get; set; } = [];
    }

    private sealed class LineAllocationV2
    {
        public int Id { get; set; }
        public int InvoiceLineId { get; set; }
        public InvoiceLineV2? InvoiceLine { get; set; }
        public decimal Amount { get; set; }
    }

    private sealed class InvoiceV2Rm { public int Id { get; set; } public DateTime InvoiceDate { get; set; } public string? Note { get; set; } }

    private sealed class EvolvedContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<InvoiceV2Rm> InvoiceHistory => Set<InvoiceV2Rm>();
        public string ArchivePath => archivePath + "|evolved";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InvoiceV2>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.HasMany(invoice => invoice.Lines).WithOne(line => line.Invoice).HasForeignKey(line => line.InvoiceId);
            });
            modelBuilder.Entity<InvoiceLineV2>(builder =>
            {
                builder.ToTable("invoice_lines");
                builder.HasKey(line => line.Id);
                builder.HasMany(line => line.Allocations).WithOne(allocation => allocation.InvoiceLine)
                    .HasForeignKey(allocation => allocation.InvoiceLineId);
            });
            modelBuilder.Entity<LineAllocationV2>(builder =>
            {
                builder.ToTable("line_allocations");
                builder.HasKey(allocation => allocation.Id);
            });
            modelBuilder.ToTieredStore<InvoiceV2>(i => i.InvoiceDate, archivePath, TierGranularity.Month)
                .WithReadModel<InvoiceV2Rm>()
                .Including<InvoiceLineV2>(invoice => invoice.Lines, line => line
                    .Including<LineAllocationV2>(item => item.Allocations));
        }
    }

    private sealed class AliasedContractEvolvedContext(
        string dbPath,
        string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<InvoiceV2Rm> InvoiceHistory => Set<InvoiceV2Rm>();
        public string ArchivePath => archivePath + "|aliased-contract-evolved";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InvoiceV2>(builder =>
            {
                builder.ToTable("invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.ToTieredStore<InvoiceV2>(invoice => invoice.InvoiceDate, archivePath)
                .PartitionBy(partitions => partitions
                    .By(invoice => invoice.CustomerId, "customer_key")
                    .ByMonth(invoice => invoice.InvoiceDate, "invoice_month"))
                .WithReadModel<InvoiceV2Rm>();
        }
    }

    private sealed class SchemaContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<InvoiceRm> InvoiceHistory => Set<InvoiceRm>();
        public string ArchivePath => archivePath + "|schema";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}").ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(b =>
            {
                b.ToTable("invoices", "accounting");
                b.HasKey(i => i.Id);
                b.Ignore(i => i.Lines);
            });

            modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath, TierGranularity.Month)
                .WithReadModel<InvoiceRm>();
        }
    }

    private sealed class ArchivePathModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), (context as IArchivePathContext)?.ArchivePath, designTime);
    }

    private static void ConfigureOwnerAliasModel(
        ModelBuilder modelBuilder,
        string archivePath,
        string ownerPartitionName)
    {
        modelBuilder.Entity<OwnerOrder>(builder =>
        {
            builder.ToTable("owner_orders");
            builder.HasKey(order => order.Id);
            builder.HasMany(order => order.Items).WithOne(item => item.Order).HasForeignKey(item => item.OwnerOrderId);
        });
        modelBuilder.Entity<OwnerOrderItem>(builder =>
        {
            builder.ToTable("owner_order_items");
            builder.HasKey(item => item.Id);
        });

        modelBuilder.ToTieredStore<OwnerOrder>(order => order.CompletedAt, archivePath)
            .PartitionBy(partitions => partitions
                .By(order => order.OwnerId, ownerPartitionName)
                .ByMonth(order => order.CompletedAt, "completed_month"))
            .WithReadModel<OwnerOrderRm>()
            .Including<OwnerOrderItem>(
                order => order.Items,
                items => items.WithReadModel<OwnerOrderItemRm>());
    }
}
