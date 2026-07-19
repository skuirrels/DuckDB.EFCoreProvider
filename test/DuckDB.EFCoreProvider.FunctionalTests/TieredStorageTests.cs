using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;
using static Microsoft.EntityFrameworkCore.TieredStorageTestHelpers;

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
        var (records, parts, details, detailSum) = TieredTotals(context);

        var result = await context.Database.ArchiveTierAsync<Record>(new DateTime(2025, 7, 1).AddMonths(-12));

        Assert.False(result.NoOp);
        Assert.Equal(5, result.RowsArchived);
        // Hot tables shrank; each aggregate table has its own Parquet subdirectory.
        Assert.True(context.Records.Count() < records);
        Assert.True(Directory.Exists(Path.Combine(_root, "archive", "records", "year=2024")));
        Assert.True(Directory.Exists(Path.Combine(_root, "archive", "record_lines", "year=2024")));
        Assert.True(Directory.Exists(Path.Combine(_root, "archive", "line_details", "year=2024")));
        // Tiered read-models reproduce the full history exactly — no duplicates, no gaps, at every level.
        var (tRecords, tParts, tDetails, tDetailSum) = TieredTotals(context);
        Assert.Equal((records, parts, details, detailSum), (tRecords, tParts, tDetails, tDetailSum));
    }

    [Fact]
    public async Task Additional_partition_is_declared_on_the_root_and_inherited_by_child_archives()
    {
        var dbPath = Path.Combine(_root, "group.duckdb");
        var archivePath = Path.Combine(_root, "group-archive");
        using var context = new GroupPartitionContext(dbPath, archivePath);
        context.Database.EnsureCreated();
        context.Records.AddRange(
            new Record
            {
                GroupId = 10,
                EffectiveAt = new DateTime(2024, 1, 10),
                Parts = { new RecordPart { Value = 10 } },
            },
            new Record
            {
                GroupId = 20,
                EffectiveAt = new DateTime(2024, 1, 20),
                Parts = { new RecordPart { Value = 20 } },
            },
            new Record
            {
                GroupId = 30,
                EffectiveAt = new DateTime(2024, 2, 10),
                Parts = { new RecordPart { Value = 30 } },
            });
        context.SaveChanges();

        var result = await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));

        Assert.Equal(2, result.RowsArchived);
        foreach (var groupId in new[] { 10, 20 })
        {
            Assert.True(Directory.Exists(Path.Combine(
                archivePath, "records", $"GroupId={groupId}", "EffectiveAt_month=2024-01-01")));
            Assert.True(Directory.Exists(Path.Combine(
                archivePath, "record_lines", $"GroupId={groupId}", "EffectiveAt_month=2024-01-01")));
        }

        Assert.Equal([10, 20, 30], context.RecordHistory.OrderBy(record => record.GroupId).Select(record => record.GroupId).ToList());
        Assert.Equal(3, context.PartHistory.Count());
        Assert.Equal(["GroupId", "EffectiveAt"], context.Model.FindEntityType(typeof(Record))!.GetTieredStorePartitionProperties());
        Assert.Empty(context.Model.FindEntityType(typeof(RecordPart))!.GetTieredStorePartitionProperties());
    }

    [Fact]
    public async Task Aliased_partitions_archive_and_preserve_hot_cold_reads_with_pruning()
    {
        var archivePath = Path.Combine(_root, "group-alias-archive");
        using var context = new AliasedPartitionContext(
            Path.Combine(_root, "group-alias.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        context.PartitionedRecords.AddRange(
            new PartitionedRecord
            {
                GroupId = 10,
                EffectiveAt = new DateTime(2024, 1, 10),
                PartitionedParts = { new PartitionedRecordPart { GroupId = 101 } },
            },
            new PartitionedRecord
            {
                GroupId = 10,
                EffectiveAt = new DateTime(2024, 2, 10),
                PartitionedParts = { new PartitionedRecordPart { GroupId = 102 } },
            },
            new PartitionedRecord
            {
                GroupId = 20,
                EffectiveAt = new DateTime(2024, 1, 10),
                PartitionedParts = { new PartitionedRecordPart { GroupId = 201 } },
            },
            new PartitionedRecord
            {
                GroupId = 20,
                EffectiveAt = new DateTime(2024, 2, 10),
                PartitionedParts = { new PartitionedRecordPart { GroupId = 202 } },
            },
            new PartitionedRecord
            {
                GroupId = 30,
                EffectiveAt = new DateTime(2024, 3, 10),
                PartitionedParts = { new PartitionedRecordPart { GroupId = 301 } },
            });
        context.SaveChanges();

        var result = await context.Database.ArchiveTierAsync<PartitionedRecord>(new DateTime(2024, 3, 1));

        Assert.Equal(4, result.RowsArchived);
        Assert.True(Directory.Exists(Path.Combine(
            archivePath, "partitioned_records", "root_group_id=10", "effective_month=2024-01-01")));
        Assert.True(Directory.Exists(Path.Combine(
            archivePath, "partitioned_record_parts", "root_group_id=10", "effective_month=2024-01-01")));
        Assert.Equal(30, Assert.Single(context.PartitionedRecords).GroupId);
        Assert.Equal(301, Assert.Single(context.PartitionedParts).GroupId);
        Assert.Equal([10, 10, 20, 20, 30], context.PartitionedRecordHistory.OrderBy(root => root.Id).Select(root => root.GroupId).ToList());
        Assert.Equal([101, 102, 201, 202, 301], context.PartitionedPartHistory.OrderBy(item => item.Id).Select(item => item.GroupId).ToList());

        var from = new DateTime(2024, 2, 1);
        var to = new DateTime(2024, 3, 1);
        var query = context.PartitionedRecordHistory.Where(
            root => root.GroupId == 10 && root.EffectiveAt >= from && root.EffectiveAt < to);
        var sql = query.ToQueryString();
        Assert.Contains("root_group_id", sql);
        Assert.Contains("effective_month", sql);
        AssertFilesPruned(Explain(context, query), "1/4");
        Assert.Single(query);

        var partitionSpec = context.Database.SqlQueryRaw<string>(
            "SELECT partition_spec AS \"Value\" FROM __duckdb_tier_control WHERE name = 'partitioned_records'").Single();
        Assert.Contains("\"Name\":\"root_group_id\"", partitionSpec);
        Assert.Contains("\"Name\":\"effective_month\"", partitionSpec);
    }

    [Fact]
    public async Task Restoration_by_an_aliased_partition_moves_the_exact_graph_back_to_hot()
    {
        var archivePath = Path.Combine(_root, "group-alias-restore-archive");
        using var context = new AliasedPartitionContext(
            Path.Combine(_root, "group-alias-restore.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        context.PartitionedRecords.AddRange(
            new PartitionedRecord
            {
                GroupId = 10,
                EffectiveAt = new DateTime(2024, 1, 10),
                PartitionedParts = { new PartitionedRecordPart { GroupId = 101 } },
            },
            new PartitionedRecord
            {
                GroupId = 20,
                EffectiveAt = new DateTime(2024, 1, 20),
                PartitionedParts = { new PartitionedRecordPart { GroupId = 201 } },
            });
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<PartitionedRecord>(new DateTime(2024, 2, 1));

        var result = await context.Database.RestoreArchiveTierAsync<PartitionedRecord>(
            new TierRestoreOptions
            {
                Scope = TierMaintenanceScope.ForPartitionValues(
                    new Dictionary<string, object?> { ["GroupId"] = 10 }),
            });

        Assert.Equal(TierArchiveOperation.Restore, result.Publication.Operation);
        Assert.Equal(1, result.RootsSelected);
        Assert.Equal(1, result.RootsInserted);
        Assert.Equal(2, result.RowsInserted);
        Assert.Equal(10, Assert.Single(context.PartitionedRecords).GroupId);
        Assert.Equal(101, Assert.Single(context.PartitionedParts).GroupId);
        Assert.Equal([10, 20], context.PartitionedRecordHistory.OrderBy(root => root.GroupId).Select(root => root.GroupId).ToList());
        Assert.Equal([101, 201], context.PartitionedPartHistory.OrderBy(item => item.GroupId).Select(item => item.GroupId).ToList());
        Assert.True(Directory.Exists(Path.Combine(
            result.Publication.ArchivePath, "root_group_id=20", "effective_month=2024-01-01")));
        Assert.False(Directory.Exists(Path.Combine(
            result.Publication.ArchivePath, "root_group_id=10", "effective_month=2024-01-01")));
        var child = Assert.Single(result.Publication.Nodes, node => node.Table == "partitioned_record_parts");
        Assert.True(Directory.Exists(Path.Combine(
            child.ArchivePath, "root_group_id=20", "effective_month=2024-01-01")));
    }

    [Fact]
    public async Task Retention_trim_preserves_exact_partition_scopes_and_hot_rows_across_restart()
    {
        var dbPath = Path.Combine(_root, "retention.duckdb");
        var archivePath = Path.Combine(_root, "retention-archive");
        TierArchiveRetentionPlan plan;
        string inputGeneration;
        using (var context = new AliasedPartitionContext(dbPath, archivePath))
        {
            context.Database.EnsureCreated();
            context.PartitionedRecords.AddRange(
                new PartitionedRecord
                {
                    GroupId = 10,
                    EffectiveAt = new DateTime(2024, 1, 10),
                    PartitionedParts = { new PartitionedRecordPart { GroupId = 101 } },
                },
                new PartitionedRecord
                {
                    GroupId = 20,
                    EffectiveAt = new DateTime(2024, 1, 20),
                    PartitionedParts = { new PartitionedRecordPart { GroupId = 201 } },
                },
                new PartitionedRecord
                {
                    GroupId = 20,
                    EffectiveAt = new DateTime(2024, 2, 20),
                    PartitionedParts = { new PartitionedRecordPart { GroupId = 202 } },
                },
                new PartitionedRecord
                {
                    GroupId = 30,
                    EffectiveAt = new DateTime(2024, 3, 20),
                    PartitionedParts = { new PartitionedRecordPart { GroupId = 301 } },
                });
            await context.SaveChangesAsync();
            await context.Database.ArchiveTierAsync<PartitionedRecord>(new DateTime(2024, 3, 1));
            context.PartitionedRecords.Add(new PartitionedRecord
            {
                GroupId = 40,
                EffectiveAt = new DateTime(2024, 1, 25),
                PartitionedParts = { new PartitionedRecordPart { GroupId = 401 } },
            });
            await context.SaveChangesAsync();

            plan = await context.Database.PlanArchiveRetentionAsync<PartitionedRecord>(
                new TierArchiveRetentionOptions
                {
                    RetainFrom = new DateTime(2024, 2, 18),
                    RetainedPartitionScopes =
                    [
                        TierMaintenanceScope.ForPartitionValues(
                            new Dictionary<string, object?> { [nameof(PartitionedRecord.GroupId)] = 10 }),
                    ],
                });

            Assert.Equal(new DateTime(2024, 2, 1), plan.EffectiveRetainFrom);
            Assert.Equal("partitioned_records", plan.ControlKey);
            Assert.Equal(3, plan.Nodes.Single(node => node.EntityType == typeof(PartitionedRecord)).InputRows);
            Assert.Equal(2, plan.Nodes.Single(node => node.EntityType == typeof(PartitionedRecord)).RetainedRows);
            Assert.Equal(1, plan.Nodes.Single(node => node.EntityType == typeof(PartitionedRecord)).ExcludedRows);
            Assert.Equal(2, plan.Nodes.Single(node => node.EntityType == typeof(PartitionedRecordPart)).RetainedRows);
            Assert.False(plan.IsNoOp);
            Assert.StartsWith("retention-", plan.ExpectedOutputGenerationId);
            inputGeneration = plan.InputGenerationId;

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                var transactionError = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => context.Database.PublishArchiveRetentionAsync<PartitionedRecord>(plan));
                Assert.Contains("caller transaction", transactionError.Message);
                await transaction.RollbackAsync();
            }

            var result = await context.Database.PublishArchiveRetentionAsync<PartitionedRecord>(plan);

            Assert.Equal(TierArchiveOperation.RetentionTrim, result.Operation);
            Assert.Equal(plan.ExpectedOutputGenerationId, result.Revision);
            Assert.False(result.NoOp);
            Assert.Equal(
                [(10, 1), (20, 2), (30, 3), (40, 1)],
                context.PartitionedRecordHistory
                    .OrderBy(root => root.GroupId)
                    .Select(root => new ValueTuple<int, int>(root.GroupId, root.EffectiveAt.Month))
                    .ToArray());
            Assert.Equal(
                [101, 202, 301, 401],
                context.PartitionedPartHistory.OrderBy(item => item.GroupId).Select(item => item.GroupId).ToArray());
            Assert.Single(context.PartitionedRecords, root => root.GroupId == 40);
            Assert.Single(context.PartitionedRecords, root => root.GroupId == 30);
        }

        using (var restarted = new AliasedPartitionContext(dbPath, archivePath))
        {
            restarted.Database.EnsureTieredStoresCreated();
            Assert.Equal(
                [(10, 1), (20, 2), (30, 3), (40, 1)],
                restarted.PartitionedRecordHistory
                    .OrderBy(root => root.GroupId)
                    .Select(root => new ValueTuple<int, int>(root.GroupId, root.EffectiveAt.Month))
                    .ToArray());

            var inventory = await restarted.Database.GetArchiveGenerationInventoryAsync<PartitionedRecord>();
            Assert.Equal(plan.ExpectedOutputGenerationId, inventory.ActiveGenerationId);
            Assert.Contains(
                inventory.Generations,
                generation => generation.GenerationId == inputGeneration
                              && generation.State == TierArchiveGenerationState.Published);
            var active = inventory.Generations.Single(generation => generation.State == TierArchiveGenerationState.Active);
            var cataloguedFiles = restarted.Database.SqlQueryRaw<string>(
                    "SELECT file_path AS \"Value\" FROM __duckdb_tier_generation_files "
                    + "WHERE control_key = 'partitioned_records' AND generation_id = {0} ORDER BY file_path",
                    plan.ExpectedOutputGenerationId)
                .ToArray();
            Assert.Equal(active.FileCount, cataloguedFiles.Length);
            Assert.All(cataloguedFiles, file => Assert.Contains(plan.ExpectedOutputGenerationId, file));
            Assert.All(cataloguedFiles, file => Assert.True(File.Exists(file)));

            var retry = await restarted.Database.PublishArchiveRetentionAsync<PartitionedRecord>(plan);
            Assert.True(retry.NoOp);
            Assert.Equal(plan.ExpectedOutputGenerationId, retry.Revision);

            var cleanup = await restarted.Database.PlanArchiveGenerationCleanupAsync<PartitionedRecord>([inputGeneration]);
            Assert.Single(cleanup.Candidates);
            Assert.Equal(inputGeneration, cleanup.Candidates[0].GenerationId);
        }
    }

    [Fact]
    public async Task Retention_trim_supports_no_op_and_complete_cold_trim()
    {
        var archivePath = Path.Combine(_root, "retention-complete-archive");
        using var context = new RecordContext(
            Path.Combine(_root, "retention-complete.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(new Record
        {
            EffectiveAt = new DateTime(2024, 1, 15),
            Parts =
            {
                new RecordPart
                {
                    Value = 10,
                    Details = { new RecordPartDetail { Value = 11 } },
                },
            },
        });
        await context.SaveChangesAsync();
        await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));

        var noOpPlan = await context.Database.PlanArchiveRetentionAsync<Record>(
            new TierArchiveRetentionOptions { RetainFrom = new DateTime(2024, 1, 1) });
        Assert.True(noOpPlan.IsNoOp);
        Assert.Equal(noOpPlan.InputGenerationId, noOpPlan.ExpectedOutputGenerationId);
        var noOp = await context.Database.PublishArchiveRetentionAsync<Record>(noOpPlan);
        Assert.True(noOp.NoOp);
        Assert.Single(context.RecordHistory);

        var completePlan = await context.Database.PlanArchiveRetentionAsync<Record>(
            new TierArchiveRetentionOptions { RetainFrom = new DateTime(2024, 2, 1) });
        Assert.False(completePlan.IsNoOp);
        Assert.All(completePlan.Nodes, node => Assert.Equal(0, node.RetainedRows));

        var result = await context.Database.PublishArchiveRetentionAsync<Record>(completePlan);

        Assert.False(result.NoOp);
        Assert.Empty(context.RecordHistory);
        Assert.Empty(context.PartHistory);
        Assert.Empty(context.DetailHistory);
        Assert.All(result.Nodes, node => Assert.Equal(0, node.FileCount));
        var inventory = await context.Database.GetArchiveGenerationInventoryAsync<Record>();
        Assert.Equal(0, inventory.Generations.Single(generation => generation.State == TierArchiveGenerationState.Active).FileCount);
        Assert.Contains(inventory.Generations, generation => generation.GenerationId == completePlan.InputGenerationId);
    }

    [Fact]
    public async Task Retention_no_op_publication_revalidates_the_exact_active_catalogue()
    {
        using var context = new RecordContext(
            Path.Combine(_root, "retention-no-op-stale.duckdb"),
            Path.Combine(_root, "retention-no-op-stale-archive"));
        context.Database.EnsureCreated();
        context.Records.Add(new Record { EffectiveAt = new DateTime(2024, 1, 15) });
        await context.SaveChangesAsync();
        await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));
        var plan = await context.Database.PlanArchiveRetentionAsync<Record>(
            new TierArchiveRetentionOptions { RetainFrom = new DateTime(2024, 1, 1) });
        Assert.True(plan.IsNoOp);

        context.Database.ExecuteSqlRaw(
            "DELETE FROM __duckdb_tier_generation_files WHERE file_path = "
            + "(SELECT min(file_path) FROM __duckdb_tier_generation_files WHERE control_key = 'records');");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.PublishArchiveRetentionAsync<Record>(plan));

        Assert.Contains("exact file catalogue", exception.Message);
    }

    [Theory]
    [InlineData("part-{uuid}.parquet")]
    [InlineData("part-{uuidv4}.parquet")]
    [InlineData("part-{uuidv7}.parquet")]
    public async Task Retention_planning_rejects_nondeterministic_filename_patterns(string filenamePattern)
    {
        using var context = new RecordContext(
            Path.Combine(_root, "retention-uuid.duckdb"),
            Path.Combine(_root, "retention-uuid-archive"));
        context.Database.EnsureCreated();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => context.Database.PlanArchiveRetentionAsync<Record>(
                new TierArchiveRetentionOptions
                {
                    RetainFrom = new DateTime(2024, 1, 1),
                    Writer = new TierParquetWriterOptions { FilenamePattern = filenamePattern },
                }));

        Assert.Equal("FilenamePattern", exception.ParamName);
        Assert.Contains("deterministic", exception.Message);
    }

    [Fact]
    public async Task Retention_publication_rejects_a_plan_after_the_active_generation_changes()
    {
        var archivePath = Path.Combine(_root, "retention-stale-archive");
        using var context = new RecordContext(
            Path.Combine(_root, "retention-stale.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(new Record { EffectiveAt = new DateTime(2024, 1, 15) });
        await context.SaveChangesAsync();
        await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));
        var plan = await context.Database.PlanArchiveRetentionAsync<Record>(
            new TierArchiveRetentionOptions { RetainFrom = new DateTime(2024, 2, 1) });
        await context.Database.CompactArchiveTierAsync<Record>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.PublishArchiveRetentionAsync<Record>(plan));

        Assert.Contains("stale", exception.Message);
        Assert.Single(context.RecordHistory);
    }

    [Fact]
    public async Task Retention_planning_rejects_an_inexact_active_generation_file_catalogue()
    {
        using var context = new RecordContext(
            Path.Combine(_root, "retention-catalogue.duckdb"),
            Path.Combine(_root, "retention-catalogue-archive"));
        context.Database.EnsureCreated();
        context.Records.Add(new Record { EffectiveAt = new DateTime(2024, 1, 15) });
        await context.SaveChangesAsync();
        await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "DELETE FROM __duckdb_tier_generation_files WHERE file_path = "
            + "(SELECT min(file_path) FROM __duckdb_tier_generation_files WHERE control_key = 'records');");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.PlanArchiveRetentionAsync<Record>(
                new TierArchiveRetentionOptions { RetainFrom = new DateTime(2024, 2, 1) }));

        Assert.Contains("exact file catalogue", exception.Message);
        Assert.Contains("does not match the physical files", exception.Message);
    }

    [Fact]
    public async Task Retention_scopes_accept_only_exact_declared_partition_values()
    {
        using var context = new AliasedPartitionContext(
            Path.Combine(_root, "retention-scope-validation.duckdb"),
            Path.Combine(_root, "retention-scope-validation-archive"));
        context.Database.EnsureCreated();
        context.PartitionedRecords.Add(new PartitionedRecord
        {
            GroupId = 10,
            EffectiveAt = new DateTime(2024, 1, 15),
        });
        await context.SaveChangesAsync();
        await context.Database.ArchiveTierAsync<PartitionedRecord>(new DateTime(2024, 2, 1));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => context.Database.PlanArchiveRetentionAsync<PartitionedRecord>(
                new TierArchiveRetentionOptions
                {
                    RetainFrom = new DateTime(2024, 2, 1),
                    RetainedPartitionScopes = [TierMaintenanceScope.All],
                }));

        Assert.Contains("exact declared partition values", exception.Message);
    }

    [Fact]
    public async Task Bounded_bootstrap_archives_only_the_aligned_initial_window()
    {
        var dbPath = Path.Combine(_root, "bootstrap.duckdb");
        var archivePath = Path.Combine(_root, "bootstrap-archive");
        using var context = new RecordContext(dbPath, archivePath);
        context.Database.EnsureCreated();
        context.Records.AddRange(
            new Record { EffectiveAt = new DateTime(2024, 1, 15) },
            new Record { EffectiveAt = new DateTime(2024, 2, 15) },
            new Record { EffectiveAt = new DateTime(2024, 3, 15) });
        await context.SaveChangesAsync();

        var result = await context.Database.BootstrapArchiveTierAsync<Record>(
            new DateTime(2024, 2, 1),
            new DateTime(2024, 3, 1));

        Assert.Equal(new DateTime(2024, 2, 1), result.WindowStart);
        Assert.Equal(new DateTime(2024, 3, 1), result.WindowEnd);
        Assert.Equal(1, result.RowsArchived);
        Assert.Equal(2, context.Records.Count());
        Assert.Equal(3, context.RecordHistory.Count());
        Assert.True(Directory.Exists(Path.Combine(archivePath, "records", "year=2024", "month=2")));
        Assert.False(Directory.Exists(Path.Combine(archivePath, "records", "year=2024", "month=1")));

        var retry = await context.Database.BootstrapArchiveTierAsync<Record>(
            new DateTime(2024, 2, 1),
            new DateTime(2024, 3, 1));
        Assert.True(retry.NoOp);
        Assert.Equal(
            new DateTime(2024, 2, 1),
            context.Database.SqlQueryRaw<DateTime>(
                    "SELECT bootstrap_from AS \"Value\" FROM __duckdb_tier_control WHERE name = 'records'")
                .Single());
        Assert.Equal(
            new DateTime(2024, 3, 1),
            context.Database.SqlQueryRaw<DateTime>(
                    "SELECT bootstrap_to AS \"Value\" FROM __duckdb_tier_control WHERE name = 'records'")
                .Single());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.BootstrapArchiveTierAsync<Record>(
                new DateTime(2024, 2, 1),
                new DateTime(2024, 4, 1)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.BootstrapArchiveTierAsync<Record>(
                new DateTime(2024, 1, 1),
                new DateTime(2024, 3, 1)));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.BootstrapArchiveTierAsync<Record>(
                new DateTime(2024, 1, 1),
                new DateTime(2024, 2, 1)));
        await Assert.ThrowsAsync<ArgumentException>(
            () => context.Database.BootstrapArchiveTierAsync<Record>(
                new DateTime(2024, 2, 2),
                new DateTime(2024, 3, 1)));
    }

    [Fact]
    public async Task Daily_retention_trim_uses_the_configured_day_boundary()
    {
        var archivePath = Path.Combine(_root, "daily-retention-archive");
        using var context = new DayGranularityContext(
            Path.Combine(_root, "daily-retention.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        context.AddRange(
            new Record { EffectiveAt = new DateTime(2024, 1, 1, 12, 0, 0) },
            new Record { EffectiveAt = new DateTime(2024, 1, 2, 12, 0, 0) },
            new Record { EffectiveAt = new DateTime(2024, 1, 3, 12, 0, 0) });
        await context.SaveChangesAsync();
        await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 1, 4));

        var plan = await context.Database.PlanArchiveRetentionAsync<Record>(
            new TierArchiveRetentionOptions { RetainFrom = new DateTime(2024, 1, 3, 18, 0, 0) });
        var result = await context.Database.PublishArchiveRetentionAsync<Record>(plan);

        Assert.Equal(new DateTime(2024, 1, 3), plan.EffectiveRetainFrom);
        Assert.Equal(1, result.RowsArchived);
        Assert.Equal([3], context.Set<RecordRm>().Select(record => record.EffectiveAt.Day).ToArray());
    }

    [Fact]
    public async Task Reconciliation_repartitions_corrected_rows_using_aliased_partition_names()
    {
        var archivePath = Path.Combine(_root, "group-alias-reconcile-archive");
        using var context = new AliasedPartitionContext(
            Path.Combine(_root, "group-alias-reconcile.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        var root = new PartitionedRecord
        {
            GroupId = 10,
            EffectiveAt = new DateTime(2024, 1, 10),
            PartitionedParts = { new PartitionedRecordPart { GroupId = 101 } },
        };
        context.PartitionedRecords.Add(root);
        context.SaveChanges();
        var orderId = root.Id;
        var itemId = root.PartitionedParts[0].Id;
        await context.Database.ArchiveTierAsync<PartitionedRecord>(new DateTime(2024, 2, 1));
        context.ChangeTracker.Clear();
        context.Database.ExecuteSqlInterpolated(
            $"""
             INSERT INTO partitioned_records ("Id", "EffectiveAt", "GroupId")
             VALUES ({orderId}, {new DateTime(2024, 1, 10)}, {20});
             """);
        context.Database.ExecuteSqlInterpolated(
            $"""
             INSERT INTO partitioned_record_parts ("Id", "GroupId", "PartitionedRecordId")
             VALUES ({itemId}, {999}, {orderId});
             """);

        var result = await context.Database.ReconcileArchiveTierAsync<PartitionedRecord>();

        Assert.Equal(TierArchiveOperation.Reconcile, result.Operation);
        Assert.False(result.NoOp);
        Assert.Equal(1, result.RowsArchived);
        Assert.Empty(context.PartitionedRecords);
        Assert.Empty(context.PartitionedParts);
        Assert.Equal(20, context.PartitionedRecordHistory.Single().GroupId);
        Assert.Equal(999, context.PartitionedPartHistory.Single().GroupId);
        Assert.True(Directory.Exists(Path.Combine(
            result.ArchivePath, "root_group_id=20", "effective_month=2024-01-01")));
        Assert.False(Directory.Exists(Path.Combine(
            result.ArchivePath, "root_group_id=10", "effective_month=2024-01-01")));
        var child = Assert.Single(result.Nodes, node => node.Table == "partitioned_record_parts");
        Assert.True(Directory.Exists(Path.Combine(
            child.ArchivePath, "root_group_id=20", "effective_month=2024-01-01")));
    }

    [Fact]
    public async Task Exact_partition_alias_shorthand_retains_the_implicit_lifecycle_bucket()
    {
        var archivePath = Path.Combine(_root, "shorthand-alias-archive");
        using var context = new ShorthandAliasPartitionContext(
            Path.Combine(_root, "shorthand-alias.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(new Record { GroupId = 10, EffectiveAt = new DateTime(2024, 1, 10) });
        context.SaveChanges();

        await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));

        Assert.True(Directory.Exists(Path.Combine(
            archivePath, "records", "group_key=10", "EffectiveAt_month=2024-01-01")));
    }

    [Fact]
    public async Task Declared_partition_order_controls_the_directory_hierarchy()
    {
        var archivePath = Path.Combine(_root, "month-first-archive");
        using var context = new MonthFirstPartitionContext(Path.Combine(_root, "month-first.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(new Record { GroupId = 10, EffectiveAt = new DateTime(2024, 1, 10) });
        context.SaveChanges();

        await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));

        Assert.True(Directory.Exists(Path.Combine(
            archivePath, "records", "EffectiveAt_month=2024-01-01", "GroupId=10")));
    }

    [Fact]
    public async Task Date_partition_transforms_create_application_defined_year_month_and_day_buckets()
    {
        var archivePath = Path.Combine(_root, "date-buckets-archive");
        using var context = new DateBucketPartitionContext(Path.Combine(_root, "date-buckets.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(new DateBucketRecord
        {
            GroupId = 10,
            CreatedAt = new DateTime(2023, 12, 20),
            ReviewedAt = new DateTime(2024, 1, 5),
            EffectiveAt = new DateTime(2024, 1, 10, 15, 30, 0),
        });
        context.SaveChanges();

        await context.Database.ArchiveTierAsync<DateBucketRecord>(new DateTime(2024, 1, 11));

        Assert.True(Directory.Exists(Path.Combine(
            archivePath,
            "date_bucket_records",
            "GroupId=10",
            "CreatedAt_year=2023-01-01",
            "ReviewedAt_month=2024-01-01",
            "EffectiveAt_day=2024-01-10")));
    }

    [Fact]
    public async Task Group_and_effective_month_predicates_prune_the_corresponding_parquet_partitions()
    {
        var archivePath = Path.Combine(_root, "pruning-archive");
        using var context = new GroupPartitionContext(Path.Combine(_root, "pruning.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.AddRange(
            new Record { GroupId = 10, EffectiveAt = new DateTime(2024, 1, 10), Parts = { new RecordPart { Value = 10 } } },
            new Record { GroupId = 10, EffectiveAt = new DateTime(2024, 2, 10), Parts = { new RecordPart { Value = 10 } } },
            new Record { GroupId = 20, EffectiveAt = new DateTime(2024, 1, 10), Parts = { new RecordPart { Value = 20 } } },
            new Record { GroupId = 20, EffectiveAt = new DateTime(2024, 2, 10), Parts = { new RecordPart { Value = 20 } } });
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 3, 1));

        var from = new DateTime(2024, 2, 1);
        var to = new DateTime(2024, 2, 29);
        var groupAndMonth = context.RecordHistory.Where(
            record => record.GroupId == 10 && record.EffectiveAt >= from && record.EffectiveAt < to);
        var groupOnly = context.RecordHistory.Where(record => record.GroupId == 10);
        var monthOnly = context.RecordHistory.Where(record => record.EffectiveAt >= from && record.EffectiveAt < to);

        var sql = groupAndMonth.ToQueryString();
        Assert.Contains("EffectiveAt_month", sql);
        Assert.Contains("date_trunc('month'", sql);
        AssertFilesPruned(Explain(context, groupAndMonth), "1/4");
        AssertFilesPruned(Explain(context, groupOnly), "2/4");
        AssertFilesPruned(Explain(context, monthOnly), "2/4");
        Assert.Equal([2], groupAndMonth.Select(record => record.Id).ToList());
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
                GroupId = 10,
                Region = "EU/West",
                IsPriority = true,
                ValueBand = 12.34m,
                SnapshotAt = new DateTime(2024, 1, 10, 12, 34, 56),
                EffectiveDate = new DateOnly(2024, 1, 10),
                TenantId = coldTenant,
            },
            new TypedPartitionRoot
            {
                ArchivedAt = new DateTime(2024, 2, 10),
                GroupId = 20,
                Region = "US",
                IsPriority = false,
                ValueBand = 56.78m,
                SnapshotAt = new DateTime(2024, 2, 10, 8, 0, 0),
                EffectiveDate = new DateOnly(2024, 2, 10),
                TenantId = hotTenant,
            });
        context.SaveChanges();

        await context.Database.ArchiveTierAsync<TypedPartitionRoot>(new DateTime(2024, 2, 1));

        Assert.Equal(69.12m, context.History.Sum(row => row.ValueBand));
        Assert.Single(context.History.Where(row => row.IsPriority));
        Assert.Single(context.History.Where(row => row.SnapshotAt < new DateTime(2024, 2, 1)));
        Assert.Single(context.History.Where(row => row.EffectiveDate < new DateOnly(2024, 2, 1)));
        Assert.Single(context.History.Where(row => row.TenantId == coldTenant));
        Assert.Equal(["EU/West", "US"], context.History.OrderBy(row => row.GroupId).Select(row => row.Region).ToList());

        var coldFile = Assert.Single(Directory.GetFiles(archivePath, "*.parquet", SearchOption.AllDirectories));
        Assert.Contains(Path.Combine("group_id=10", "region_code=EU%2FWest"), coldFile);
        Assert.Contains("ArchivedAt_month=2024-01-01", coldFile);
        Assert.Equal(
            ["GroupId", "Region", "IsPriority", "ValueBand", "SnapshotAt", "EffectiveDate", "TenantId"],
            context.Model.FindEntityType(typeof(TypedPartitionRoot))!.GetTieredStorePartitionProperties());

        var partitionSpec = context.Database.SqlQueryRaw<string>(
            "SELECT partition_spec AS \"Value\" FROM __duckdb_tier_control WHERE name = 'typed_roots'").Single();
        Assert.Contains("\"Version\":2", partitionSpec);
        Assert.Contains("\"Granularity\":0", partitionSpec);
        Assert.Contains("\"Name\":\"ValueBand\",\"StoreType\":\"DECIMAL(10,2)\"", partitionSpec);
        Assert.Contains("\"Name\":\"EffectiveDate\",\"StoreType\":\"DATE\"", partitionSpec);
    }

    [Fact]
    public void Child_builder_does_not_expose_partition_configuration()
        => Assert.DoesNotContain(
            typeof(TieredChildBuilder<RecordPart>).GetMethods(),
            method => method.Name == nameof(TieredStoreBuilder<Record>.PartitionBy));

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
            ["EffectiveAt"],
            context.Model.FindEntityType(typeof(Record))!.GetTieredStorePartitionProperties());
        Assert.Equal(
            ["GroupId", "EffectiveAt"],
            repeated.Model.FindEntityType(typeof(Record))!.GetTieredStorePartitionProperties());
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

        var first = await context.Database.ArchiveTierAsync<Record>(cutoff);
        var second = await context.Database.ArchiveTierAsync<Record>(cutoff.AddMonths(-1));

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
            context.Database.ArchiveTierAsync<Record>(new DateTime(2025, 7, 1)));

        Assert.Contains("outside the caller transaction", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(_root, "archive", "records")));
    }

    [Fact]
    public async Task Single_table_archive_rejects_an_existing_transaction_before_copying()
    {
        var archivePath = Path.Combine(_root, "single-transaction-archive");
        using var context = new MonthFirstPartitionContext(
            Path.Combine(_root, "single-transaction.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(new Record { GroupId = 10, EffectiveAt = new DateTime(2024, 1, 15) });
        context.SaveChanges();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1)));

        Assert.Contains("cannot be rolled back", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(archivePath, "records")));
    }

    [Fact]
    public async Task Reporting_join_across_read_models_spans_hot_and_cold()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        await context.Database.ArchiveTierAsync<Record>(new DateTime(2025, 7, 1).AddMonths(-12));

        var totalValueByYear =
            (from l in context.PartHistory
             join i in context.RecordHistory on l.RecordId equals i.Id
             group l.Value by i.EffectiveAt.Year into g
             select new { Year = g.Key, TotalValue = g.Sum() }).ToList();

        // Both the cold (2024) and hot (2025) years must appear in one query.
        Assert.Contains(totalValueByYear, r => r.Year == 2024);
        Assert.Contains(totalValueByYear, r => r.Year == 2025);
        Assert.Equal(context.PartHistory.Sum(l => l.Value), totalValueByYear.Sum(r => r.TotalValue));
    }

    [Fact]
    public void Hot_writes_and_include_are_unaffected_by_tiering()
    {
        using var context = CreateContext();

        // Normal EF: write a root with a child graph, then Include across the aggregate.
        var record = new Record { EffectiveAt = new DateTime(2025, 6, 1) };
        var part = new RecordPart { Value = 42 };
        part.Details.Add(new RecordPartDetail { Value = 42 });
        record.Parts.Add(part);
        context.Records.Add(record);
        context.SaveChanges();

        using var reader = CreateContext();
        var loaded = reader.Records.Include(i => i.Parts).ThenInclude(l => l.Details).Single();
        Assert.Single(loaded.Parts);
        Assert.Single(loaded.Parts[0].Details);
        Assert.Equal(42, loaded.Parts[0].Details[0].Value);
    }

    [Fact]
    public void Ensure_created_alone_creates_all_aggregate_views()
    {
        using var context = CreateContext(); // EnsureCreated only; no explicit EnsureTieredStoresCreated
        context.Records.Add(new Record { EffectiveAt = new DateTime(2025, 6, 1), Parts = { new RecordPart { Value = 1 } } });
        context.SaveChanges();

        // Querying every tiered view must not raise a "view does not exist" error.
        Assert.Equal(1, context.RecordHistory.Count());
        Assert.Equal(1, context.PartHistory.Count());
    }

    [Fact]
    public void Existing_control_table_is_upgraded_with_partition_metadata()
    {
        using var context = CreateContext();
        context.Database.ExecuteSqlRaw("DROP VIEW line_details_tiered;");
        context.Database.ExecuteSqlRaw("DROP VIEW record_lines_tiered;");
        context.Database.ExecuteSqlRaw("DROP VIEW records_tiered;");
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
        var bootstrapColumns = context.Database
            .SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM pragma_table_info('__duckdb_tier_control') "
                + "WHERE name IN ('bootstrap_from', 'bootstrap_to')")
            .Single();
        Assert.Equal(2, bootstrapColumns);
    }

    [Fact]
    public async Task Purge_drops_a_period_across_every_aggregate_table()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        await context.Database.ArchiveTierAsync<Record>(new DateTime(2025, 7, 1).AddMonths(-12));
        var beforeRecords = context.RecordHistory.Count();

        var purged = context.Database.PurgeArchiveOlderThan<Record>(new DateTime(2024, 4, 1));

        Assert.Equal(6, purged); // 2 months × 3 tables
        Assert.Equal(beforeRecords - 2, context.RecordHistory.Count());
    }

    [Fact]
    public async Task Purge_that_empties_the_archive_falls_back_to_a_hot_only_view()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        await context.Database.ArchiveTierAsync<Record>(new DateTime(2025, 7, 1).AddMonths(-12));

        // Purge past all cold data: every archived partition is removed for every aggregate table.
        var purged = context.Database.PurgeArchiveOlderThan<Record>(new DateTime(2025, 7, 1));

        Assert.True(purged > 0);
        // The views must fall back to hot-only rather than error on an empty read_parquet glob.
        Assert.Equal(context.Records.Count(), context.RecordHistory.Count());
        Assert.Equal(context.Set<RecordPart>().Count(), context.PartHistory.Count());
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
        using (var original = new RecordContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            Seed(original, months: 2, baseDate: new DateTime(2024, 2, 1));
            await original.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));
        }

        using var changed = new GroupPartitionContext(dbPath, archivePath);
        var exception = Assert.Throws<InvalidOperationException>(() => changed.Database.EnsureTieredStoresCreated());

        Assert.Contains("different partition layout", exception.Message);
        Assert.Contains("Rewrite or clear", exception.Message);
    }

    [Fact]
    public async Task Changing_an_aliased_partition_name_is_rejected_as_partition_layout_drift()
    {
        var dbPath = Path.Combine(_root, "alias-layout.duckdb");
        var archivePath = Path.Combine(_root, "alias-layout-archive");
        using (var original = new AliasedPartitionContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            original.PartitionedRecords.Add(new PartitionedRecord
            {
                GroupId = 10,
                EffectiveAt = new DateTime(2024, 1, 10),
                PartitionedParts = { new PartitionedRecordPart { GroupId = 101 } },
            });
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<PartitionedRecord>(new DateTime(2024, 2, 1));
            var partitionSpec = original.Database.SqlQueryRaw<string>(
                "SELECT partition_spec AS \"Value\" FROM __duckdb_tier_control WHERE name = 'partitioned_records'").Single();
            Assert.Contains("\"Name\":\"root_group_id\"", partitionSpec);
        }

        using var changed = new ChangedAliasedPartitionContext(dbPath, archivePath);
        var exception = Assert.Throws<InvalidOperationException>(
            () => changed.Database.EnsureTieredStoresCreated());

        Assert.Contains("root_group_key", exception.Message);
        Assert.Contains("different partition layout", exception.Message);
        Assert.Contains("Rewrite or clear", exception.Message);
    }

    [Fact]
    public async Task Unrecorded_cold_files_reject_a_new_partition_layout()
    {
        var dbPath = Path.Combine(_root, "orphan.duckdb");
        var archivePath = Path.Combine(_root, "orphan-archive");
        using (var original = new RecordContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            Seed(original, months: 2, baseDate: new DateTime(2024, 2, 1));
            await original.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));
            original.Database.ExecuteSqlRaw("DELETE FROM __duckdb_tier_control WHERE name = 'records';");
        }

        using var changed = new GroupPartitionContext(dbPath, archivePath);
        var exception = Assert.Throws<InvalidOperationException>(() => changed.Database.EnsureTieredStoresCreated());

        Assert.Contains("unrecorded partition layout", exception.Message);
        Assert.Contains("Rewrite or clear", exception.Message);
    }

    [Fact]
    public async Task Changing_granularity_with_existing_cold_files_is_rejected()
    {
        var dbPath = Path.Combine(_root, "granularity.duckdb");
        var archivePath = Path.Combine(_root, "granularity-archive");
        using (var original = new RecordContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            Seed(original, months: 2, baseDate: new DateTime(2024, 2, 1));
            await original.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));
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
        await context.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "UPDATE __duckdb_tier_control SET partition_spec = NULL WHERE name = 'records';");

        context.Database.EnsureTieredStoresCreated();

        var partitionSpec = context.Database.SqlQueryRaw<string>(
            "SELECT partition_spec AS \"Value\" FROM __duckdb_tier_control WHERE name = 'records'").Single();
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

        var result = await context.Database.ArchiveTierAsync<Record>(cutoff);

        // Simulate a crash after COPY but before DELETE: the archived rows are back in the hot tables.
        ReinsertArchivedIntoHot(context);
        Assert.Equal(expected, TieredTotals(context)); // views still exact — no double counting

        var heal = await context.Database.ArchiveTierAsync<Record>(cutoff);
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

        await context.Database.ArchiveTierAsync<Record>(cutoff);
        var before = TieredTotals(context);

        context.Records.Add(new Record { EffectiveAt = cutoff.AddMonths(-1) });
        context.SaveChanges();

        Assert.Equal(before.Records + 1, context.RecordHistory.Count());

        var rerun = await context.Database.ArchiveTierAsync<Record>(cutoff);

        Assert.True(rerun.NoOp);
        Assert.Equal(before.Records + 1, context.RecordHistory.Count());
    }

    [Fact]
    public async Task Noop_archive_rerun_does_not_delete_late_hot_rows_before_existing_watermark()
    {
        using var context = CreateContext();
        Seed(context, months: 18, baseDate: new DateTime(2025, 7, 1));
        var cutoff = new DateTime(2025, 7, 1).AddMonths(-12);

        await context.Database.ArchiveTierAsync<Record>(cutoff);
        var hotRowsAfterArchive = context.Records.Count();

        context.Records.Add(new Record { EffectiveAt = cutoff.AddMonths(-1) });
        context.SaveChanges();

        Assert.Equal(hotRowsAfterArchive + 1, context.Records.Count());

        var rerun = await context.Database.ArchiveTierAsync<Record>(cutoff);

        Assert.True(rerun.NoOp);
        Assert.Equal(hotRowsAfterArchive + 1, context.Records.Count());
    }

    [Fact]
    public void Cold_files_without_a_watermark_do_not_hide_hot_rows_when_views_are_regenerated()
    {
        using var context = CreateContext();
        Seed(context, months: 3, baseDate: new DateTime(2025, 7, 1));
        var archive = Path.Combine(_root, "archive", "records");
        Directory.CreateDirectory(archive);

#pragma warning disable EF1002, EF1003 // archive is a test-owned temp path, not user input
        context.Database.ExecuteSqlRaw(
            $"""
             COPY (
                 SELECT "Id", "EffectiveAt", year("EffectiveAt") AS "year", month("EffectiveAt") AS "month"
                   FROM records
             )
             TO '{archive.Replace("'", "''")}'
             (FORMAT PARQUET, PARTITION_BY ("year", "month"), OVERWRITE_OR_IGNORE);
             """);
#pragma warning restore EF1002, EF1003

        context.Database.EnsureTieredStoresCreated();

        Assert.Equal(context.Records.Count(), context.RecordHistory.Count());
    }

    [Fact]
    public void Tiered_storage_honors_schema_mapped_hot_tables()
    {
        using var context = new SchemaContext(Path.Combine(_root, "schema.duckdb"), Path.Combine(_root, "schema-archive"));

        context.Database.EnsureCreated();
        context.Records.Add(new Record { EffectiveAt = new DateTime(2025, 6, 1) });
        context.SaveChanges();

        Assert.Equal(1, context.RecordHistory.Count());
    }

    [Fact]
    public void Purge_skips_malformed_partition_directories()
    {
        using var context = CreateContext();
        Directory.CreateDirectory(Path.Combine(_root, "archive", "records", "year=2024", "month=99"));

        var purged = context.Database.PurgeArchiveOlderThan<Record>(new DateTime(2025, 1, 1));

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
            await context.Database.ArchiveTierAsync<Record>(new DateTime(2025, 7, 1).AddMonths(-12));
        }

        using (var evolved = new EvolvedContext(dbPath, archivePath))
        {
            evolved.Database.ExecuteSqlRaw("ALTER TABLE records ADD COLUMN \"Note\" TEXT;");
            evolved.Database.EnsureTieredStoresCreated(); // regenerate the view over the new schema

            var all = evolved.RecordHistory.OrderBy(i => i.Id).ToList();
            Assert.Equal(18, all.Count);
            Assert.All(all.Where(i => i.Id <= 5), i => Assert.Null(i.Note)); // the 5 archived (cold) records
        }
    }

    [Fact]
    public async Task Nullable_column_contract_change_can_be_planned_and_rewritten_immutably()
    {
        var dbPath = Path.Combine(_root, "contract-rewrite.duckdb");
        var archivePath = Path.Combine(_root, "contract-rewrite-archive");
        using (var original = new RecordContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            original.Records.Add(new Record
            {
                GroupId = 7,
                EffectiveAt = new DateTime(2024, 1, 15),
            });
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));
        }

        using var evolved = new EvolvedContext(dbPath, archivePath);
        evolved.Database.ExecuteSqlRaw("ALTER TABLE records ADD COLUMN \"Note\" TEXT;");
        var inspection = await evolved.Database.InspectArchiveContractAsync<RecordV2>();

        var difference = Assert.Single(inspection.Differences, item =>
            item.Kind == TierArchiveContractDifferenceKind.ColumnAdded);
        Assert.Equal("Note", difference.Column);
        Assert.True(inspection.IsCompatible);

        var plan = await evolved.Database.PlanArchiveContractRewriteAsync<RecordV2>(
            new TierArchiveRewriteOptions());
        var result = await evolved.Database.RewriteArchiveContractAsync<RecordV2>(plan);

        Assert.Equal(TierArchiveOperation.RewriteContract, result.Operation);
        Assert.NotNull(result.Revision);
        Assert.Null(evolved.RecordHistory.Single().Note);
        var afterRewrite = await evolved.Database.InspectArchiveContractAsync<RecordV2>();
        Assert.True(afterRewrite.IsCompatible);
        Assert.Empty(afterRewrite.Differences);
        var laterMaintenance = await evolved.Database.CompactArchiveTierAsync<RecordV2>();
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
            original.Records.Add(new Record
            {
                GroupId = 7,
                EffectiveAt = new DateTime(2024, 1, 15),
            });
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<Record>(new DateTime(2024, 2, 1));
        }

        using var evolved = new AliasedContractEvolvedContext(dbPath, archivePath);
        evolved.Database.ExecuteSqlRaw("ALTER TABLE records ADD COLUMN \"Note\" TEXT;");
        var inspection = await evolved.Database.InspectArchiveContractAsync<RecordV2>();

        var difference = Assert.Single(inspection.Differences, item =>
            item.Kind == TierArchiveContractDifferenceKind.ColumnAdded);
        Assert.Equal("Note", difference.Column);
        Assert.True(inspection.IsCompatible);

        var plan = await evolved.Database.PlanArchiveContractRewriteAsync<RecordV2>(
            new TierArchiveRewriteOptions());
        var result = await evolved.Database.RewriteArchiveContractAsync<RecordV2>(plan);

        Assert.Equal(TierArchiveOperation.RewriteContract, result.Operation);
        Assert.True(Directory.Exists(Path.Combine(
            result.ArchivePath, "group_key=7", "record_month=2024-01-01")));
        Assert.Null(evolved.RecordHistory.Single().Note);
        var afterRewrite = await evolved.Database.InspectArchiveContractAsync<RecordV2>();
        Assert.True(afterRewrite.IsCompatible);
        Assert.Empty(afterRewrite.Differences);
    }

    private void ReinsertArchivedIntoHot(RecordContext context)
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

        // Foreign-key root: parents before children.
        Copy("records", "\"Id\", \"GroupId\", \"EffectiveAt\"");
        Copy("record_lines", "\"Id\", \"RecordId\", \"Value\"");
        Copy("line_details", "\"Id\", \"RecordPartId\", \"Value\"");
    }

    private static (int Records, int Parts, int Details, decimal AllocSum) TieredTotals(RecordContext c)
        => (c.RecordHistory.Count(), c.PartHistory.Count(), c.DetailHistory.Count(), c.DetailHistory.Sum(a => a.Value));

    private static void Seed(RecordContext context, int months, DateTime baseDate)
    {
        for (var m = months - 1; m >= 0; m--)
        {
            var record = new Record { EffectiveAt = baseDate.AddMonths(-m) };
            for (var part = 0; part < 2; part++)
            {
                var value = (m + 1) * 10 + part;
                record.Parts.Add(new RecordPart { Value = value, Details = { new RecordPartDetail { Value = value } } });
            }

            context.Records.Add(record);
        }

        context.SaveChanges();
    }

    private RecordContext CreateContext()
    {
        var context = new RecordContext(Path.Combine(_root, "store.duckdb"), Path.Combine(_root, "archive"));
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class Record
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public DateTime EffectiveAt { get; set; }
        public List<RecordPart> Parts { get; set; } = [];
    }

    private sealed class RecordPart
    {
        public int Id { get; set; }
        public int RecordId { get; set; }
        public Record? Record { get; set; }
        public decimal Value { get; set; }
        public List<RecordPartDetail> Details { get; set; } = [];
    }

    private sealed class RecordPartDetail
    {
        public int Id { get; set; }
        public int RecordPartId { get; set; }
        public RecordPart? RecordPart { get; set; }
        public decimal Value { get; set; }
    }

    private sealed class RecordRm { public int Id { get; set; } public DateTime EffectiveAt { get; set; } }
    private sealed class RecordPartRm { public int Id { get; set; } public int RecordId { get; set; } public decimal Value { get; set; } }
    private sealed class RecordPartDetailRm { public int Id { get; set; } public int RecordPartId { get; set; } public decimal Value { get; set; } }
    private sealed class GroupRecordRm { public int Id { get; set; } public int GroupId { get; set; } public DateTime EffectiveAt { get; set; } }

    private sealed class PartitionedRecord
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public DateTime EffectiveAt { get; set; }
        public List<PartitionedRecordPart> PartitionedParts { get; set; } = [];
    }

    private sealed class PartitionedRecordPart
    {
        public int Id { get; set; }
        public int PartitionedRecordId { get; set; }
        public int GroupId { get; set; }
        public PartitionedRecord? Root { get; set; }
    }

    private sealed class PartitionedRecordRm
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public DateTime EffectiveAt { get; set; }
    }

    private sealed class PartitionedRecordPartRm
    {
        public int Id { get; set; }
        public int PartitionedRecordId { get; set; }
        public int GroupId { get; set; }
    }

    private sealed class DateBucketRecord
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ReviewedAt { get; set; }
        public DateTime EffectiveAt { get; set; }
    }

    private sealed class DateBucketRecordRm
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ReviewedAt { get; set; }
        public DateTime EffectiveAt { get; set; }
    }

    private sealed class TypedPartitionRoot
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
        public int GroupId { get; set; }
        public string Region { get; set; } = null!;
        public bool IsPriority { get; set; }
        public decimal ValueBand { get; set; }
        public DateTime SnapshotAt { get; set; }
        public DateOnly EffectiveDate { get; set; }
        public Guid TenantId { get; set; }
    }

    private sealed class TypedPartitionRm
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
        public int GroupId { get; set; }
        public string Region { get; set; } = null!;
        public bool IsPriority { get; set; }
        public decimal ValueBand { get; set; }
        public DateTime SnapshotAt { get; set; }
        public DateOnly EffectiveDate { get; set; }
        public Guid TenantId { get; set; }
    }

    private interface IArchivePathContext
    {
        string ArchivePath { get; }
    }

    private static DbContextOptionsBuilder ConfigureTieredContext(DbContextOptionsBuilder options, string dbPath)
        => options.UseDuckDB($"Data Source={dbPath}")
            .ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>()
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

    private sealed class RecordContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Record> Records => Set<Record>();
        public DbSet<RecordRm> RecordHistory => Set<RecordRm>();
        public DbSet<RecordPartRm> PartHistory => Set<RecordPartRm>();
        public DbSet<RecordPartDetailRm> DetailHistory => Set<RecordPartDetailRm>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(b =>
            {
                b.ToTable("records");
                b.HasKey(i => i.Id);
                b.HasMany(i => i.Parts).WithOne(l => l.Record).HasForeignKey(l => l.RecordId);
            });
            modelBuilder.Entity<RecordPart>(b =>
            {
                b.ToTable("record_lines");
                b.HasKey(l => l.Id);
                b.HasMany(l => l.Details).WithOne(a => a.RecordPart).HasForeignKey(a => a.RecordPartId);
            });
            modelBuilder.Entity<RecordPartDetail>(b => { b.ToTable("line_details"); b.HasKey(a => a.Id); });

            modelBuilder.ToTieredStore<Record>(i => i.EffectiveAt, archivePath, TierGranularity.Month)
                .WithReadModel<RecordRm>()
                .Including<RecordPart>(i => i.Parts, part => part
                    .WithReadModel<RecordPartRm>()
                    .Including<RecordPartDetail>(l => l.Details, detail => detail.WithReadModel<RecordPartDetailRm>()));
        }
    }

    private sealed class GroupPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Record> Records => Set<Record>();
        public DbSet<GroupRecordRm> RecordHistory => Set<GroupRecordRm>();
        public DbSet<RecordPartRm> PartHistory => Set<RecordPartRm>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.HasMany(record => record.Parts).WithOne(part => part.Record).HasForeignKey(part => part.RecordId);
            });
            modelBuilder.Entity<RecordPart>(builder =>
            {
                builder.ToTable("record_lines");
                builder.HasKey(part => part.Id);
                builder.Ignore(part => part.Details);
            });

            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath, TierGranularity.Month)
                .PartitionBy(partitions => partitions
                    .By(record => record.GroupId)
                    .ByMonth(record => record.EffectiveAt))
                .WithReadModel<GroupRecordRm>()
                .Including<RecordPart>(record => record.Parts, part => part.WithReadModel<RecordPartRm>());
        }
    }

    private sealed class AliasedPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<PartitionedRecord> PartitionedRecords => Set<PartitionedRecord>();
        public DbSet<PartitionedRecordPart> PartitionedParts => Set<PartitionedRecordPart>();
        public DbSet<PartitionedRecordRm> PartitionedRecordHistory => Set<PartitionedRecordRm>();
        public DbSet<PartitionedRecordPartRm> PartitionedPartHistory => Set<PartitionedRecordPartRm>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => ConfigureAliasedPartitionModel(modelBuilder, archivePath, "root_group_id");
    }

    private sealed class ChangedAliasedPartitionContext(
        string dbPath,
        string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => ConfigureAliasedPartitionModel(modelBuilder, archivePath, "root_group_key");
    }

    private sealed class ShorthandAliasPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Record> Records => Set<Record>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .PartitionBy(record => record.GroupId, "group_key");
        }
    }

    private sealed class AliasedContractContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Record> Records => Set<Record>();
        public string ArchivePath => archivePath + "|aliased-contract";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .PartitionBy(partitions => partitions
                    .By(record => record.GroupId, "group_key")
                    .ByMonth(record => record.EffectiveAt, "record_month"))
                .WithReadModel<RecordRm>();
        }
    }

    private sealed class MonthFirstPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Record> Records => Set<Record>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .PartitionBy(partitions => partitions
                    .ByMonth(record => record.EffectiveAt)
                    .By(record => record.GroupId));
        }
    }

    private sealed class DateBucketPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<DateBucketRecord> Records => Set<DateBucketRecord>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DateBucketRecord>(builder =>
            {
                builder.ToTable("date_bucket_records");
                builder.HasKey(record => record.Id);
            });
            modelBuilder.ToTieredStore<DateBucketRecord>(record => record.EffectiveAt, archivePath, TierGranularity.Day)
                .PartitionBy(partitions => partitions
                    .By(record => record.GroupId)
                    .ByYear(record => record.CreatedAt)
                    .ByMonth(record => record.ReviewedAt)
                    .ByDay(record => record.EffectiveAt))
                .WithReadModel<DateBucketRecordRm>();
        }
    }

    private sealed class TypedPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<TypedPartitionRoot> Roots => Set<TypedPartitionRoot>();
        public DbSet<TypedPartitionRm> History => Set<TypedPartitionRm>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TypedPartitionRoot>(builder =>
            {
                builder.ToTable("typed_roots");
                builder.HasKey(root => root.Id);
                builder.Property(root => root.GroupId).HasColumnName("group_id");
                builder.Property(root => root.Region).HasColumnName("region_code");
                builder.Property(root => root.ValueBand).HasPrecision(10, 2);
            });
            modelBuilder.Entity<TypedPartitionRm>(builder =>
            {
                builder.Property(root => root.GroupId).HasColumnName("group_id");
                builder.Property(root => root.Region).HasColumnName("region_code");
                builder.Property(root => root.ValueBand).HasPrecision(10, 2);
            });

            modelBuilder.ToTieredStore<TypedPartitionRoot>(root => root.ArchivedAt, archivePath)
                .PartitionBy(root => root.GroupId, root => root.Region)
                .PartitionBy(
                    root => root.IsPriority,
                    root => root.ValueBand,
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
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath, TierGranularity.Day)
                .WithReadModel<RecordRm>();
        }
    }

    private sealed class DuplicatePartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .PartitionBy(record => record.GroupId)
                .PartitionBy(record => record.GroupId);
        }
    }

    private sealed class ExactLifecyclePartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .PartitionBy(record => record.EffectiveAt);
        }
    }

    private sealed class RepeatedExactLifecyclePartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .PartitionBy(record => record.GroupId)
                .PartitionBy(record => record.EffectiveAt);
        }
    }

    private sealed class MissingLifecyclePartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .PartitionBy(partitions => partitions.By(record => record.GroupId));
        }
    }

    private sealed class NonDateTransformContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .PartitionBy(partitions => partitions
                    .ByMonth(record => record.GroupId)
                    .ByMonth(record => record.EffectiveAt));
        }
    }

    private sealed class EmptyPartitionAliasContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .PartitionBy(partitions => partitions
                    .By(record => record.GroupId, " ")
                    .ByMonth(record => record.EffectiveAt));
        }
    }

    private sealed class RootColumnAliasCollisionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .PartitionBy(partitions => partitions
                    .By(record => record.GroupId, "Id")
                    .ByMonth(record => record.EffectiveAt));
        }
    }

    private sealed class DuplicatePartitionAliasContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .PartitionBy(partitions => partitions
                    .By(record => record.GroupId, "bucket")
                    .ByMonth(record => record.EffectiveAt, "BUCKET"));
        }
    }

    private sealed class BadNavigationContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Record has no relationship to RecordPart, so the .Including navigation has no foreign key.
            modelBuilder.Entity<Record>(b => { b.ToTable("records"); b.HasKey(i => i.Id); b.Ignore(i => i.Parts); });
            modelBuilder.Entity<RecordPart>(b => { b.ToTable("record_lines"); b.HasKey(l => l.Id); b.Ignore(l => l.Details); });
            modelBuilder.ToTieredStore<Record>(i => i.EffectiveAt, archivePath)
                .Including<RecordPart>(i => i.Parts);
        }
    }

    private sealed class BadChildPartitionContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.HasMany(record => record.Parts).WithOne(part => part.Record).HasForeignKey(part => part.RecordId);
            });
            modelBuilder.Entity<RecordPart>(builder =>
            {
                builder.ToTable("record_lines");
                builder.HasKey(part => part.Id);
                builder.Ignore(part => part.Details);
            });
            modelBuilder.ToTieredStore<Record>(record => record.EffectiveAt, archivePath)
                .Including<RecordPart>(record => record.Parts);

            // Simulates malformed metadata from a manually-authored convention or compiled model.
            modelBuilder.Entity<RecordPart>().HasAnnotation("DuckDB:TieredStore:PartitionProperties", "Value");
        }
    }

    private sealed class BadReadModelContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(b => { b.ToTable("records"); b.HasKey(i => i.Id); b.Ignore(i => i.Parts); });
            // MismatchRm has a column the records table does not.
            modelBuilder.ToTieredStore<Record>(i => i.EffectiveAt, archivePath).WithReadModel<MismatchRm>();
        }

        private sealed class MismatchRm { public int Id { get; set; } public DateTime EffectiveAt { get; set; } public string? Nonexistent { get; set; } }
    }

    private sealed class Ledger { public int Id { get; set; } public DateTime PostedAt { get; set; } }

    // Maps a property to the reserved hive partition column name "year".
    private sealed class ReservedColumnContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(b =>
            {
                b.ToTable("records");
                b.HasKey(i => i.Id);
                b.Ignore(i => i.Parts);
                b.Property(i => i.Id).HasColumnName("year");
            });
            modelBuilder.ToTieredStore<Record>(i => i.EffectiveAt, archivePath);
        }
    }

    // Two aggregate roots sharing the same archive directory.
    private sealed class OverlapContext(string dbPath, string archiveRoot) : DbContext, IArchivePathContext
    {
        public string ArchivePath => archiveRoot;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(b => { b.ToTable("records"); b.HasKey(i => i.Id); b.Ignore(i => i.Parts); });
            modelBuilder.Entity<Ledger>(b => { b.ToTable("ledger"); b.HasKey(l => l.Id); });
            modelBuilder.ToTieredStore<Record>(i => i.EffectiveAt, archiveRoot);
            modelBuilder.ToTieredStore<Ledger>(l => l.PostedAt, archiveRoot);
        }
    }

    // Second-generation root model over the same "records" table/archive, with an added column.
    private sealed class RecordV2
    {
        public int Id { get; set; }
        public int GroupId { get; set; }
        public DateTime EffectiveAt { get; set; }
        public string? Note { get; set; }
        public List<RecordPartV2> Parts { get; set; } = [];
    }

    private sealed class RecordPartV2
    {
        public int Id { get; set; }
        public int RecordId { get; set; }
        public RecordV2? Record { get; set; }
        public decimal Value { get; set; }
        public List<RecordPartDetailV2> Details { get; set; } = [];
    }

    private sealed class RecordPartDetailV2
    {
        public int Id { get; set; }
        public int RecordPartId { get; set; }
        public RecordPartV2? RecordPart { get; set; }
        public decimal Value { get; set; }
    }

    private sealed class RecordV2Rm { public int Id { get; set; } public DateTime EffectiveAt { get; set; } public string? Note { get; set; } }

    private sealed class EvolvedContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<RecordV2Rm> RecordHistory => Set<RecordV2Rm>();
        public string ArchivePath => archivePath + "|evolved";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RecordV2>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.HasMany(record => record.Parts).WithOne(part => part.Record).HasForeignKey(part => part.RecordId);
            });
            modelBuilder.Entity<RecordPartV2>(builder =>
            {
                builder.ToTable("record_lines");
                builder.HasKey(part => part.Id);
                builder.HasMany(part => part.Details).WithOne(detail => detail.RecordPart)
                    .HasForeignKey(detail => detail.RecordPartId);
            });
            modelBuilder.Entity<RecordPartDetailV2>(builder =>
            {
                builder.ToTable("line_details");
                builder.HasKey(detail => detail.Id);
            });
            modelBuilder.ToTieredStore<RecordV2>(i => i.EffectiveAt, archivePath, TierGranularity.Month)
                .WithReadModel<RecordV2Rm>()
                .Including<RecordPartV2>(record => record.Parts, part => part
                    .Including<RecordPartDetailV2>(item => item.Details));
        }
    }

    private sealed class AliasedContractEvolvedContext(
        string dbPath,
        string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<RecordV2Rm> RecordHistory => Set<RecordV2Rm>();
        public string ArchivePath => archivePath + "|aliased-contract-evolved";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RecordV2>(builder =>
            {
                builder.ToTable("records");
                builder.HasKey(record => record.Id);
                builder.Ignore(record => record.Parts);
            });
            modelBuilder.ToTieredStore<RecordV2>(record => record.EffectiveAt, archivePath)
                .PartitionBy(partitions => partitions
                    .By(record => record.GroupId, "group_key")
                    .ByMonth(record => record.EffectiveAt, "record_month"))
                .WithReadModel<RecordV2Rm>();
        }
    }

    private sealed class SchemaContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<Record> Records => Set<Record>();
        public DbSet<RecordRm> RecordHistory => Set<RecordRm>();
        public string ArchivePath => archivePath + "|schema";

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => ConfigureTieredContext(options, dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Record>(b =>
            {
                b.ToTable("records", "accounting");
                b.HasKey(i => i.Id);
                b.Ignore(i => i.Parts);
            });

            modelBuilder.ToTieredStore<Record>(i => i.EffectiveAt, archivePath, TierGranularity.Month)
                .WithReadModel<RecordRm>();
        }
    }

    private sealed class ArchivePathModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), (context as IArchivePathContext)?.ArchivePath, designTime);
    }

    private static void ConfigureAliasedPartitionModel(
        ModelBuilder modelBuilder,
        string archivePath,
        string ownerPartitionName)
    {
        modelBuilder.Entity<PartitionedRecord>(builder =>
        {
            builder.ToTable("partitioned_records");
            builder.HasKey(root => root.Id);
            builder.HasMany(root => root.PartitionedParts).WithOne(item => item.Root).HasForeignKey(item => item.PartitionedRecordId);
        });
        modelBuilder.Entity<PartitionedRecordPart>(builder =>
        {
            builder.ToTable("partitioned_record_parts");
            builder.HasKey(item => item.Id);
        });

        modelBuilder.ToTieredStore<PartitionedRecord>(root => root.EffectiveAt, archivePath)
            .PartitionBy(partitions => partitions
                .By(root => root.GroupId, ownerPartitionName)
                .ByMonth(root => root.EffectiveAt, "effective_month"))
            .WithReadModel<PartitionedRecordRm>()
            .Including<PartitionedRecordPart>(
                root => root.PartitionedParts,
                items => items.WithReadModel<PartitionedRecordPartRm>());
    }
}
