using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;
using static Microsoft.EntityFrameworkCore.TieredStorageTestHelpers;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Generic tiered-view LINQ conformance coverage shared by the view-only/separate-context and legacy
///     read-model registration paths.
/// </summary>
public sealed class TieredViewQueryCompositionTests : IDisposable
{
    private static readonly DateTime ArchiveCutoff = new(2024, 4, 1);
    private static readonly DateTime RangeFrom = new(2024, 2, 1);
    private static readonly DateTime RangeTo = new(2024, 5, 1);
    private static readonly DateTime SharedTimestamp = new(2024, 2, 10, 12, 0, 0);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "duckdb-tier-query-composition-" + Guid.NewGuid().ToString("N"));

    public TieredViewQueryCompositionTests() => Directory.CreateDirectory(_root);

    public static TheoryData<TieredRegistration> RegistrationModes =>
    [
        TieredRegistration.TieredViewAndSeparateContext,
        TieredRegistration.ReadModel,
    ];

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

    [Theory]
    [MemberData(nameof(RegistrationModes))]
    public async Task Ordered_bounded_and_keyset_queries_remain_server_side_and_prune_files(
        TieredRegistration registration)
    {
        using var harness = await CreateHarnessAsync(registration, "pagination");
        const int ownerId = 10;
        const int pageSize = 2;

        var firstPageQuery = Scoped(harness.Invoices, ownerId, RangeFrom, RangeTo)
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id)
            .Take(pageSize + 1);
        var sql = firstPageQuery.ToQueryString();

        AssertPrunedOrderedLimitSql(sql, expectsContract: registration == TieredRegistration.TieredViewAndSeparateContext);
        AssertFilesPruned(Explain(harness.QueryContext, firstPageQuery), "2/6");
        var firstPage = firstPageQuery.ToArray();
        Assert.Equal([2, 3, 4], firstPage.Select(invoice => invoice.Id).ToArray());
        Assert.All(firstPage, invoice => Assert.Equal(ownerId, invoice.OwnerId));
        Assert.All(firstPage, invoice => Assert.InRange(invoice.CompletedAt, RangeFrom, RangeTo.AddTicks(-1)));
        Assert.DoesNotContain(firstPage, invoice => invoice.Id == 7);
        Assert.Equal(
            new[] { 2, 3, 4 },
            (await firstPageQuery.ToListAsync()).Select(invoice => invoice.Id).ToArray());

        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => firstPageQuery.ToListAsync(cancellation.Token));
        }

        var middlePage = KeysetPage(
            harness.Invoices,
            ownerId,
            RangeFrom,
            RangeTo,
            SharedTimestamp,
            cursorId: 3,
            pageSize + 1);
        AssertPrunedOrderedLimitSql(
            middlePage.ToQueryString(),
            expectsContract: registration == TieredRegistration.TieredViewAndSeparateContext);
        AssertFilesPruned(Explain(harness.QueryContext, middlePage), "2/6");
        Assert.Equal([4, 5, 6], middlePage.Select(invoice => invoice.Id).ToArray());

        var finalPage = KeysetPage(
            harness.Invoices,
            ownerId,
            RangeFrom,
            RangeTo,
            new DateTime(2024, 3, 15),
            cursorId: 5,
            pageSize + 1);
        Assert.Equal(new[] { 6 }, await finalPage.Select(invoice => invoice.Id).ToArrayAsync());

        var emptyPage = KeysetPage(
            harness.Invoices,
            ownerId,
            RangeFrom,
            RangeTo,
            new DateTime(2024, 4, 1),
            cursorId: 6,
            pageSize + 1);
        Assert.Empty(await emptyPage.ToArrayAsync());

        var offsetPage = Scoped(harness.Invoices, ownerId, RangeFrom, RangeTo)
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id)
            .Skip(2)
            .Take(2);
        var offsetSql = offsetPage.ToQueryString();
        Assert.Contains("OFFSET", offsetSql);
        Assert.Contains("LIMIT", offsetSql);
        Assert.Equal([4, 5], offsetPage.Select(invoice => invoice.Id).ToArray());

        var reversed = Scoped(harness.Invoices, ownerId, RangeFrom, RangeTo)
            .OrderByDescending(invoice => invoice.CompletedAt)
            .ThenByDescending(invoice => invoice.Id)
            .Take(2);
        Assert.Equal([6, 5], reversed.Select(invoice => invoice.Id).ToArray());

        var otherOwner = Scoped(harness.Invoices, 20, RangeFrom, RangeTo)
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id)
            .Take(pageSize + 1);
        Assert.Equal([21, 22, 23], otherOwner.Select(invoice => invoice.Id).ToArray());
        Assert.All(otherOwner, invoice => Assert.Equal(20, invoice.OwnerId));
    }

    [Theory]
    [MemberData(nameof(RegistrationModes))]
    public async Task Projection_and_terminal_operator_matrix_is_consistent(TieredRegistration registration)
    {
        using var harness = await CreateHarnessAsync(registration, "terminals");
        var scoped = Scoped(harness.Invoices, 10, RangeFrom, RangeTo);

        Assert.Equal(5, scoped.Count());
        Assert.Equal(5L, scoped.LongCount());
        Assert.True(scoped.Any(invoice => invoice.Status == "hot"));
        Assert.Equal(2, scoped.Min(invoice => invoice.Id));
        Assert.Equal(6, scoped.Max(invoice => invoice.Id));
        Assert.Equal(200m, scoped.Sum(invoice => invoice.Total));
        Assert.Equal(40m, scoped.Average(invoice => invoice.Total));
        Assert.Equal(3, scoped.Select(invoice => invoice.Status).Distinct().Count());

        Assert.Equal(5, await scoped.CountAsync());
        Assert.Equal(5L, await scoped.LongCountAsync());
        Assert.True(await scoped.AnyAsync(invoice => invoice.Status == "hot"));
        Assert.Equal(2, await scoped.MinAsync(invoice => invoice.Id));
        Assert.Equal(6, await scoped.MaxAsync(invoice => invoice.Id));
        Assert.Equal(200m, await scoped.SumAsync(invoice => invoice.Total));
        Assert.Equal(40m, await scoped.AverageAsync(invoice => invoice.Total));

        Assert.Equal(2, scoped.OrderBy(invoice => invoice.CompletedAt).ThenBy(invoice => invoice.Id).First().Id);
        Assert.Equal(
            2,
            (await scoped.OrderBy(invoice => invoice.CompletedAt).ThenBy(invoice => invoice.Id).FirstAsync()).Id);
        Assert.Null(scoped.FirstOrDefault(invoice => invoice.Id == 999));
        Assert.Null(await scoped.FirstOrDefaultAsync(invoice => invoice.Id == 999));
        Assert.Equal(3, scoped.Single(invoice => invoice.Id == 3).Id);
        Assert.Equal(3, (await scoped.SingleAsync(invoice => invoice.Id == 3)).Id);
        Assert.Null(scoped.SingleOrDefault(invoice => invoice.Id == 999));
        Assert.Null(await scoped.SingleOrDefaultAsync(invoice => invoice.Id == 999));

        var selectedKeys = new[] { 2, 6, 999 };
        Assert.Equal(
            [2, 6],
            scoped.Where(invoice => selectedKeys.Contains(invoice.Id))
                .OrderBy(invoice => invoice.Id)
                .Select(invoice => invoice.Id)
                .ToArray());

        var projectionBeforeOrdering = scoped
            .Select(invoice => new { invoice.Id, invoice.CompletedAt, invoice.Total })
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id)
            .Take(2);
        Assert.Equal([2, 3], projectionBeforeOrdering.Select(invoice => invoice.Id).ToArray());

        var projectionAfterOrdering = scoped
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id)
            .Select(invoice => new InvoiceSummary { Id = invoice.Id, Total = invoice.Total })
            .Take(2);
        var projected = await projectionAfterOrdering.ToArrayAsync();
        Assert.Equal([2, 3], projected.Select(invoice => invoice.Id).ToArray());
        Assert.Equal([20m, 30m], projected.Select(invoice => invoice.Total).ToArray());
    }

    [Theory]
    [MemberData(nameof(RegistrationModes))]
    public async Task Grouping_subqueries_and_post_projection_composition_translate(TieredRegistration registration)
    {
        using var harness = await CreateHarnessAsync(registration, "composition");

        var ownerGroups = harness.Invoices
            .Where(invoice => invoice.CompletedAt >= RangeFrom && invoice.CompletedAt < RangeTo)
            .GroupBy(invoice => invoice.OwnerId)
            .Select(group => new { OwnerId = group.Key, Count = group.Count(), Total = group.Sum(x => x.Total) })
            .OrderBy(group => group.OwnerId);
        var groups = await ownerGroups.ToArrayAsync();
        Assert.Equal([10, 20], groups.Select(group => group.OwnerId).ToArray());
        Assert.Equal([5, 3], groups.Select(group => group.Count).ToArray());

        var lifecycleGroups = Scoped(harness.Invoices, 10, RangeFrom, RangeTo)
            .GroupBy(invoice => invoice.CompletedAt.Month)
            .Select(group => new { Month = group.Key, Count = group.Count(), Total = group.Sum(x => x.Total) })
            .OrderBy(group => group.Month);
        Assert.Equal([2, 3, 4], lifecycleGroups.Select(group => group.Month).ToArray());
        Assert.Equal([3, 1, 1], lifecycleGroups.Select(group => group.Count).ToArray());

        var scopedIds = Scoped(harness.Invoices, 10, RangeFrom, RangeTo).Select(invoice => invoice.Id);
        var outerTieredQuery = harness.Invoices
            .Where(invoice => scopedIds.Contains(invoice.Id) && invoice.Total >= 40m)
            .OrderBy(invoice => invoice.Id);
        Assert.Equal([4, 5, 6], outerTieredQuery.Select(invoice => invoice.Id).ToArray());

        var innerTieredQuery =
            from invoice in Scoped(harness.Invoices, 10, RangeFrom, RangeTo)
            join candidate in harness.Invoices.Where(invoice => invoice.Status != "ignored")
                on invoice.Id equals candidate.Id
            orderby invoice.Id
            select invoice.Id;
        Assert.Equal([2, 3, 4, 5, 6], innerTieredQuery.ToArray());

        var afterSelect = Scoped(harness.Invoices, 10, RangeFrom, RangeTo)
            .Select(invoice => new InvoiceSummary { Id = invoice.Id, Total = invoice.Total })
            .Where(invoice => invoice.Total >= 40m)
            .OrderBy(invoice => invoice.Id);
        Assert.Equal([4, 5, 6], afterSelect.Select(invoice => invoice.Id).ToArray());

        var afterTake = Scoped(harness.Invoices, 10, RangeFrom, RangeTo)
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id)
            .Take(4)
            .Where(invoice => invoice.Id > 2);
        Assert.Equal(new[] { 3, 4, 5 }, await afterTake.Select(invoice => invoice.Id).ToArrayAsync());
    }

    [Theory]
    [MemberData(nameof(RegistrationModes))]
    public async Task Root_descendant_and_nested_descendant_joins_use_explicit_keys(
        TieredRegistration registration)
    {
        using var harness = await CreateHarnessAsync(registration, "relationships");
        var roots = Scoped(harness.Invoices, 10, RangeFrom, RangeTo);

        var rootToLine =
            from invoice in roots
            join line in harness.Lines on invoice.Id equals line.InvoiceId
            orderby invoice.Id, line.LineNumber
            select new { invoice.Id, line.LineNumber, line.Amount };
        var lineRows = rootToLine.ToArray();
        Assert.Equal(8, lineRows.Length);
        Assert.Equal([2, 2, 3, 3, 4, 4, 5, 5], lineRows.Select(row => row.Id).ToArray());

        var lineToRoot =
            from line in harness.Lines
            join invoice in roots on line.InvoiceId equals invoice.Id
            orderby line.InvoiceId, line.LineNumber
            select new { line.InvoiceId, invoice.OwnerId, line.Amount };
        Assert.Equal(lineRows.Length, await lineToRoot.CountAsync());
        Assert.All(await lineToRoot.ToArrayAsync(), row => Assert.Equal(10, row.OwnerId));

        var nested =
            from invoice in roots
            join line in harness.Lines on invoice.Id equals line.InvoiceId
            join adjustment in harness.Adjustments
                on new { line.InvoiceId, line.LineNumber }
                equals new { adjustment.InvoiceId, adjustment.LineNumber }
            orderby invoice.Id
            select new { invoice.Id, adjustment.Code, adjustment.Amount };
        Assert.Equal([2, 3, 4, 5], nested.Select(row => row.Id).ToArray());

        var leftJoin =
            from invoice in roots
            join line in harness.Lines on invoice.Id equals line.InvoiceId into lines
            from line in lines.DefaultIfEmpty()
            orderby invoice.Id, line.LineNumber
            select new { invoice.Id, LineNumber = (int?)line.LineNumber };
        var leftRows = leftJoin.ToArray();
        Assert.Equal(9, leftRows.Length);
        Assert.Contains(leftRows, row => row.Id == 6 && row.LineNumber == null);
    }

    [Fact]
    public async Task Owner_and_day_partitioning_prune_across_a_year_boundary()
    {
        var dbPath = Path.Combine(_root, "daily.duckdb");
        var archivePath = Path.Combine(_root, "daily archive with spaces");
        using var context = new DailyContext(dbPath, archivePath);
        context.Database.EnsureCreated();
        context.Records.AddRange(
            new DailyRecord { Id = 1, OwnerId = 10, CompletedOn = new DateTime(2023, 12, 31, 12, 0, 0) },
            new DailyRecord { Id = 2, OwnerId = 10, CompletedOn = new DateTime(2024, 1, 1) },
            new DailyRecord { Id = 3, OwnerId = 20, CompletedOn = new DateTime(2023, 12, 31, 12, 0, 0) },
            new DailyRecord { Id = 4, OwnerId = 20, CompletedOn = new DateTime(2024, 1, 1) });
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<DailyRecord>(new DateTime(2024, 1, 2));

        var from = new DateTime(2023, 12, 31);
        var to = new DateTime(2024, 1, 2);
        var query = context.History
            .Where(record => record.OwnerId == 10 && record.CompletedOn >= from && record.CompletedOn < to)
            .OrderBy(record => record.CompletedOn)
            .ThenBy(record => record.Id)
            .Take(3);
        var sql = query.ToQueryString();

        Assert.Contains("owner_key", sql);
        Assert.Contains("completed_day", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("LIMIT", sql);
        AssertFilesPruned(Explain(context, query), "2/4");
        Assert.Equal([1, 2], query.Select(record => record.Id).ToArray());
    }

    [Fact]
    public async Task Nullable_exact_partition_and_date_only_lifecycle_prune_without_hiding_null_lifecycle_rows()
    {
        var dbPath = Path.Combine(_root, "nullable-date-only.duckdb");
        var archivePath = Path.Combine(_root, "nullable-date-only-archive");
        using var context = new NullableDateOnlyContext(dbPath, archivePath);
        context.Database.EnsureCreated();
        context.Records.AddRange(
            new NullableDateOnlyRecord { Id = 1, OwnerId = null, CompletedOn = new DateOnly(2024, 1, 15) },
            new NullableDateOnlyRecord { Id = 2, OwnerId = 10, CompletedOn = new DateOnly(2024, 1, 15) },
            new NullableDateOnlyRecord { Id = 3, OwnerId = null, CompletedOn = null });
        context.SaveChanges();

        await context.Database.ArchiveTierAsync<NullableDateOnlyRecord>(new DateTime(2024, 2, 1));

        Assert.Single(context.Records);
        Assert.Null(context.Records.Single().CompletedOn);
        Assert.Equal(3, context.History.Count());

        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 2, 1);
        var query = context.History
            .Where(record => record.OwnerId == null && record.CompletedOn >= from && record.CompletedOn < to)
            .OrderBy(record => record.CompletedOn)
            .ThenBy(record => record.Id)
            .Take(2);
        var sql = query.ToQueryString();

        Assert.Contains("owner_key", sql);
        Assert.Contains("completed_month", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("LIMIT", sql);
        AssertFilesPruned(Explain(context, query), "1/2");
        Assert.Equal(new[] { 1 }, await query.Select(record => record.Id).ToArrayAsync());
    }

    [Fact]
    public async Task Context_order_model_cache_and_concurrent_readers_do_not_change_results()
    {
        var dbPath = Path.Combine(_root, "context-order.duckdb");
        var archivePath = Path.Combine(_root, "context-order-archive");

        using var readerCreatedFirst = new EntityHistoryContext<ContextOrderMarker>(dbPath);
        _ = readerCreatedFirst.Model;

        using var owner = new ViewOwnerContext<ContextOrderMarker>(dbPath, archivePath);
        owner.Database.EnsureCreated();
        Seed(owner);
        await owner.Database.ArchiveTierAsync<Invoice>(ArchiveCutoff);

        Assert.Equal(archivePath, owner.Model.FindEntityType(typeof(Invoice))!.GetTieredStoreArchivePath());
        Assert.Equal(
            [2, 3, 4, 5, 6],
            Scoped(Project(readerCreatedFirst.Invoices), 10, RangeFrom, RangeTo)
                .OrderBy(invoice => invoice.CompletedAt)
                .ThenBy(invoice => invoice.Id)
                .Select(invoice => invoice.Id)
                .ToArray());

        var compiledPage = EF.CompileQuery(
            (EntityHistoryContext<ContextOrderMarker> context, int ownerId, DateTime from, DateTime to) =>
                context.Invoices.AsNoTracking()
                    .Where(invoice =>
                        invoice.OwnerId == ownerId
                        && invoice.CompletedAt >= from
                        && invoice.CompletedAt < to)
                    .OrderBy(invoice => invoice.CompletedAt)
                    .ThenBy(invoice => invoice.Id)
                    .Take(3)
                    .Select(invoice => invoice.Id));
        Assert.Equal([2, 3, 4], compiledPage(readerCreatedFirst, 10, RangeFrom, RangeTo).ToArray());
        Assert.Equal([21, 22, 23], compiledPage(readerCreatedFirst, 20, RangeFrom, RangeTo).ToArray());

        using var secondReader = new EntityHistoryContext<ContextOrderMarker>(dbPath);
        var firstTask = Scoped(Project(readerCreatedFirst.Invoices), 10, RangeFrom, RangeTo)
            .OrderBy(invoice => invoice.Id)
            .ToArrayAsync();
        var secondTask = Scoped(Project(secondReader.Invoices), 20, RangeFrom, RangeTo)
            .OrderBy(invoice => invoice.Id)
            .ToArrayAsync();
        await Task.WhenAll(firstTask, secondTask);
        Assert.Equal([2, 3, 4, 5, 6], firstTask.Result.Select(invoice => invoice.Id).ToArray());
        Assert.Equal([21, 22, 23], secondTask.Result.Select(invoice => invoice.Id).ToArray());

        var secondDbPath = Path.Combine(_root, "context-order-second.duckdb");
        var secondArchivePath = Path.Combine(_root, "context-order-second-archive");
        using var secondOwner = new ViewOwnerContext<ContextOrderMarker>(secondDbPath, secondArchivePath);
        Assert.Equal(
            secondArchivePath,
            secondOwner.Model.FindEntityType(typeof(Invoice))!.GetTieredStoreArchivePath());
    }

    [Theory]
    [MemberData(nameof(RegistrationModes))]
    public async Task Cold_hot_spanning_and_empty_ranges_keep_expected_pruning(TieredRegistration registration)
    {
        using var harness = await CreateHarnessAsync(registration, "range-variants");

        var coldOnly = Scoped(
                harness.Invoices,
                10,
                new DateTime(2024, 2, 1),
                new DateTime(2024, 4, 1))
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id)
            .Take(10);
        AssertFilesPruned(Explain(harness.QueryContext, coldOnly), "2/6");
        Assert.Equal([2, 3, 4, 5], coldOnly.Select(invoice => invoice.Id).ToArray());

        var spanning = Scoped(
                harness.Invoices,
                10,
                new DateTime(2024, 3, 1),
                new DateTime(2024, 5, 1))
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id)
            .Take(10);
        AssertFilesPruned(Explain(harness.QueryContext, spanning), "1/6");
        Assert.Equal([5, 6], spanning.Select(invoice => invoice.Id).ToArray());

        var hotOnly = Scoped(
                harness.Invoices,
                10,
                new DateTime(2024, 4, 1),
                new DateTime(2024, 5, 1))
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id)
            .Take(10);
        Assert.Equal(new[] { 6 }, await hotOnly.Select(invoice => invoice.Id).ToArrayAsync());
        Assert.Contains("LIMIT", hotOnly.ToQueryString());

        var noColdPartition = Scoped(
                harness.Invoices,
                10,
                new DateTime(2024, 6, 1),
                new DateTime(2024, 7, 1))
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id)
            .Take(10);
        Assert.Empty(noColdPartition);

        var unlimited = Scoped(harness.Invoices, 10, RangeFrom, RangeTo)
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id);
        Assert.DoesNotContain("LIMIT", unlimited.ToQueryString());
        Assert.Equal([2, 3, 4, 5, 6], unlimited.Select(invoice => invoice.Id).ToArray());
    }

    [Fact]
    public async Task Principal_query_remains_valid_across_archive_reconcile_restore_and_compact()
    {
        var dbPath = Path.Combine(_root, "lifecycle.duckdb");
        var archivePath = Path.Combine(_root, "lifecycle-archive");

        using (var context = new ReadModelOwnerContext<LifecycleMarker>(dbPath, archivePath))
        {
            context.Database.EnsureCreated();
            Seed(context);
            AssertPrincipalQuery(context, [2, 3, 4, 5, 6], expectedFileFraction: null);

            var firstArchive = await context.Database.ArchiveTierAsync<Invoice>(ArchiveCutoff);
            Assert.False(firstArchive.NoOp);
            AssertPrincipalQuery(context, [2, 3, 4, 5, 6], "2/6");

            var noOp = await context.Database.ArchiveTierAsync<Invoice>(ArchiveCutoff);
            Assert.True(noOp.NoOp);
            AssertPrincipalQuery(context, [2, 3, 4, 5, 6], "2/6");
        }

        using var restarted = new ReadModelOwnerContext<LifecycleMarker>(dbPath, archivePath);
        restarted.Database.EnsureTieredStoresCreated();
        AssertPrincipalQuery(restarted, [2, 3, 4, 5, 6], "2/6");

        restarted.Invoices.Add(CreateInvoice(8, 10, new DateTime(2024, 2, 20), "late", 80m));
        restarted.Invoices.Add(CreateInvoice(9, 10, new DateTime(2024, 4, 15), "hot", 90m, withLines: false));
        restarted.Invoices.Add(new Invoice
        {
            Id = 30,
            ExternalId = "invoice-3",
            OwnerId = 10,
            CompletedAt = SharedTimestamp,
            Status = "corrected",
            Total = 300m,
        });
        restarted.SaveChanges();
        AssertPrincipalQuery(restarted, [2, 3, 4, 8, 5, 6, 9], "2/6");

        var reconciliation = await restarted.Database.ReconcileArchiveTierAsync<Invoice>();
        Assert.False(reconciliation.NoOp);
        AssertPrincipalQuery(restarted, [2, 4, 30, 8, 5, 6, 9], "2/6");

        var restoration = await restarted.Database.RestoreArchiveTierAsync<Invoice>(
            new TierRestoreOptions
            {
                Scope = TierMaintenanceScope.ForRootMatchKeys(
                    TierRowIdentity.For<Invoice>(
                        new Dictionary<string, object?>
                        {
                            [nameof(Invoice.ExternalId)] = "invoice-2",
                        })),
            });
        Assert.Equal(TierArchiveOperation.Restore, restoration.Publication.Operation);
        AssertPrincipalQuery(restarted, [2, 4, 30, 8, 5, 6, 9], "2/6");

        var compaction = await restarted.Database.CompactArchiveTierAsync<Invoice>();
        Assert.Equal(TierArchiveOperation.Compact, compaction.Operation);
        AssertPrincipalQuery(restarted, [2, 4, 30, 8, 5, 6, 9], "2/6");
        Assert.Equal(
            restarted.InvoiceHistory.Count(),
            restarted.InvoiceHistory.Select(invoice => invoice.ExternalId).Distinct().Count());
    }

    [Fact]
    public async Task Shared_descendant_view_keeps_deterministic_root_scoped_bindings_with_take()
    {
        var dbPath = Path.Combine(_root, "shared-descendant.duckdb");
        var archivePath = Path.Combine(_root, "shared-descendant-archive");
        using (var owner = new SharedOwnerContext<SharedQueryMarker>(dbPath, archivePath))
        {
            owner.Database.EnsureCreated();
            owner.RootAs.Add(new SharedRootA
            {
                Id = 1,
                CompletedAt = new DateTime(2024, 1, 10),
                Charges = [new SharedCharge { Id = 101, Value = "a" }],
            });
            owner.RootBs.Add(new SharedRootB
            {
                Id = 2,
                CompletedAt = new DateTime(2024, 1, 11),
                Charges = [new SharedCharge { Id = 202, Value = "b" }],
            });
            owner.SaveChanges();

            var rootB = await owner.Database.ArchiveTierAsync<SharedRootB>(new DateTime(2024, 2, 1));
            var rootA = await owner.Database.ArchiveTierAsync<SharedRootA>(new DateTime(2024, 2, 1));
            Assert.Equal("composition-shared-b", rootB.Binding?.ControlKey);
            Assert.Equal("composition-shared-a", rootA.Binding?.ControlKey);
            Assert.Empty(owner.Charges);
        }

        using var history = new SharedHistoryContext(dbPath);
        var query = history.Charges.OrderBy(charge => charge.Id).Take(3);
        var sql = query.ToQueryString();
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("LIMIT", sql);
        Assert.Equal([101, 202], query.Select(charge => charge.Id).ToArray());
    }

    private async Task<QueryHarness> CreateHarnessAsync(TieredRegistration registration, string suffix)
    {
        var dbPath = Path.Combine(_root, $"{registration}-{suffix}.duckdb");
        var archivePath = Path.Combine(_root, $"{registration}-{suffix}-archive");

        if (registration == TieredRegistration.TieredViewAndSeparateContext)
        {
            var owner = new ViewOwnerContext<ViewOwnerMarker>(dbPath, archivePath);
            owner.Database.EnsureCreated();
            Seed(owner);
            await owner.Database.ArchiveTierAsync<Invoice>(ArchiveCutoff);
            var history = new EntityHistoryContext<ViewOwnerMarker>(dbPath);
            return new QueryHarness(
                owner,
                history,
                Project(history.Invoices),
                Project(history.Lines),
                Project(history.Adjustments));
        }

        var readModelOwner = new ReadModelOwnerContext<ReadModelMarker>(dbPath, archivePath);
        readModelOwner.Database.EnsureCreated();
        Seed(readModelOwner);
        await readModelOwner.Database.ArchiveTierAsync<Invoice>(ArchiveCutoff);
        return new QueryHarness(
            readModelOwner,
            readModelOwner,
            Project(readModelOwner.InvoiceHistory),
            Project(readModelOwner.LineHistory),
            Project(readModelOwner.AdjustmentHistory));
    }

    private static IQueryable<InvoiceQueryRow> Scoped(
        IQueryable<InvoiceQueryRow> source,
        int ownerId,
        DateTime from,
        DateTime to)
        => source.Where(invoice =>
            invoice.OwnerId == ownerId
            && invoice.CompletedAt >= from
            && invoice.CompletedAt < to);

    private static IQueryable<InvoiceQueryRow> KeysetPage(
        IQueryable<InvoiceQueryRow> source,
        int ownerId,
        DateTime from,
        DateTime to,
        DateTime cursorCompletedAt,
        int cursorId,
        int take)
        => Scoped(source, ownerId, from, to)
            .Where(invoice =>
                invoice.CompletedAt > cursorCompletedAt
                || invoice.CompletedAt == cursorCompletedAt && invoice.Id > cursorId)
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id)
            .Take(take);

    private static void AssertPrunedOrderedLimitSql(string sql, bool expectsContract)
    {
        var ownerPartitionIndex = sql.LastIndexOf("root_owner_key", StringComparison.Ordinal);
        var monthPartitionIndex = sql.LastIndexOf("completed_month", StringComparison.Ordinal);
        var orderIndex = sql.LastIndexOf("ORDER BY", StringComparison.Ordinal);
        var limitIndex = sql.LastIndexOf("LIMIT", StringComparison.Ordinal);

        Assert.True(ownerPartitionIndex >= 0, sql);
        Assert.True(monthPartitionIndex > ownerPartitionIndex, sql);
        Assert.True(orderIndex > monthPartitionIndex, sql);
        Assert.True(limitIndex > orderIndex, sql);
        Assert.Contains("$ownerId", sql);
        Assert.Contains("$from", sql);
        Assert.Contains("$to", sql);
        Assert.Contains("CompletedAt", sql);
        Assert.Contains("Id", sql);
        Assert.Equal(1, CountOccurrences(sql, "root_owner_key"));
        Assert.Equal(2, CountOccurrences(sql, "completed_month"));
        if (expectsContract)
        {
            Assert.Contains(DuckDBTierPartitionContract.ColumnPrefix, sql);
        }
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static void AssertPrincipalQuery(
        ReadModelOwnerContext<LifecycleMarker> context,
        int[] expectedIds,
        string? expectedFileFraction)
    {
        var query = Scoped(Project(context.InvoiceHistory), 10, RangeFrom, RangeTo)
            .OrderBy(invoice => invoice.CompletedAt)
            .ThenBy(invoice => invoice.Id)
            .Take(20);
        Assert.Contains("ORDER BY", query.ToQueryString());
        Assert.Contains("LIMIT", query.ToQueryString());
        if (expectedFileFraction is not null)
        {
            AssertFilesPruned(Explain(context, query), expectedFileFraction);
        }

        Assert.Equal(expectedIds, query.Select(invoice => invoice.Id).ToArray());
    }

    private static IQueryable<InvoiceQueryRow> Project(IQueryable<Invoice> source)
        => source.AsNoTracking().Select(invoice => new InvoiceQueryRow
        {
            Id = invoice.Id,
            ExternalId = invoice.ExternalId,
            OwnerId = invoice.OwnerId,
            CompletedAt = invoice.CompletedAt,
            Status = invoice.Status,
            Total = invoice.Total,
        });

    private static IQueryable<InvoiceQueryRow> Project(IQueryable<InvoiceHistory> source)
        => source.AsNoTracking().Select(invoice => new InvoiceQueryRow
        {
            Id = invoice.Id,
            ExternalId = invoice.ExternalId,
            OwnerId = invoice.OwnerId,
            CompletedAt = invoice.CompletedAt,
            Status = invoice.Status,
            Total = invoice.Total,
        });

    private static IQueryable<LineQueryRow> Project(IQueryable<InvoiceLine> source)
        => source.AsNoTracking().Select(line => new LineQueryRow
        {
            InvoiceId = line.InvoiceId,
            LineNumber = line.LineNumber,
            OwnerId = line.OwnerId,
            Description = line.Description,
            Amount = line.Amount,
        });

    private static IQueryable<LineQueryRow> Project(IQueryable<InvoiceLineHistory> source)
        => source.AsNoTracking().Select(line => new LineQueryRow
        {
            InvoiceId = line.InvoiceId,
            LineNumber = line.LineNumber,
            OwnerId = line.OwnerId,
            Description = line.Description,
            Amount = line.Amount,
        });

    private static IQueryable<AdjustmentQueryRow> Project(IQueryable<InvoiceLineAdjustment> source)
        => source.AsNoTracking().Select(adjustment => new AdjustmentQueryRow
        {
            InvoiceId = adjustment.InvoiceId,
            LineNumber = adjustment.LineNumber,
            Code = adjustment.Code,
            Amount = adjustment.Amount,
        });

    private static IQueryable<AdjustmentQueryRow> Project(IQueryable<InvoiceLineAdjustmentHistory> source)
        => source.AsNoTracking().Select(adjustment => new AdjustmentQueryRow
        {
            InvoiceId = adjustment.InvoiceId,
            LineNumber = adjustment.LineNumber,
            Code = adjustment.Code,
            Amount = adjustment.Amount,
        });

    private static void Seed(OwnerContext owner)
    {
        owner.Invoices.AddRange(
            CreateInvoice(1, 10, new DateTime(2024, 1, 15), "paid", 10m),
            CreateInvoice(2, 10, RangeFrom, "paid", 20m),
            CreateInvoice(3, 10, SharedTimestamp, "paid", 30m),
            CreateInvoice(4, 10, SharedTimestamp, "adjusted", 40m),
            CreateInvoice(5, 10, new DateTime(2024, 3, 15), "adjusted", 50m),
            CreateInvoice(6, 10, ArchiveCutoff, "hot", 60m, withLines: false),
            CreateInvoice(7, 10, RangeTo, "hot", 70m),
            CreateInvoice(20, 20, new DateTime(2024, 1, 15), "paid", 200m),
            CreateInvoice(21, 20, RangeFrom, "paid", 210m),
            CreateInvoice(22, 20, new DateTime(2024, 3, 15), "adjusted", 220m),
            CreateInvoice(23, 20, ArchiveCutoff, "hot", 230m));
        owner.SaveChanges();
    }

    private static Invoice CreateInvoice(
        int id,
        int ownerId,
        DateTime completedAt,
        string status,
        decimal total,
        bool withLines = true)
    {
        var invoice = new Invoice
        {
            Id = id,
            ExternalId = $"invoice-{id}",
            OwnerId = ownerId,
            CompletedAt = completedAt,
            Status = status,
            Total = total,
        };
        if (!withLines)
        {
            return invoice;
        }

        invoice.Lines.Add(new InvoiceLine
        {
            InvoiceId = id,
            LineNumber = 1,
            OwnerId = ownerId + 1_000,
            Description = "service",
            Amount = total,
            Adjustments =
            [
                new InvoiceLineAdjustment
                {
                    InvoiceId = id,
                    LineNumber = 1,
                    Code = "tax",
                    Amount = total / 10m,
                },
            ],
        });
        invoice.Lines.Add(new InvoiceLine
        {
            InvoiceId = id,
            LineNumber = 2,
            OwnerId = ownerId + 1_000,
            Description = "fee",
            Amount = 1m,
        });
        return invoice;
    }

    private static void ConfigureInvoicePartitions(TieredPartitionBuilder<Invoice> partitions)
        => partitions
            .By(invoice => invoice.OwnerId, "root_owner_key")
            .ByMonth(invoice => invoice.CompletedAt, "completed_month");

    private static void ConfigureHistoryPartitions(TieredPartitionBuilder<InvoiceHistory> partitions)
        => partitions
            .By(invoice => invoice.OwnerId, "root_owner_key")
            .ByMonth(invoice => invoice.CompletedAt, "completed_month");

    public enum TieredRegistration
    {
        TieredViewAndSeparateContext,
        ReadModel,
    }

    private sealed class InvoiceSummary
    {
        public int Id { get; init; }
        public decimal Total { get; init; }
    }

    private sealed class InvoiceQueryRow
    {
        public int Id { get; init; }
        public string ExternalId { get; init; } = null!;
        public int OwnerId { get; init; }
        public DateTime CompletedAt { get; init; }
        public string Status { get; init; } = null!;
        public decimal Total { get; init; }
    }

    private sealed class LineQueryRow
    {
        public int InvoiceId { get; init; }
        public int LineNumber { get; init; }
        public int OwnerId { get; init; }
        public string Description { get; init; } = null!;
        public decimal Amount { get; init; }
    }

    private sealed class AdjustmentQueryRow
    {
        public int InvoiceId { get; init; }
        public int LineNumber { get; init; }
        public string Code { get; init; } = null!;
        public decimal Amount { get; init; }
    }

    private sealed class QueryHarness : IDisposable
    {
        private readonly OwnerContext _owner;

        public QueryHarness(
            OwnerContext owner,
            DbContext queryContext,
            IQueryable<InvoiceQueryRow> invoices,
            IQueryable<LineQueryRow> lines,
            IQueryable<AdjustmentQueryRow> adjustments)
        {
            _owner = owner;
            QueryContext = queryContext;
            Invoices = invoices;
            Lines = lines;
            Adjustments = adjustments;
        }

        public DbContext QueryContext { get; }
        public IQueryable<InvoiceQueryRow> Invoices { get; }
        public IQueryable<LineQueryRow> Lines { get; }
        public IQueryable<AdjustmentQueryRow> Adjustments { get; }

        public void Dispose()
        {
            if (!ReferenceEquals(_owner, QueryContext))
            {
                QueryContext.Dispose();
            }

            _owner.Dispose();
        }
    }

    private interface ITieredModelKey
    {
        string ModelKey { get; }
    }

    private sealed class TieredModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is ITieredModelKey keyed
                ? (context.GetType(), keyed.ModelKey, designTime)
                : (object)(context.GetType(), designTime);
    }

    private abstract class OwnerContext(string dbPath, string archivePath) : DbContext, ITieredModelKey
    {
        protected string ArchivePath { get; } = archivePath;
        public string ModelKey => ArchivePath;
        public DbSet<Invoice> Invoices => Set<Invoice>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, TieredModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTable("composition_invoices");
                builder.HasKey(invoice => invoice.Id);
                builder.HasIndex(invoice => invoice.ExternalId).IsUnique();
                builder.HasMany(invoice => invoice.Lines)
                    .WithOne(line => line.Invoice)
                    .HasForeignKey(line => line.InvoiceId);
            });
            modelBuilder.Entity<InvoiceLine>(builder =>
            {
                builder.ToTable("composition_invoice_lines");
                builder.HasKey(line => new { line.InvoiceId, line.LineNumber });
                builder.HasMany(line => line.Adjustments)
                    .WithOne(adjustment => adjustment.Line)
                    .HasForeignKey(adjustment => new { adjustment.InvoiceId, adjustment.LineNumber });
            });
            modelBuilder.Entity<InvoiceLineAdjustment>(builder =>
            {
                builder.ToTable("composition_invoice_adjustments");
                builder.HasKey(adjustment => new
                {
                    adjustment.InvoiceId,
                    adjustment.LineNumber,
                    adjustment.Code,
                });
            });
            ConfigureTieredStore(modelBuilder);
        }

        protected abstract void ConfigureTieredStore(ModelBuilder modelBuilder);
    }

    private sealed class ViewOwnerContext<TMarker>(string dbPath, string archivePath)
        : OwnerContext(dbPath, archivePath)
    {
        protected override void ConfigureTieredStore(ModelBuilder modelBuilder)
            => modelBuilder.ToTieredStore<Invoice>(invoice => invoice.CompletedAt, ArchivePath)
                .MatchBy(invoice => invoice.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                .PartitionBy(ConfigureInvoicePartitions)
                .WithTieredView("composition_invoice_history")
                .Including<InvoiceLine>(invoice => invoice.Lines, line => line
                    .WithTieredView()
                    .Including<InvoiceLineAdjustment>(
                        item => item.Adjustments,
                        adjustment => adjustment.WithTieredView("composition_adjustment_history")));
    }

    private sealed class ReadModelOwnerContext<TMarker>(string dbPath, string archivePath)
        : OwnerContext(dbPath, archivePath)
    {
        public DbSet<InvoiceHistory> InvoiceHistory => Set<InvoiceHistory>();
        public DbSet<InvoiceLineHistory> LineHistory => Set<InvoiceLineHistory>();
        public DbSet<InvoiceLineAdjustmentHistory> AdjustmentHistory => Set<InvoiceLineAdjustmentHistory>();

        protected override void ConfigureTieredStore(ModelBuilder modelBuilder)
            => modelBuilder.ToTieredStore<Invoice>(invoice => invoice.CompletedAt, ArchivePath)
                .MatchBy(invoice => invoice.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                .PartitionBy(ConfigureInvoicePartitions)
                .WithReadModel<InvoiceHistory>()
                .Including<InvoiceLine>(invoice => invoice.Lines, line => line
                    .WithReadModel<InvoiceLineHistory>()
                    .Including<InvoiceLineAdjustment>(
                        item => item.Adjustments,
                        adjustment => adjustment.WithReadModel<InvoiceLineAdjustmentHistory>()));
    }

    private sealed class EntityHistoryContext<TMarker>(string dbPath) : DbContext
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<InvoiceLine> Lines => Set<InvoiceLine>();
        public DbSet<InvoiceLineAdjustment> Adjustments => Set<InvoiceLineAdjustment>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Invoice>(builder =>
            {
                builder.ToTieredView("composition_invoice_history", ConfigureInvoicePartitions);
                builder.Ignore(invoice => invoice.Lines);
            });
            modelBuilder.Entity<InvoiceLine>(builder =>
            {
                builder.HasNoKey();
                builder.ToView("composition_invoice_lines_tiered");
                builder.Ignore(line => line.Invoice);
                builder.Ignore(line => line.Adjustments);
            });
            modelBuilder.Entity<InvoiceLineAdjustment>(builder =>
            {
                builder.HasNoKey();
                builder.ToView("composition_adjustment_history");
                builder.Ignore(adjustment => adjustment.Line);
            });
        }
    }

    private sealed class DailyContext(string dbPath, string archivePath) : DbContext, ITieredModelKey
    {
        public string ModelKey => archivePath;
        public DbSet<DailyRecord> Records => Set<DailyRecord>();
        public DbSet<DailyRecordHistory> History => Set<DailyRecordHistory>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, TieredModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DailyRecord>(builder =>
            {
                builder.ToTable("daily_records");
                builder.HasKey(record => record.Id);
            });
            modelBuilder.ToTieredStore<DailyRecord>(
                    record => record.CompletedOn,
                    archivePath,
                    TierGranularity.Day)
                .PartitionBy(partitions => partitions
                    .By(record => record.OwnerId, "owner_key")
                    .ByDay(record => record.CompletedOn, "completed_day"))
                .WithReadModel<DailyRecordHistory>();
        }
    }

    private sealed class NullableDateOnlyContext(string dbPath, string archivePath) : DbContext, ITieredModelKey
    {
        public string ModelKey => archivePath;
        public DbSet<NullableDateOnlyRecord> Records => Set<NullableDateOnlyRecord>();
        public DbSet<NullableDateOnlyRecordHistory> History => Set<NullableDateOnlyRecordHistory>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, TieredModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NullableDateOnlyRecord>(builder =>
            {
                builder.ToTable("nullable_date_only_records");
                builder.HasKey(record => record.Id);
            });
            modelBuilder.ToTieredStore<NullableDateOnlyRecord>(record => record.CompletedOn, archivePath)
                .PartitionBy(partitions => partitions
                    .By(record => record.OwnerId, "owner_key")
                    .ByMonth(record => record.CompletedOn, "completed_month"))
                .WithReadModel<NullableDateOnlyRecordHistory>();
        }
    }

    private sealed class SharedOwnerContext<TMarker>(string dbPath, string archivePath)
        : DbContext, ITieredModelKey
    {
        public string ModelKey => archivePath;
        public DbSet<SharedRootA> RootAs => Set<SharedRootA>();
        public DbSet<SharedRootB> RootBs => Set<SharedRootB>();
        public DbSet<SharedCharge> Charges => Set<SharedCharge>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, TieredModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SharedRootA>(builder =>
            {
                builder.ToTable("composition_shared_root_a");
                builder.HasKey(root => root.Id);
                builder.HasMany(root => root.Charges)
                    .WithOne(charge => charge.RootA)
                    .HasForeignKey(charge => charge.RootAId);
            });
            modelBuilder.Entity<SharedRootB>(builder =>
            {
                builder.ToTable("composition_shared_root_b");
                builder.HasKey(root => root.Id);
                builder.HasMany(root => root.Charges)
                    .WithOne(charge => charge.RootB)
                    .HasForeignKey(charge => charge.RootBId);
            });
            modelBuilder.Entity<SharedCharge>(builder =>
            {
                builder.ToTable("composition_shared_charges");
                builder.HasKey(charge => charge.Id);
            });
            modelBuilder.ToTieredStore<SharedRootB>(
                    root => root.CompletedAt,
                    archivePath + "/b",
                    controlKey: "composition-shared-b")
                .WithTieredView("composition_shared_root_b_history")
                .Including<SharedCharge>(root => root.Charges, charge => charge.WithTieredView());
            modelBuilder.ToTieredStore<SharedRootA>(
                    root => root.CompletedAt,
                    archivePath + "/a",
                    controlKey: "composition-shared-a")
                .WithTieredView("composition_shared_root_a_history")
                .Including<SharedCharge>(root => root.Charges, charge => charge.WithTieredView());
        }
    }

    private sealed class SharedHistoryContext(string dbPath) : DbContext
    {
        public DbSet<SharedCharge> Charges => Set<SharedCharge>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<SharedCharge>(builder =>
            {
                builder.HasNoKey();
                builder.ToView("composition_shared_charges_tiered");
                builder.Ignore(charge => charge.RootA);
                builder.Ignore(charge => charge.RootB);
            });
    }

    private sealed class Invoice
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public int OwnerId { get; set; }
        public DateTime CompletedAt { get; set; }
        public string Status { get; set; } = null!;
        public decimal Total { get; set; }
        public List<InvoiceLine> Lines { get; set; } = [];
    }

    private sealed class InvoiceLine
    {
        public int InvoiceId { get; set; }
        public int LineNumber { get; set; }
        public int OwnerId { get; set; }
        public string Description { get; set; } = null!;
        public decimal Amount { get; set; }
        public Invoice? Invoice { get; set; }
        public List<InvoiceLineAdjustment> Adjustments { get; set; } = [];
    }

    private sealed class InvoiceLineAdjustment
    {
        public int InvoiceId { get; set; }
        public int LineNumber { get; set; }
        public string Code { get; set; } = null!;
        public decimal Amount { get; set; }
        public InvoiceLine? Line { get; set; }
    }

    private sealed class InvoiceHistory
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public int OwnerId { get; set; }
        public DateTime CompletedAt { get; set; }
        public string Status { get; set; } = null!;
        public decimal Total { get; set; }
    }

    private sealed class InvoiceLineHistory
    {
        public int InvoiceId { get; set; }
        public int LineNumber { get; set; }
        public int OwnerId { get; set; }
        public string Description { get; set; } = null!;
        public decimal Amount { get; set; }
    }

    private sealed class InvoiceLineAdjustmentHistory
    {
        public int InvoiceId { get; set; }
        public int LineNumber { get; set; }
        public string Code { get; set; } = null!;
        public decimal Amount { get; set; }
    }

    private sealed class DailyRecord
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public DateTime CompletedOn { get; set; }
    }

    private sealed class DailyRecordHistory
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public DateTime CompletedOn { get; set; }
    }

    private sealed class NullableDateOnlyRecord
    {
        public int Id { get; set; }
        public int? OwnerId { get; set; }
        public DateOnly? CompletedOn { get; set; }
    }

    private sealed class NullableDateOnlyRecordHistory
    {
        public int Id { get; set; }
        public int? OwnerId { get; set; }
        public DateOnly? CompletedOn { get; set; }
    }

    private sealed class SharedRootA
    {
        public int Id { get; set; }
        public DateTime CompletedAt { get; set; }
        public List<SharedCharge> Charges { get; set; } = [];
    }

    private sealed class SharedRootB
    {
        public int Id { get; set; }
        public DateTime CompletedAt { get; set; }
        public List<SharedCharge> Charges { get; set; } = [];
    }

    private sealed class SharedCharge
    {
        public int Id { get; set; }
        public int? RootAId { get; set; }
        public SharedRootA? RootA { get; set; }
        public int? RootBId { get; set; }
        public SharedRootB? RootB { get; set; }
        public string Value { get; set; } = null!;
    }

    private sealed class ViewOwnerMarker;
    private sealed class ReadModelMarker;
    private sealed class ContextOrderMarker;
    private sealed class LifecycleMarker;
    private sealed class SharedQueryMarker;
}
