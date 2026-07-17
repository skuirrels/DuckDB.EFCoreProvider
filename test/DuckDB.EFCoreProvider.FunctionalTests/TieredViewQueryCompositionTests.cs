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
        const int groupId = 10;
        const int pageSize = 2;

        var firstPageQuery = Scoped(harness.Records, groupId, RangeFrom, RangeTo)
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id)
            .Take(pageSize + 1);
        var sql = firstPageQuery.ToQueryString();

        AssertPrunedOrderedLimitSql(sql, expectsContract: registration == TieredRegistration.TieredViewAndSeparateContext);
        AssertFilesPruned(Explain(harness.QueryContext, firstPageQuery), "2/6");
        var firstPage = firstPageQuery.ToArray();
        Assert.Equal([2, 3, 4], firstPage.Select(record => record.Id).ToArray());
        Assert.All(firstPage, record => Assert.Equal(groupId, record.GroupId));
        Assert.All(firstPage, record => Assert.InRange(record.EffectiveAt, RangeFrom, RangeTo.AddTicks(-1)));
        Assert.DoesNotContain(firstPage, record => record.Id == 7);
        Assert.Equal(
            new[] { 2, 3, 4 },
            (await firstPageQuery.ToListAsync()).Select(record => record.Id).ToArray());

        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => firstPageQuery.ToListAsync(cancellation.Token));
        }

        var middlePage = KeysetPage(
            harness.Records,
            groupId,
            RangeFrom,
            RangeTo,
            SharedTimestamp,
            cursorId: 3,
            pageSize + 1);
        AssertPrunedOrderedLimitSql(
            middlePage.ToQueryString(),
            expectsContract: registration == TieredRegistration.TieredViewAndSeparateContext);
        AssertFilesPruned(Explain(harness.QueryContext, middlePage), "2/6");
        Assert.Equal([4, 5, 6], middlePage.Select(record => record.Id).ToArray());

        var finalPage = KeysetPage(
            harness.Records,
            groupId,
            RangeFrom,
            RangeTo,
            new DateTime(2024, 3, 15),
            cursorId: 5,
            pageSize + 1);
        Assert.Equal(new[] { 6 }, await finalPage.Select(record => record.Id).ToArrayAsync());

        var emptyPage = KeysetPage(
            harness.Records,
            groupId,
            RangeFrom,
            RangeTo,
            new DateTime(2024, 4, 1),
            cursorId: 6,
            pageSize + 1);
        Assert.Empty(await emptyPage.ToArrayAsync());

        var offsetPage = Scoped(harness.Records, groupId, RangeFrom, RangeTo)
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id)
            .Skip(2)
            .Take(2);
        var offsetSql = offsetPage.ToQueryString();
        Assert.Contains("OFFSET", offsetSql);
        Assert.Contains("LIMIT", offsetSql);
        Assert.Equal([4, 5], offsetPage.Select(record => record.Id).ToArray());

        var reversed = Scoped(harness.Records, groupId, RangeFrom, RangeTo)
            .OrderByDescending(record => record.EffectiveAt)
            .ThenByDescending(record => record.Id)
            .Take(2);
        Assert.Equal([6, 5], reversed.Select(record => record.Id).ToArray());

        var otherGroup = Scoped(harness.Records, 20, RangeFrom, RangeTo)
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id)
            .Take(pageSize + 1);
        Assert.Equal([21, 22, 23], otherGroup.Select(record => record.Id).ToArray());
        Assert.All(otherGroup, record => Assert.Equal(20, record.GroupId));
    }

    [Theory]
    [MemberData(nameof(RegistrationModes))]
    public async Task Projection_and_terminal_operator_matrix_is_consistent(TieredRegistration registration)
    {
        using var harness = await CreateHarnessAsync(registration, "terminals");
        var scoped = Scoped(harness.Records, 10, RangeFrom, RangeTo);

        Assert.Equal(5, scoped.Count());
        Assert.Equal(5L, scoped.LongCount());
        Assert.True(scoped.Any(record => record.Status == "hot"));
        Assert.Equal(2, scoped.Min(record => record.Id));
        Assert.Equal(6, scoped.Max(record => record.Id));
        Assert.Equal(200m, scoped.Sum(record => record.Metric));
        Assert.Equal(40m, scoped.Average(record => record.Metric));
        Assert.Equal(3, scoped.Select(record => record.Status).Distinct().Count());

        Assert.Equal(5, await scoped.CountAsync());
        Assert.Equal(5L, await scoped.LongCountAsync());
        Assert.True(await scoped.AnyAsync(record => record.Status == "hot"));
        Assert.Equal(2, await scoped.MinAsync(record => record.Id));
        Assert.Equal(6, await scoped.MaxAsync(record => record.Id));
        Assert.Equal(200m, await scoped.SumAsync(record => record.Metric));
        Assert.Equal(40m, await scoped.AverageAsync(record => record.Metric));

        Assert.Equal(2, scoped.OrderBy(record => record.EffectiveAt).ThenBy(record => record.Id).First().Id);
        Assert.Equal(
            2,
            (await scoped.OrderBy(record => record.EffectiveAt).ThenBy(record => record.Id).FirstAsync()).Id);
        Assert.Null(scoped.FirstOrDefault(record => record.Id == 999));
        Assert.Null(await scoped.FirstOrDefaultAsync(record => record.Id == 999));
        Assert.Equal(3, scoped.Single(record => record.Id == 3).Id);
        Assert.Equal(3, (await scoped.SingleAsync(record => record.Id == 3)).Id);
        Assert.Null(scoped.SingleOrDefault(record => record.Id == 999));
        Assert.Null(await scoped.SingleOrDefaultAsync(record => record.Id == 999));

        var selectedKeys = new[] { 2, 6, 999 };
        Assert.Equal(
            [2, 6],
            scoped.Where(record => selectedKeys.Contains(record.Id))
                .OrderBy(record => record.Id)
                .Select(record => record.Id)
                .ToArray());

        var projectionBeforeOrdering = scoped
            .Select(record => new { record.Id, record.EffectiveAt, record.Metric })
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id)
            .Take(2);
        Assert.Equal([2, 3], projectionBeforeOrdering.Select(record => record.Id).ToArray());

        var projectionAfterOrdering = scoped
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id)
            .Select(record => new RecordSummary { Id = record.Id, Metric = record.Metric })
            .Take(2);
        var projected = await projectionAfterOrdering.ToArrayAsync();
        Assert.Equal([2, 3], projected.Select(record => record.Id).ToArray());
        Assert.Equal([20m, 30m], projected.Select(record => record.Metric).ToArray());
    }

    [Theory]
    [MemberData(nameof(RegistrationModes))]
    public async Task Grouping_subqueries_and_post_projection_composition_translate(TieredRegistration registration)
    {
        using var harness = await CreateHarnessAsync(registration, "composition");

        var groupAggregates = harness.Records
            .Where(record => record.EffectiveAt >= RangeFrom && record.EffectiveAt < RangeTo)
            .GroupBy(record => record.GroupId)
            .Select(group => new { GroupId = group.Key, Count = group.Count(), Metric = group.Sum(x => x.Metric) })
            .OrderBy(group => group.GroupId);
        var groups = await groupAggregates.ToArrayAsync();
        Assert.Equal([10, 20], groups.Select(group => group.GroupId).ToArray());
        Assert.Equal([5, 3], groups.Select(group => group.Count).ToArray());

        var lifecycleGroups = Scoped(harness.Records, 10, RangeFrom, RangeTo)
            .GroupBy(record => record.EffectiveAt.Month)
            .Select(group => new { Month = group.Key, Count = group.Count(), Metric = group.Sum(x => x.Metric) })
            .OrderBy(group => group.Month);
        Assert.Equal([2, 3, 4], lifecycleGroups.Select(group => group.Month).ToArray());
        Assert.Equal([3, 1, 1], lifecycleGroups.Select(group => group.Count).ToArray());

        var scopedIds = Scoped(harness.Records, 10, RangeFrom, RangeTo).Select(record => record.Id);
        var outerTieredQuery = harness.Records
            .Where(record => scopedIds.Contains(record.Id) && record.Metric >= 40m)
            .OrderBy(record => record.Id);
        Assert.Equal([4, 5, 6], outerTieredQuery.Select(record => record.Id).ToArray());

        var innerTieredQuery =
            from record in Scoped(harness.Records, 10, RangeFrom, RangeTo)
            join candidate in harness.Records.Where(record => record.Status != "ignored")
                on record.Id equals candidate.Id
            orderby record.Id
            select record.Id;
        Assert.Equal([2, 3, 4, 5, 6], innerTieredQuery.ToArray());

        var afterSelect = Scoped(harness.Records, 10, RangeFrom, RangeTo)
            .Select(record => new RecordSummary { Id = record.Id, Metric = record.Metric })
            .Where(record => record.Metric >= 40m)
            .OrderBy(record => record.Id);
        Assert.Equal([4, 5, 6], afterSelect.Select(record => record.Id).ToArray());

        var afterTake = Scoped(harness.Records, 10, RangeFrom, RangeTo)
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id)
            .Take(4)
            .Where(record => record.Id > 2);
        Assert.Equal(new[] { 3, 4, 5 }, await afterTake.Select(record => record.Id).ToArrayAsync());
    }

    [Theory]
    [MemberData(nameof(RegistrationModes))]
    public async Task Root_descendant_and_nested_descendant_joins_use_explicit_keys(
        TieredRegistration registration)
    {
        using var harness = await CreateHarnessAsync(registration, "relationships");
        var roots = Scoped(harness.Records, 10, RangeFrom, RangeTo);

        var rootToLine =
            from record in roots
            join part in harness.Parts on record.Id equals part.RecordId
            orderby record.Id, part.PartNumber
            select new { record.Id, part.PartNumber, part.Value };
        var lineRows = rootToLine.ToArray();
        Assert.Equal(8, lineRows.Length);
        Assert.Equal([2, 2, 3, 3, 4, 4, 5, 5], lineRows.Select(row => row.Id).ToArray());

        var partToRoot =
            from part in harness.Parts
            join record in roots on part.RecordId equals record.Id
            orderby part.RecordId, part.PartNumber
            select new { part.RecordId, record.GroupId, part.Value };
        Assert.Equal(lineRows.Length, await partToRoot.CountAsync());
        Assert.All(await partToRoot.ToArrayAsync(), row => Assert.Equal(10, row.GroupId));

        var nested =
            from record in roots
            join part in harness.Parts on record.Id equals part.RecordId
            join detail in harness.Details
                on new { part.RecordId, part.PartNumber }
                equals new { detail.RecordId, detail.PartNumber }
            orderby record.Id
            select new { record.Id, detail.Code, detail.Value };
        Assert.Equal([2, 3, 4, 5], nested.Select(row => row.Id).ToArray());

        var leftJoin =
            from record in roots
            join part in harness.Parts on record.Id equals part.RecordId into parts
            from part in parts.DefaultIfEmpty()
            orderby record.Id, part.PartNumber
            select new { record.Id, PartNumber = (int?)part.PartNumber };
        var leftRows = leftJoin.ToArray();
        Assert.Equal(9, leftRows.Length);
        Assert.Contains(leftRows, row => row.Id == 6 && row.PartNumber == null);
    }

    [Fact]
    public async Task Group_and_day_partitioning_prune_across_a_year_boundary()
    {
        var dbPath = Path.Combine(_root, "daily.duckdb");
        var archivePath = Path.Combine(_root, "daily archive with spaces");
        using var context = new DailyContext(dbPath, archivePath);
        context.Database.EnsureCreated();
        context.Records.AddRange(
            new DailyRecord { Id = 1, GroupId = 10, EffectiveOn = new DateTime(2023, 12, 31, 12, 0, 0) },
            new DailyRecord { Id = 2, GroupId = 10, EffectiveOn = new DateTime(2024, 1, 1) },
            new DailyRecord { Id = 3, GroupId = 20, EffectiveOn = new DateTime(2023, 12, 31, 12, 0, 0) },
            new DailyRecord { Id = 4, GroupId = 20, EffectiveOn = new DateTime(2024, 1, 1) });
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<DailyRecord>(new DateTime(2024, 1, 2));

        var from = new DateTime(2023, 12, 31);
        var to = new DateTime(2024, 1, 2);
        var query = context.History
            .Where(record => record.GroupId == 10 && record.EffectiveOn >= from && record.EffectiveOn < to)
            .OrderBy(record => record.EffectiveOn)
            .ThenBy(record => record.Id)
            .Take(3);
        var sql = query.ToQueryString();

        Assert.Contains("group_key", sql);
        Assert.Contains("effective_day", sql);
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
            new NullableDateOnlyRecord { Id = 1, GroupId = null, EffectiveOn = new DateOnly(2024, 1, 15) },
            new NullableDateOnlyRecord { Id = 2, GroupId = 10, EffectiveOn = new DateOnly(2024, 1, 15) },
            new NullableDateOnlyRecord { Id = 3, GroupId = null, EffectiveOn = null });
        context.SaveChanges();

        await context.Database.ArchiveTierAsync<NullableDateOnlyRecord>(new DateTime(2024, 2, 1));

        Assert.Single(context.Records);
        Assert.Null(context.Records.Single().EffectiveOn);
        Assert.Equal(3, context.History.Count());

        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 2, 1);
        var query = context.History
            .Where(record => record.GroupId == null && record.EffectiveOn >= from && record.EffectiveOn < to)
            .OrderBy(record => record.EffectiveOn)
            .ThenBy(record => record.Id)
            .Take(2);
        var sql = query.ToQueryString();

        Assert.Contains("group_key", sql);
        Assert.Contains("effective_month", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("LIMIT", sql);
        AssertFilesPruned(Explain(context, query), "1/2");
        Assert.Equal(new[] { 1 }, await query.Select(record => record.Id).ToArrayAsync());
    }

    [Fact]
    public async Task Context_order_model_cache_and_concurrent_readers_do_not_change_results()
    {
        var dbPath = Path.Combine(_root, "context-record.duckdb");
        var archivePath = Path.Combine(_root, "context-record-archive");

        using var readerCreatedFirst = new EntityHistoryContext<ContextRecordMarker>(dbPath);
        _ = readerCreatedFirst.Model;

        using var owner = new ViewOwnerContext<ContextRecordMarker>(dbPath, archivePath);
        owner.Database.EnsureCreated();
        Seed(owner);
        await owner.Database.ArchiveTierAsync<Record>(ArchiveCutoff);

        Assert.Equal(archivePath, owner.Model.FindEntityType(typeof(Record))!.GetTieredStoreArchivePath());
        Assert.Equal(
            [2, 3, 4, 5, 6],
            Scoped(Project(readerCreatedFirst.Records), 10, RangeFrom, RangeTo)
                .OrderBy(record => record.EffectiveAt)
                .ThenBy(record => record.Id)
                .Select(record => record.Id)
                .ToArray());

        var compiledPage = EF.CompileQuery(
            (EntityHistoryContext<ContextRecordMarker> context, int groupId, DateTime from, DateTime to) =>
                context.Records.AsNoTracking()
                    .Where(record =>
                        record.GroupId == groupId
                        && record.EffectiveAt >= from
                        && record.EffectiveAt < to)
                    .OrderBy(record => record.EffectiveAt)
                    .ThenBy(record => record.Id)
                    .Take(3)
                    .Select(record => record.Id));
        Assert.Equal([2, 3, 4], compiledPage(readerCreatedFirst, 10, RangeFrom, RangeTo).ToArray());
        Assert.Equal([21, 22, 23], compiledPage(readerCreatedFirst, 20, RangeFrom, RangeTo).ToArray());

        using var secondReader = new EntityHistoryContext<ContextRecordMarker>(dbPath);
        var firstTask = Scoped(Project(readerCreatedFirst.Records), 10, RangeFrom, RangeTo)
            .OrderBy(record => record.Id)
            .ToArrayAsync();
        var secondTask = Scoped(Project(secondReader.Records), 20, RangeFrom, RangeTo)
            .OrderBy(record => record.Id)
            .ToArrayAsync();
        await Task.WhenAll(firstTask, secondTask);
        Assert.Equal([2, 3, 4, 5, 6], firstTask.Result.Select(record => record.Id).ToArray());
        Assert.Equal([21, 22, 23], secondTask.Result.Select(record => record.Id).ToArray());

        var secondDbPath = Path.Combine(_root, "context-record-second.duckdb");
        var secondArchivePath = Path.Combine(_root, "context-record-second-archive");
        using var secondOwner = new ViewOwnerContext<ContextRecordMarker>(secondDbPath, secondArchivePath);
        Assert.Equal(
            secondArchivePath,
            secondOwner.Model.FindEntityType(typeof(Record))!.GetTieredStoreArchivePath());
    }

    [Theory]
    [MemberData(nameof(RegistrationModes))]
    public async Task Cold_hot_spanning_and_empty_ranges_keep_expected_pruning(TieredRegistration registration)
    {
        using var harness = await CreateHarnessAsync(registration, "range-variants");

        var coldOnly = Scoped(
                harness.Records,
                10,
                new DateTime(2024, 2, 1),
                new DateTime(2024, 4, 1))
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id)
            .Take(10);
        AssertFilesPruned(Explain(harness.QueryContext, coldOnly), "2/6");
        Assert.Equal([2, 3, 4, 5], coldOnly.Select(record => record.Id).ToArray());

        var spanning = Scoped(
                harness.Records,
                10,
                new DateTime(2024, 3, 1),
                new DateTime(2024, 5, 1))
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id)
            .Take(10);
        AssertFilesPruned(Explain(harness.QueryContext, spanning), "1/6");
        Assert.Equal([5, 6], spanning.Select(record => record.Id).ToArray());

        var hotOnly = Scoped(
                harness.Records,
                10,
                new DateTime(2024, 4, 1),
                new DateTime(2024, 5, 1))
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id)
            .Take(10);
        Assert.Equal(new[] { 6 }, await hotOnly.Select(record => record.Id).ToArrayAsync());
        Assert.Contains("LIMIT", hotOnly.ToQueryString());

        var noColdPartition = Scoped(
                harness.Records,
                10,
                new DateTime(2024, 6, 1),
                new DateTime(2024, 7, 1))
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id)
            .Take(10);
        Assert.Empty(noColdPartition);

        var unlimited = Scoped(harness.Records, 10, RangeFrom, RangeTo)
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id);
        Assert.DoesNotContain("LIMIT", unlimited.ToQueryString());
        Assert.Equal([2, 3, 4, 5, 6], unlimited.Select(record => record.Id).ToArray());
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

            var firstArchive = await context.Database.ArchiveTierAsync<Record>(ArchiveCutoff);
            Assert.False(firstArchive.NoOp);
            AssertPrincipalQuery(context, [2, 3, 4, 5, 6], "2/6");

            var noOp = await context.Database.ArchiveTierAsync<Record>(ArchiveCutoff);
            Assert.True(noOp.NoOp);
            AssertPrincipalQuery(context, [2, 3, 4, 5, 6], "2/6");
        }

        using var restarted = new ReadModelOwnerContext<LifecycleMarker>(dbPath, archivePath);
        restarted.Database.EnsureTieredStoresCreated();
        AssertPrincipalQuery(restarted, [2, 3, 4, 5, 6], "2/6");

        restarted.Records.Add(CreateRecord(8, 10, new DateTime(2024, 2, 20), "late", 80m));
        restarted.Records.Add(CreateRecord(9, 10, new DateTime(2024, 4, 15), "hot", 90m, withParts: false));
        restarted.Records.Add(new Record
        {
            Id = 30,
            ExternalId = "record-3",
            GroupId = 10,
            EffectiveAt = SharedTimestamp,
            Status = "corrected",
            Metric = 300m,
        });
        restarted.SaveChanges();
        AssertPrincipalQuery(restarted, [2, 3, 4, 8, 5, 6, 9], "2/6");

        var reconciliation = await restarted.Database.ReconcileArchiveTierAsync<Record>();
        Assert.False(reconciliation.NoOp);
        AssertPrincipalQuery(restarted, [2, 4, 30, 8, 5, 6, 9], "2/6");

        var restoration = await restarted.Database.RestoreArchiveTierAsync<Record>(
            new TierRestoreOptions
            {
                Scope = TierMaintenanceScope.ForRootMatchKeys(
                    TierRowIdentity.For<Record>(
                        new Dictionary<string, object?>
                        {
                            [nameof(Record.ExternalId)] = "record-2",
                        })),
            });
        Assert.Equal(TierArchiveOperation.Restore, restoration.Publication.Operation);
        AssertPrincipalQuery(restarted, [2, 4, 30, 8, 5, 6, 9], "2/6");

        var compaction = await restarted.Database.CompactArchiveTierAsync<Record>();
        Assert.Equal(TierArchiveOperation.Compact, compaction.Operation);
        AssertPrincipalQuery(restarted, [2, 4, 30, 8, 5, 6, 9], "2/6");
        Assert.Equal(
            restarted.RecordHistory.Count(),
            restarted.RecordHistory.Select(record => record.ExternalId).Distinct().Count());
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
                EffectiveAt = new DateTime(2024, 1, 10),
                Tags = [new SharedTag { Id = 101, Value = "a" }],
            });
            owner.RootBs.Add(new SharedRootB
            {
                Id = 2,
                EffectiveAt = new DateTime(2024, 1, 11),
                Tags = [new SharedTag { Id = 202, Value = "b" }],
            });
            owner.SaveChanges();

            var rootB = await owner.Database.ArchiveTierAsync<SharedRootB>(new DateTime(2024, 2, 1));
            var rootA = await owner.Database.ArchiveTierAsync<SharedRootA>(new DateTime(2024, 2, 1));
            Assert.Equal("composition-shared-b", rootB.Binding?.ControlKey);
            Assert.Equal("composition-shared-a", rootA.Binding?.ControlKey);
            Assert.Empty(owner.Tags);
        }

        using var history = new SharedHistoryContext(dbPath);
        var query = history.Tags.OrderBy(tag => tag.Id).Take(3);
        var sql = query.ToQueryString();
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("LIMIT", sql);
        Assert.Equal([101, 202], query.Select(tag => tag.Id).ToArray());
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
            await owner.Database.ArchiveTierAsync<Record>(ArchiveCutoff);
            var history = new EntityHistoryContext<ViewOwnerMarker>(dbPath);
            return new QueryHarness(
                owner,
                history,
                Project(history.Records),
                Project(history.Parts),
                Project(history.Details));
        }

        var readModelOwner = new ReadModelOwnerContext<ReadModelMarker>(dbPath, archivePath);
        readModelOwner.Database.EnsureCreated();
        Seed(readModelOwner);
        await readModelOwner.Database.ArchiveTierAsync<Record>(ArchiveCutoff);
        return new QueryHarness(
            readModelOwner,
            readModelOwner,
            Project(readModelOwner.RecordHistory),
            Project(readModelOwner.PartHistory),
            Project(readModelOwner.DetailHistory));
    }

    private static IQueryable<RecordQueryRow> Scoped(
        IQueryable<RecordQueryRow> source,
        int groupId,
        DateTime from,
        DateTime to)
        => source.Where(record =>
            record.GroupId == groupId
            && record.EffectiveAt >= from
            && record.EffectiveAt < to);

    private static IQueryable<RecordQueryRow> KeysetPage(
        IQueryable<RecordQueryRow> source,
        int groupId,
        DateTime from,
        DateTime to,
        DateTime cursorEffectiveAt,
        int cursorId,
        int take)
        => Scoped(source, groupId, from, to)
            .Where(record =>
                record.EffectiveAt > cursorEffectiveAt
                || record.EffectiveAt == cursorEffectiveAt && record.Id > cursorId)
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id)
            .Take(take);

    private static void AssertPrunedOrderedLimitSql(string sql, bool expectsContract)
    {
        var ownerPartitionIndex = sql.LastIndexOf("root_group_key", StringComparison.Ordinal);
        var monthPartitionIndex = sql.LastIndexOf("effective_month", StringComparison.Ordinal);
        var orderIndex = sql.LastIndexOf("ORDER BY", StringComparison.Ordinal);
        var limitIndex = sql.LastIndexOf("LIMIT", StringComparison.Ordinal);

        Assert.True(ownerPartitionIndex >= 0, sql);
        Assert.True(monthPartitionIndex > ownerPartitionIndex, sql);
        Assert.True(orderIndex > monthPartitionIndex, sql);
        Assert.True(limitIndex > orderIndex, sql);
        Assert.Contains("$groupId", sql);
        Assert.Contains("$from", sql);
        Assert.Contains("$to", sql);
        Assert.Contains("EffectiveAt", sql);
        Assert.Contains("Id", sql);
        Assert.Equal(1, CountOccurrences(sql, "root_group_key"));
        Assert.Equal(2, CountOccurrences(sql, "effective_month"));
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
        var query = Scoped(Project(context.RecordHistory), 10, RangeFrom, RangeTo)
            .OrderBy(record => record.EffectiveAt)
            .ThenBy(record => record.Id)
            .Take(20);
        Assert.Contains("ORDER BY", query.ToQueryString());
        Assert.Contains("LIMIT", query.ToQueryString());
        if (expectedFileFraction is not null)
        {
            AssertFilesPruned(Explain(context, query), expectedFileFraction);
        }

        Assert.Equal(expectedIds, query.Select(record => record.Id).ToArray());
    }

    private static IQueryable<RecordQueryRow> Project(IQueryable<Record> source)
        => source.AsNoTracking().Select(record => new RecordQueryRow
        {
            Id = record.Id,
            ExternalId = record.ExternalId,
            GroupId = record.GroupId,
            EffectiveAt = record.EffectiveAt,
            Status = record.Status,
            Metric = record.Metric,
        });

    private static IQueryable<RecordQueryRow> Project(IQueryable<RecordHistory> source)
        => source.AsNoTracking().Select(record => new RecordQueryRow
        {
            Id = record.Id,
            ExternalId = record.ExternalId,
            GroupId = record.GroupId,
            EffectiveAt = record.EffectiveAt,
            Status = record.Status,
            Metric = record.Metric,
        });

    private static IQueryable<PartQueryRow> Project(IQueryable<RecordPart> source)
        => source.AsNoTracking().Select(part => new PartQueryRow
        {
            RecordId = part.RecordId,
            PartNumber = part.PartNumber,
            GroupId = part.GroupId,
            Description = part.Description,
            Value = part.Value,
        });

    private static IQueryable<PartQueryRow> Project(IQueryable<RecordPartHistory> source)
        => source.AsNoTracking().Select(part => new PartQueryRow
        {
            RecordId = part.RecordId,
            PartNumber = part.PartNumber,
            GroupId = part.GroupId,
            Description = part.Description,
            Value = part.Value,
        });

    private static IQueryable<DetailQueryRow> Project(IQueryable<RecordPartDetail> source)
        => source.AsNoTracking().Select(detail => new DetailQueryRow
        {
            RecordId = detail.RecordId,
            PartNumber = detail.PartNumber,
            Code = detail.Code,
            Value = detail.Value,
        });

    private static IQueryable<DetailQueryRow> Project(IQueryable<RecordPartDetailHistory> source)
        => source.AsNoTracking().Select(detail => new DetailQueryRow
        {
            RecordId = detail.RecordId,
            PartNumber = detail.PartNumber,
            Code = detail.Code,
            Value = detail.Value,
        });

    private static void Seed(OwnerContext owner)
    {
        owner.Records.AddRange(
            CreateRecord(1, 10, new DateTime(2024, 1, 15), "paid", 10m),
            CreateRecord(2, 10, RangeFrom, "paid", 20m),
            CreateRecord(3, 10, SharedTimestamp, "paid", 30m),
            CreateRecord(4, 10, SharedTimestamp, "updated", 40m),
            CreateRecord(5, 10, new DateTime(2024, 3, 15), "updated", 50m),
            CreateRecord(6, 10, ArchiveCutoff, "hot", 60m, withParts: false),
            CreateRecord(7, 10, RangeTo, "hot", 70m),
            CreateRecord(20, 20, new DateTime(2024, 1, 15), "paid", 200m),
            CreateRecord(21, 20, RangeFrom, "paid", 210m),
            CreateRecord(22, 20, new DateTime(2024, 3, 15), "updated", 220m),
            CreateRecord(23, 20, ArchiveCutoff, "hot", 230m));
        owner.SaveChanges();
    }

    private static Record CreateRecord(
        int id,
        int groupId,
        DateTime effectiveAt,
        string status,
        decimal metric,
        bool withParts = true)
    {
        var record = new Record
        {
            Id = id,
            ExternalId = $"record-{id}",
            GroupId = groupId,
            EffectiveAt = effectiveAt,
            Status = status,
            Metric = metric,
        };
        if (!withParts)
        {
            return record;
        }

        record.Parts.Add(new RecordPart
        {
            RecordId = id,
            PartNumber = 1,
            GroupId = groupId + 1_000,
            Description = "service",
            Value = metric,
            Details =
            [
                new RecordPartDetail
                {
                    RecordId = id,
                    PartNumber = 1,
                    Code = "tax",
                    Value = metric / 10m,
                },
            ],
        });
        record.Parts.Add(new RecordPart
        {
            RecordId = id,
            PartNumber = 2,
            GroupId = groupId + 1_000,
            Description = "fee",
            Value = 1m,
        });
        return record;
    }

    private static void ConfigureRecordPartitions(TieredPartitionBuilder<Record> partitions)
        => partitions
            .By(record => record.GroupId, "root_group_key")
            .ByMonth(record => record.EffectiveAt, "effective_month");

    private static void ConfigureHistoryPartitions(TieredPartitionBuilder<RecordHistory> partitions)
        => partitions
            .By(record => record.GroupId, "root_group_key")
            .ByMonth(record => record.EffectiveAt, "effective_month");

    public enum TieredRegistration
    {
        TieredViewAndSeparateContext,
        ReadModel,
    }

    private sealed class RecordSummary
    {
        public int Id { get; init; }
        public decimal Metric { get; init; }
    }

    private sealed class RecordQueryRow
    {
        public int Id { get; init; }
        public string ExternalId { get; init; } = null!;
        public int GroupId { get; init; }
        public DateTime EffectiveAt { get; init; }
        public string Status { get; init; } = null!;
        public decimal Metric { get; init; }
    }

    private sealed class PartQueryRow
    {
        public int RecordId { get; init; }
        public int PartNumber { get; init; }
        public int GroupId { get; init; }
        public string Description { get; init; } = null!;
        public decimal Value { get; init; }
    }

    private sealed class DetailQueryRow
    {
        public int RecordId { get; init; }
        public int PartNumber { get; init; }
        public string Code { get; init; } = null!;
        public decimal Value { get; init; }
    }

    private sealed class QueryHarness : IDisposable
    {
        private readonly OwnerContext _owner;

        public QueryHarness(
            OwnerContext owner,
            DbContext queryContext,
            IQueryable<RecordQueryRow> records,
            IQueryable<PartQueryRow> parts,
            IQueryable<DetailQueryRow> details)
        {
            _owner = owner;
            QueryContext = queryContext;
            Records = records;
            Parts = parts;
            Details = details;
        }

        public DbContext QueryContext { get; }
        public IQueryable<RecordQueryRow> Records { get; }
        public IQueryable<PartQueryRow> Parts { get; }
        public IQueryable<DetailQueryRow> Details { get; }

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
        public DbSet<Record> Records => Set<Record>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, TieredModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("composition_records");
                builder.HasKey(record => record.Id);
                builder.HasIndex(record => record.ExternalId).IsUnique();
                builder.HasMany(record => record.Parts)
                    .WithOne(part => part.Record)
                    .HasForeignKey(part => part.RecordId);
            });
            modelBuilder.Entity<RecordPart>(builder =>
            {
                builder.ToTable("composition_record_lines");
                builder.HasKey(part => new { part.RecordId, part.PartNumber });
                builder.HasMany(part => part.Details)
                    .WithOne(detail => detail.Part)
                    .HasForeignKey(detail => new { detail.RecordId, detail.PartNumber });
            });
            modelBuilder.Entity<RecordPartDetail>(builder =>
            {
                builder.ToTable("composition_record_details");
                builder.HasKey(detail => new
                {
                    detail.RecordId,
                    detail.PartNumber,
                    detail.Code,
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
            => modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, ArchivePath)
                .MatchBy(record => record.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                .PartitionBy(ConfigureRecordPartitions)
                .WithTieredView("composition_record_history")
                .Including<RecordPart>(record => record.Parts, part => part
                    .WithTieredView()
                    .Including<RecordPartDetail>(
                        item => item.Details,
                        detail => detail.WithTieredView("composition_detail_history")));
    }

    private sealed class ReadModelOwnerContext<TMarker>(string dbPath, string archivePath)
        : OwnerContext(dbPath, archivePath)
    {
        public DbSet<RecordHistory> RecordHistory => Set<RecordHistory>();
        public DbSet<RecordPartHistory> PartHistory => Set<RecordPartHistory>();
        public DbSet<RecordPartDetailHistory> DetailHistory => Set<RecordPartDetailHistory>();

        protected override void ConfigureTieredStore(ModelBuilder modelBuilder)
            => modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, ArchivePath)
                .MatchBy(record => record.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                .PartitionBy(ConfigureRecordPartitions)
                .WithReadModel<RecordHistory>()
                .Including<RecordPart>(record => record.Parts, part => part
                    .WithReadModel<RecordPartHistory>()
                    .Including<RecordPartDetail>(
                        item => item.Details,
                        detail => detail.WithReadModel<RecordPartDetailHistory>()));
    }

    private sealed class EntityHistoryContext<TMarker>(string dbPath) : DbContext
    {
        public DbSet<Record> Records => Set<Record>();
        public DbSet<RecordPart> Parts => Set<RecordPart>();
        public DbSet<RecordPartDetail> Details => Set<RecordPartDetail>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTieredView("composition_record_history", ConfigureRecordPartitions);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.Entity<RecordPart>(builder =>
            {
                builder.HasNoKey();
                builder.ToView("composition_record_lines_tiered");
                builder.Ignore(part => part.Record);
                builder.Ignore(part => part.Details);
            });
            modelBuilder.Entity<RecordPartDetail>(builder =>
            {
                builder.HasNoKey();
                builder.ToView("composition_detail_history");
                builder.Ignore(detail => detail.Part);
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
                    record => record.EffectiveOn,
                    archivePath,
                    TierGranularity.Day)
                .PartitionBy(partitions => partitions
                    .By(record => record.GroupId, "group_key")
                    .ByDay(record => record.EffectiveOn, "effective_day"))
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
            modelBuilder.ToTieredStore<NullableDateOnlyRecord>(record => record.EffectiveOn, archivePath)
                .PartitionBy(partitions => partitions
                    .By(record => record.GroupId, "group_key")
                    .ByMonth(record => record.EffectiveOn, "effective_month"))
                .WithReadModel<NullableDateOnlyRecordHistory>();
        }
    }

    private sealed class SharedOwnerContext<TMarker>(string dbPath, string archivePath)
        : DbContext, ITieredModelKey
    {
        public string ModelKey => archivePath;
        public DbSet<SharedRootA> RootAs => Set<SharedRootA>();
        public DbSet<SharedRootB> RootBs => Set<SharedRootB>();
        public DbSet<SharedTag> Tags => Set<SharedTag>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, TieredModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SharedRootA>(builder =>
            {
                builder.ToTable("composition_shared_root_a");
                builder.HasKey(root => root.Id);
                builder.HasMany(root => root.Tags)
                    .WithOne(tag => tag.RootA)
                    .HasForeignKey(tag => tag.RootAId);
            });
            modelBuilder.Entity<SharedRootB>(builder =>
            {
                builder.ToTable("composition_shared_root_b");
                builder.HasKey(root => root.Id);
                builder.HasMany(root => root.Tags)
                    .WithOne(tag => tag.RootB)
                    .HasForeignKey(tag => tag.RootBId);
            });
            modelBuilder.Entity<SharedTag>(builder =>
            {
                builder.ToTable("composition_shared_tags");
                builder.HasKey(tag => tag.Id);
            });
            modelBuilder.ToTieredStore<SharedRootB>(
                    root => root.EffectiveAt,
                    archivePath + "/b",
                    controlKey: "composition-shared-b")
                .WithTieredView("composition_shared_root_b_history")
                .Including<SharedTag>(root => root.Tags, tag => tag.WithTieredView());
            modelBuilder.ToTieredStore<SharedRootA>(
                    root => root.EffectiveAt,
                    archivePath + "/a",
                    controlKey: "composition-shared-a")
                .WithTieredView("composition_shared_root_a_history")
                .Including<SharedTag>(root => root.Tags, tag => tag.WithTieredView());
        }
    }

    private sealed class SharedHistoryContext(string dbPath) : DbContext
    {
        public DbSet<SharedTag> Tags => Set<SharedTag>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<SharedTag>(builder =>
            {
                builder.HasNoKey();
                builder.ToView("composition_shared_tags_tiered");
                builder.Ignore(tag => tag.RootA);
                builder.Ignore(tag => tag.RootB);
            });
    }

    private sealed class Record
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public int GroupId { get; set; }
        public DateTime EffectiveAt { get; set; }
        public string Status { get; set; } = null!;
        public decimal Metric { get; set; }
        public List<RecordPart> Parts { get; set; } = [];
    }

    private sealed class RecordPart
    {
        public int RecordId { get; set; }
        public int PartNumber { get; set; }
        public int GroupId { get; set; }
        public string Description { get; set; } = null!;
        public decimal Value { get; set; }
        public Record? Record { get; set; }
        public List<RecordPartDetail> Details { get; set; } = [];
    }

    private sealed class RecordPartDetail
    {
        public int RecordId { get; set; }
        public int PartNumber { get; set; }
        public string Code { get; set; } = null!;
        public decimal Value { get; set; }
        public RecordPart? Part { get; set; }
    }

    private sealed class RecordHistory
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public int GroupId { get; set; }
        public DateTime EffectiveAt { get; set; }
        public string Status { get; set; } = null!;
        public decimal Metric { get; set; }
    }

    private sealed class RecordPartHistory
    {
        public int RecordId { get; set; }
        public int PartNumber { get; set; }
        public int GroupId { get; set; }
        public string Description { get; set; } = null!;
        public decimal Value { get; set; }
    }

    private sealed class RecordPartDetailHistory
    {
        public int RecordId { get; set; }
        public int PartNumber { get; set; }
        public string Code { get; set; } = null!;
        public decimal Value { get; set; }
    }

    private sealed class DailyRecord
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public DateTime EffectiveOn { get; set; }
    }

    private sealed class DailyRecordHistory
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public DateTime EffectiveOn { get; set; }
    }

    private sealed class NullableDateOnlyRecord
    {
        public int Id { get; set; }
        public int? GroupId { get; set; }
        public DateOnly? EffectiveOn { get; set; }
    }

    private sealed class NullableDateOnlyRecordHistory
    {
        public int Id { get; set; }
        public int? GroupId { get; set; }
        public DateOnly? EffectiveOn { get; set; }
    }

    private sealed class SharedRootA
    {
        public int Id { get; set; }
        public DateTime EffectiveAt { get; set; }
        public List<SharedTag> Tags { get; set; } = [];
    }

    private sealed class SharedRootB
    {
        public int Id { get; set; }
        public DateTime EffectiveAt { get; set; }
        public List<SharedTag> Tags { get; set; } = [];
    }

    private sealed class SharedTag
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
    private sealed class ContextRecordMarker;
    private sealed class LifecycleMarker;
    private sealed class SharedQueryMarker;
}
