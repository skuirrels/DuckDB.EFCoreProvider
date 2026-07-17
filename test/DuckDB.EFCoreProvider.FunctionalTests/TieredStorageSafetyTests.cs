using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class TieredStorageSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "duckdb-tier-safety-" + Guid.NewGuid().ToString("N"));

    public TieredStorageSafetyTests() => Directory.CreateDirectory(_root);

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
    public async Task Nullable_lifecycle_stays_hot_then_archives_after_becoming_eligible()
    {
        var archivePath = Path.Combine(_root, "nullable-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "nullable.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.AddRange(
            CreateRecord("pending", effectiveAt: null),
            CreateRecord("january", new DateTime(2024, 1, 15)),
            CreateRecord("february", new DateTime(2024, 2, 15)));
        context.SaveChanges();

        var first = await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 28));

        Assert.Equal(new DateTime(2024, 2, 1), first.Watermark);
        Assert.Equal(1, first.RowsArchived);
        Assert.Equal(TierArchiveOperation.Archive, first.Operation);
        Assert.Null(first.PreviousWatermark);
        Assert.Equal(DateTime.MinValue, first.WindowStart);
        Assert.Equal(new DateTime(2024, 2, 1), first.WindowEnd);
        Assert.Equal(TierArchiveStage.Completed, first.Stage);
        var rootEvidence = Assert.Single(first.Nodes, node => node.Table == "stable_records");
        Assert.Equal(1, rootEvidence.SelectedRows);
        Assert.Equal(1, rootEvidence.CopiedRows);
        Assert.Equal(1, rootEvidence.DeletedRows);
        Assert.NotEmpty(rootEvidence.Files);
        context.ChangeTracker.Clear();
        Assert.Equal(2, context.Records.Count());
        Assert.Single(context.Records.Where(record => record.EffectiveAt == null));
        Assert.Equal(3, context.RecordHistory.Count());
        Assert.True(Directory.Exists(Path.Combine(
            archivePath, "stable_records", "EffectiveAt_month=2024-01-01")));
        var archiveSpec = context.Database.SqlQueryRaw<string>(
            "SELECT archive_spec AS \"Value\" FROM __duckdb_tier_control WHERE name = 'stable_records'").Single();
        Assert.Contains("\"LifecycleColumn\":\"EffectiveAt\"", archiveSpec);
        Assert.Contains("\"MatchKeyColumns\":[\"ExternalId\"]", archiveSpec);

        var pending = context.Records.Single(record => record.ExternalId == "pending");
        pending.EffectiveAt = new DateTime(2024, 2, 20);
        context.SaveChanges();

        var second = await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 3, 19));

        Assert.Equal(new DateTime(2024, 3, 1), second.Watermark);
        Assert.Equal(2, second.RowsArchived);
        context.ChangeTracker.Clear();
        Assert.Empty(context.Records);
        Assert.Equal(3, context.RecordHistory.Count());
        Assert.Equal(2, context.RecordHistory.Count(
            record => record.EffectiveAt >= new DateTime(2024, 2, 1)
                     && record.EffectiveAt < new DateTime(2024, 3, 1)));
        Assert.Contains(
            "EffectiveAt_month",
            context.RecordHistory.Where(record => record.EffectiveAt >= new DateTime(2024, 2, 1)).ToQueryString());
    }

    [Fact]
    public async Task Null_only_lifecycle_set_advances_watermark_without_moving_or_hiding_rows()
    {
        var archivePath = Path.Combine(_root, "null-only-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "null-only.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord("pending", effectiveAt: null));
        context.SaveChanges();

        var result = await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));
        var retry = await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));

        Assert.Equal(0, result.RowsArchived);
        Assert.Equal(new DateTime(2024, 2, 1), result.Watermark);
        Assert.True(retry.NoOp);
        Assert.Single(context.Records);
        Assert.Single(context.RecordHistory);
        Assert.False(Directory.Exists(Path.Combine(archivePath, "stable_records")));
    }

    [Fact]
    public async Task Stable_root_and_composite_child_keys_recover_a_surrogate_key_replay()
    {
        var archivePath = Path.Combine(_root, "replay-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "replay.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord(
            "record-1",
            new DateTime(2024, 1, 15),
            new StableRecordPart { RecordExternalKey = "record-1", PartCode = "A", Value = 12m }));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));

        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_records (\"Id\", \"EffectiveAt\", \"ExternalId\", \"Status\") "
            + "VALUES (101, TIMESTAMP '2024-01-15', 'record-1', 'complete');");
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_record_parts (\"Id\", \"Value\", \"RecordExternalKey\", \"PartCode\", \"RecordId\") "
            + "VALUES (201, 12, 'record-1', 'A', 101);");

        // Model a stop after watermark publication but before view publication: retry must repair the views
        // before it removes the replayed hot graph.
        context.Database.ExecuteSqlRaw(
            "CREATE OR REPLACE VIEW stable_records_tiered AS SELECT * FROM stable_records;");
        context.Database.ExecuteSqlRaw(
            "CREATE OR REPLACE VIEW stable_record_parts_tiered AS SELECT * FROM stable_record_parts;");

        var retry = await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));

        Assert.True(retry.NoOp);
        context.ChangeTracker.Clear();
        Assert.Empty(context.Records);
        Assert.Empty(context.Parts);
        Assert.Single(context.RecordHistory);
        Assert.Single(context.PartHistory);
        Assert.Equal(
            ["ExternalId"],
            context.Model.FindEntityType(typeof(StableRecord))!.GetTieredStoreMatchProperties());
        Assert.Equal(
            ["RecordExternalKey", "PartCode"],
            context.Model.FindEntityType(typeof(StableRecordPart))!.GetTieredStoreMatchProperties());
    }

    [Fact]
    public async Task Corrected_archived_key_is_rejected_and_preserved_hot()
    {
        var archivePath = Path.Combine(_root, "correction-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "correction.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord("record-1", new DateTime(2024, 1, 15)));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_records (\"Id\", \"EffectiveAt\", \"ExternalId\", \"Status\") "
            + "VALUES (101, TIMESTAMP '2024-01-15', 'record-1', 'corrected');");

        var exception = await Assert.ThrowsAsync<TierArchivedKeyConflictException>(
            () => context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1)));

        Assert.Equal(typeof(StableRecord), exception.EntityType);
        Assert.Equal(1, exception.ConflictingRows);
        Assert.Equal("stable_records", exception.Binding?.ControlKey);
        Assert.Contains("control 'stable_records'", exception.Message);
        Assert.Equal("corrected", context.Records.Single().Status);
    }

    [Fact]
    public async Task Reopened_archived_key_is_rejected_and_preserved_hot()
    {
        var archivePath = Path.Combine(_root, "reopened-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "reopened.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord("record-1", new DateTime(2024, 1, 15)));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_records (\"Id\", \"EffectiveAt\", \"ExternalId\", \"Status\") "
            + "VALUES (101, NULL, 'record-1', 'complete');");

        var exception = await Assert.ThrowsAsync<TierArchivedKeyConflictException>(
            () => context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1)));

        Assert.Equal(typeof(StableRecord), exception.EntityType);
        Assert.Null(context.Records.Single().EffectiveAt);
    }

    [Fact]
    public async Task Corrected_composite_child_key_is_rejected_and_preserved_hot()
    {
        var archivePath = Path.Combine(_root, "child-correction-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "child-correction.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord(
            "record-1",
            new DateTime(2024, 1, 15),
            new StableRecordPart { RecordExternalKey = "record-1", PartCode = "A", Value = 12m }));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_records (\"Id\", \"EffectiveAt\", \"ExternalId\", \"Status\") "
            + "VALUES (101, TIMESTAMP '2024-01-15', 'record-1', 'complete');");
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_record_parts (\"Id\", \"Value\", \"RecordExternalKey\", \"PartCode\", \"RecordId\") "
            + "VALUES (201, 99, 'record-1', 'A', 101);");

        var exception = await Assert.ThrowsAsync<TierArchivedKeyConflictException>(
            () => context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1)));

        Assert.Equal(typeof(StableRecordPart), exception.EntityType);
        Assert.Equal(99m, context.Parts.Single().Value);
    }

    [Fact]
    public async Task Approved_correction_and_late_unseen_row_publish_a_new_cold_generation()
    {
        var archivePath = Path.Combine(_root, "reconcile-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "reconcile.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord("record-1", new DateTime(2024, 1, 15)));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_records (\"Id\", \"EffectiveAt\", \"ExternalId\", \"Status\") VALUES "
            + "(101, TIMESTAMP '2024-01-15', 'record-1', 'corrected'), "
            + "(102, TIMESTAMP '2024-01-20', 'late-unseen', 'complete');");

        var result = await context.Database.ReconcileArchiveTierAsync<StableRecord>();

        Assert.Equal(TierArchiveOperation.Reconcile, result.Operation);
        Assert.False(result.NoOp);
        Assert.Equal(2, result.RowsArchived);
        Assert.NotNull(result.Revision);
        Assert.Contains("/_revisions/", result.ArchivePath.Replace('\\', '/'));
        var rootEvidence = Assert.Single(result.Nodes, node => node.Table == "stable_records");
        Assert.Equal(2, rootEvidence.SelectedRows);
        Assert.Equal(2, rootEvidence.CopiedRows);
        Assert.Equal(2, rootEvidence.DeletedRows);
        Assert.Empty(context.Records);
        Assert.Equal(2, context.RecordHistory.Count());
        Assert.Equal("corrected", context.RecordHistory.Single(record => record.ExternalId == "record-1").Status);
        Assert.Equal("complete", context.RecordHistory.Single(record => record.ExternalId == "late-unseen").Status);
        Assert.True(Directory.Exists(Path.Combine(archivePath, "stable_records")));
    }

    [Fact]
    public async Task Approved_composite_child_correction_is_reconciled_with_its_root_replay()
    {
        var archivePath = Path.Combine(_root, "reconcile-child-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "reconcile-child.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord(
            "record-1",
            new DateTime(2024, 1, 15),
            new StableRecordPart { RecordExternalKey = "record-1", PartCode = "A", Value = 12m }));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_records (\"Id\", \"EffectiveAt\", \"ExternalId\", \"Status\") "
            + "VALUES (101, TIMESTAMP '2024-01-15', 'record-1', 'complete');");
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_record_parts (\"Id\", \"Value\", \"RecordExternalKey\", \"PartCode\", \"RecordId\") "
            + "VALUES (201, 99, 'record-1', 'A', 101);");

        var result = await context.Database.ReconcileArchiveTierAsync<StableRecord>();

        Assert.Equal(1, result.RowsArchived);
        Assert.Empty(context.Records);
        Assert.Empty(context.Parts);
        Assert.Single(context.RecordHistory);
        Assert.Equal(99m, context.PartHistory.Single().Value);
        Assert.All(result.Nodes, node => Assert.Equal(node.SelectedRows, node.CopiedRows));
    }

    [Fact]
    public async Task Scoped_reconciliation_changes_only_the_selected_root_key()
    {
        var archivePath = Path.Combine(_root, "scoped-reconcile-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "scoped-reconcile.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.AddRange(
            CreateRecord("record-1", new DateTime(2024, 1, 15)),
            CreateRecord("record-2", new DateTime(2024, 1, 16)));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_records (\"Id\", \"EffectiveAt\", \"ExternalId\", \"Status\") VALUES "
            + "(101, TIMESTAMP '2024-01-15', 'record-1', 'corrected-1'), "
            + "(102, TIMESTAMP '2024-01-16', 'record-2', 'corrected-2');");

        var result = await context.Database.ReconcileArchiveTierAsync<StableRecord>(
            new TierReconciliationOptions
            {
                Scope = TierMaintenanceScope.ForRootMatchKeys(
                    TierRowIdentity.For<StableRecord>(
                        new Dictionary<string, object?> { ["ExternalId"] = "record-1" })),
            });

        Assert.False(result.NoOp);
        Assert.Equal("corrected-1", context.RecordHistory.Single(record => record.ExternalId == "record-1").Status);
        Assert.Equal("complete", context.RecordHistory.Single(record => record.ExternalId == "record-2").Status);
        var conflicts = await context.Database.GetArchiveConflictsAsync<StableRecord>();
        var conflict = Assert.Single(conflicts.Keys);
        Assert.Equal("record-2", conflict.Values["ExternalId"]);
    }

    [Fact]
    public async Task Root_tombstone_cascades_through_the_declared_cold_aggregate()
    {
        var archivePath = Path.Combine(_root, "tombstone-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "tombstone.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord(
            "record-1",
            new DateTime(2024, 1, 15),
            new StableRecordPart { RecordExternalKey = "record-1", PartCode = "A", Value = 12m }));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));

        await context.Database.ReconcileArchiveTierAsync<StableRecord>(
            new TierReconciliationOptions
            {
                Tombstones =
                [
                    TierRowIdentity.For<StableRecord>(
                        new Dictionary<string, object?> { ["ExternalId"] = "record-1" }),
                ],
            });

        Assert.Empty(context.RecordHistory);
        Assert.Empty(context.PartHistory);
    }

    [Fact]
    public async Task Restore_moves_an_exact_cold_graph_back_to_hot_and_removes_it_from_cold()
    {
        var archivePath = Path.Combine(_root, "restore-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "restore.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord(
            "record-1",
            new DateTime(2024, 1, 15),
            new StableRecordPart { RecordExternalKey = "record-1", PartCode = "A", Value = 12m }));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));

        var result = await context.Database.RestoreArchiveTierAsync<StableRecord>(
            new TierRestoreOptions
            {
                Scope = TierMaintenanceScope.ForRootMatchKeys(
                    TierRowIdentity.For<StableRecord>(
                        new Dictionary<string, object?> { ["ExternalId"] = "record-1" })),
            });

        Assert.Equal(1, result.RootsSelected);
        Assert.Equal(1, result.RootsInserted);
        Assert.Equal(2, result.RowsInserted);
        Assert.Equal(TierArchiveOperation.Restore, result.Publication.Operation);
        Assert.Single(context.Records);
        Assert.Single(context.Parts);
        Assert.Single(context.RecordHistory);
        Assert.Single(context.PartHistory);
    }

    [Fact]
    public async Task Bounded_manifest_inventory_compaction_and_local_preflight_are_generic()
    {
        var archivePath = Path.Combine(_root, "maintenance-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "maintenance.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord("record-1", new DateTime(2024, 1, 15)));
        context.SaveChanges();

        var archived = await context.Database.ArchiveTierAsync<StableRecord>(
            new DateTime(2024, 2, 1),
            new TierArchiveOptions
            {
                Manifest = new TierManifestOptions { Detail = TierManifestDetail.Summary },
                Writer = new TierParquetWriterOptions
                {
                    Compression = "zstd",
                    CompressionLevel = 3,
                    RowGroupSize = 2048,
                },
            });
        var node = Assert.Single(archived.Nodes, evidence => evidence.Table == "stable_records");
        Assert.Empty(node.Files);
        Assert.True(node.FileCount > 0);
        Assert.True(node.FilesTruncated);

        var inventory = await context.Database.GetArchiveGenerationInventoryAsync<StableRecord>();
        var active = Assert.Single(inventory.Generations, generation =>
            generation.State == TierArchiveGenerationState.Active);
        Assert.Equal(inventory.ActiveGenerationId, active.GenerationId);
        Assert.True(active.FileCount > 0);

        var compacted = await context.Database.CompactArchiveTierAsync<StableRecord>(
            new TierCompactionOptions
            {
                Writer = new TierParquetWriterOptions { Compression = "zstd", CompressionLevel = 5 },
            });
        Assert.Equal(TierArchiveOperation.Compact, compacted.Operation);
        Assert.NotNull(compacted.Revision);

        var preflight = await context.Database.PreflightTieredStorageAsync<StableRecord>(
            new TierStoragePreflightOptions { ProbeWriteAndDelete = true });
        Assert.True(preflight.Succeeded);
        Assert.Contains(preflight.Capabilities, capability =>
            capability.Capability == TierStorageCapability.Write && capability.Supported);
        Assert.Contains(preflight.Capabilities, capability =>
            capability.Capability == TierStorageCapability.Delete && capability.Supported);
    }

    [Fact]
    public async Task Empty_archive_reports_read_as_untested_without_failing_preflight()
    {
        var archivePath = Path.Combine(_root, "empty-preflight-archive");
        Directory.CreateDirectory(archivePath);
        using var context = new StableRecordContext(Path.Combine(_root, "empty-preflight.duckdb"), archivePath);

        var preflight = await context.Database.PreflightTieredStorageAsync<StableRecord>();

        Assert.True(preflight.Succeeded);
        var read = Assert.Single(
            preflight.Capabilities,
            capability => capability.Capability == TierStorageCapability.Read);
        Assert.False(read.Supported);
        Assert.False(read.WasTested);
    }

    [Fact]
    public void Diagnostic_redaction_removes_uri_credentials_query_and_fragment()
    {
        var message = DuckDBArchiveExtensions.RedactDiagnosticMessage(
            new InvalidOperationException(
                "Failed to read https://alice:password@example.test/archive?sig=TOPSECRET#fragment"));

        Assert.DoesNotContain("alice", message, StringComparison.Ordinal);
        Assert.DoesNotContain("password", message, StringComparison.Ordinal);
        Assert.DoesNotContain("TOPSECRET", message, StringComparison.Ordinal);
        Assert.DoesNotContain("fragment", message, StringComparison.Ordinal);
        Assert.Contains("https://example.test/archive", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Writer_filename_pattern_cannot_escape_the_provider_archive_path()
    {
        using var context = new StableRecordContext(
            Path.Combine(_root, "writer-validation.duckdb"),
            Path.Combine(_root, "writer-validation-archive"));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => context.Database.ArchiveTierAsync<StableRecord>(
                new DateTime(2024, 2, 1),
                new TierArchiveOptions
                {
                    Writer = new TierParquetWriterOptions
                    {
                        FilenamePattern = "../outside-{i}.parquet",
                    },
                }));

        Assert.Equal("FilenamePattern", exception.ParamName);
    }

    [Fact]
    public async Task Read_only_diagnostics_do_not_create_provider_tables()
    {
        using var context = new StableRecordContext(
            Path.Combine(_root, "diagnostics.duckdb"),
            Path.Combine(_root, "diagnostics-archive"));

        var contract = await context.Database.InspectArchiveContractAsync<StableRecord>();
        var conflicts = await context.Database.GetArchiveConflictsAsync<StableRecord>();
        var inventory = await context.Database.GetArchiveGenerationInventoryAsync<StableRecord>();

        Assert.Null(contract.PersistedContractJson);
        Assert.Empty(conflicts.Keys);
        Assert.Empty(inventory.Generations);
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM duckdb_tables() WHERE table_name IN "
            + $"('{DuckDBTierControl.ControlTable}', '{DuckDBTierControl.GenerationTable}', "
            + $"'{DuckDBTierControl.GenerationNodeTable}', '{DuckDBTierControl.GenerationFileTable}');";
        Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Composite_child_foreign_key_archives_deletes_and_reads_as_one_aggregate()
    {
        var archivePath = Path.Combine(_root, "composite-fk-archive");
        using var context = new CompositeForeignKeyContext(
            Path.Combine(_root, "composite-fk.duckdb"),
            archivePath);
        context.Database.EnsureCreated();
        context.Roots.Add(new CompositeRoot
        {
            TenantId = 7,
            Id = 11,
            ExternalId = "root-1",
            EffectiveAt = new DateTime(2024, 1, 15),
            Children =
            [
                new CompositeChild
                {
                    Id = 21,
                    TenantId = 7,
                    RootId = 11,
                    ExternalId = "child-1",
                },
            ],
        });
        context.SaveChanges();

        var result = await context.Database.ArchiveTierAsync<CompositeRoot>(new DateTime(2024, 2, 1));

        Assert.Equal(1, result.RowsArchived);
        Assert.Empty(context.Roots);
        Assert.Empty(context.Children);
        Assert.Single(context.RootHistory);
        Assert.Single(context.ChildHistory);
    }

    [Fact]
    public async Task DateOnly_lifecycle_selector_archives_normally()
    {
        using (var dateOnly = new DateOnlyLifecycleContext(
                   Path.Combine(_root, "date-only.duckdb"),
                   Path.Combine(_root, "date-only-archive")))
        {
            dateOnly.Database.EnsureCreated();
            dateOnly.Rows.Add(new DateOnlyLifecycle
            {
                Id = 1,
                ArchivedOn = new DateOnly(2024, 1, 15),
            });
            dateOnly.SaveChanges();

            var result = await dateOnly.Database.ArchiveTierAsync<DateOnlyLifecycle>(
                new DateTime(2024, 2, 1));

            Assert.Equal(1, result.RowsArchived);
            Assert.Empty(dateOnly.Rows);
            Assert.Single(dateOnly.History);
        }
    }

    [Fact]
    public async Task Reconciliation_rejects_a_reopened_or_moved_lifecycle()
    {
        var archivePath = Path.Combine(_root, "reconcile-reopened-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "reconcile-reopened.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord("record-1", new DateTime(2024, 1, 15)));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_records (\"Id\", \"EffectiveAt\", \"ExternalId\", \"Status\") "
            + "VALUES (101, NULL, 'record-1', 'reopened');");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.ReconcileArchiveTierAsync<StableRecord>());

        Assert.Contains("separate restore workflow", exception.Message);
        Assert.Single(context.Records);
        Assert.Equal(2, context.RecordHistory.Count());
    }

    [Fact]
    public async Task Late_unseen_row_remains_hot_on_an_ordinary_no_op_archive()
    {
        var archivePath = Path.Combine(_root, "late-hot-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "late-hot.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord("record-1", new DateTime(2024, 1, 15)));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));
        context.Records.Add(CreateRecord("late-unseen", new DateTime(2024, 1, 20)));
        context.SaveChanges();

        var result = await context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));

        Assert.True(result.NoOp);
        Assert.Single(context.Records);
        Assert.Equal(2, context.RecordHistory.Count());
    }

    [Fact]
    public async Task Match_key_change_is_rejected_after_cold_files_exist()
    {
        var dbPath = Path.Combine(_root, "key-layout.duckdb");
        var archivePath = Path.Combine(_root, "key-layout-archive");
        using (var original = new StableRecordContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            original.Records.Add(CreateRecord("record-1", new DateTime(2024, 1, 15)));
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));
        }

        using var changed = new PrimaryKeyRecordContext(dbPath, archivePath);
        var exception = Assert.Throws<InvalidOperationException>(
            () => changed.Database.EnsureTieredStoresCreated());

        Assert.Contains("match-key layout changed", exception.Message);
        Assert.Contains("Migrate or clear", exception.Message);
    }

    [Fact]
    public async Task Archive_path_change_is_rejected_after_cold_files_exist()
    {
        var dbPath = Path.Combine(_root, "path-layout.duckdb");
        var originalPath = Path.Combine(_root, "path-layout-archive");
        using (var original = new StableRecordContext(dbPath, originalPath))
        {
            original.Database.EnsureCreated();
            original.Records.Add(CreateRecord("record-1", new DateTime(2024, 1, 15)));
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1));
        }

        using var changed = new StableRecordContext(dbPath, Path.Combine(_root, "different-archive"));
        var exception = Assert.Throws<InvalidOperationException>(
            () => changed.Database.EnsureTieredStoresCreated());

        Assert.Contains("archive path changed", exception.Message);
    }

    [Fact]
    public async Task Null_match_key_on_an_archiveable_row_is_rejected_before_copy()
    {
        var archivePath = Path.Combine(_root, "null-key-archive");
        using var context = new NullableMatchKeyContext(Path.Combine(_root, "null-key.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(new NullableMatchRecord
        {
            ExternalId = null,
            EffectiveAt = new DateTime(2024, 1, 15),
        });
        context.SaveChanges();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.ArchiveTierAsync<NullableMatchRecord>(new DateTime(2024, 2, 1)));

        Assert.Contains("NULL configured match-key component", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(archivePath, "nullable_match_records")));
        Assert.Single(context.Records);
    }

    [Fact]
    public async Task Null_child_match_key_is_rejected_before_any_aggregate_table_is_copied()
    {
        var archivePath = Path.Combine(_root, "null-child-key-archive");
        using var context = new StableRecordContext(Path.Combine(_root, "null-child-key.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Records.Add(CreateRecord(
            "record-1",
            new DateTime(2024, 1, 15),
            new StableRecordPart { RecordExternalKey = null, PartCode = "A", Value = 12m }));
        context.SaveChanges();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.ArchiveTierAsync<StableRecord>(new DateTime(2024, 2, 1)));

        Assert.Contains("stable_record_parts", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(archivePath, "stable_records")));
        Assert.Single(context.Records);
        Assert.Single(context.Parts);
    }

    [Fact]
    public void Match_key_requires_model_uniqueness_or_explicit_external_opt_in()
    {
        using var context = new UnprovenMatchKeyContext(
            Path.Combine(_root, "unproven.duckdb"),
            Path.Combine(_root, "unproven-archive"));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("unique index", exception.Message);
        Assert.Contains(nameof(TierMatchKeyUniqueness.ExternallyEnforced), exception.Message);
    }

    [Fact]
    public void Declared_unique_index_satisfies_match_key_validation()
    {
        using var context = new DeclaredUniqueMatchKeyContext(
            Path.Combine(_root, "declared-unique.duckdb"),
            Path.Combine(_root, "declared-unique-archive"));

        var entity = context.Model.FindEntityType(typeof(NullableMatchRecord))!;

        Assert.Equal(["ExternalId"], entity.GetTieredStoreMatchProperties());
        Assert.Equal(TierMatchKeyUniqueness.Model, entity.GetTieredStoreMatchKeyUniqueness());
    }

    [Fact]
    public void Invalid_match_key_uniqueness_is_rejected()
    {
        using var context = new InvalidMatchKeyUniquenessContext(
            Path.Combine(_root, "invalid-uniqueness.duckdb"),
            Path.Combine(_root, "invalid-uniqueness-archive"));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _ = context.Model);

        Assert.Equal("uniqueness", exception.ParamName);
    }

    private static StableRecord CreateRecord(
        string externalId,
        DateTime? effectiveAt,
        params StableRecordPart[] parts)
        => new()
        {
            ExternalId = externalId,
            EffectiveAt = effectiveAt,
            Status = "complete",
            Parts = [.. parts],
        };

    private sealed class StableRecord
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime? EffectiveAt { get; set; }
        public string Status { get; set; } = null!;
        public List<StableRecordPart> Parts { get; set; } = [];
    }

    private sealed class StableRecordPart
    {
        public int Id { get; set; }
        public int RecordId { get; set; }
        public StableRecord? Record { get; set; }
        public string? RecordExternalKey { get; set; }
        public string PartCode { get; set; } = null!;
        public decimal Value { get; set; }
    }

    private sealed class CompositeRoot
    {
        public int TenantId { get; set; }
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime EffectiveAt { get; set; }
        public List<CompositeChild> Children { get; set; } = [];
    }

    private sealed class CompositeChild
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int RootId { get; set; }
        public string ExternalId { get; set; } = null!;
        public CompositeRoot? Root { get; set; }
    }

    private sealed class CompositeRootHistory
    {
        public int TenantId { get; set; }
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime EffectiveAt { get; set; }
    }

    private sealed class CompositeChildHistory
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int RootId { get; set; }
        public string ExternalId { get; set; } = null!;
    }

    private sealed class DateOnlyLifecycle
    {
        public int Id { get; set; }
        public DateOnly ArchivedOn { get; set; }
    }

    private sealed class DateOnlyLifecycleHistory
    {
        public int Id { get; set; }
        public DateOnly ArchivedOn { get; set; }
    }

    private sealed class StableRecordHistory
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime? EffectiveAt { get; set; }
        public string Status { get; set; } = null!;
    }

    private sealed class StableRecordPartHistory
    {
        public int Id { get; set; }
        public int RecordId { get; set; }
        public string? RecordExternalKey { get; set; }
        public string PartCode { get; set; } = null!;
        public decimal Value { get; set; }
    }

    private sealed class StableRecordContext(string dbPath, string archivePath) : DbContext, IArchiveContext
    {
        public DbSet<StableRecord> Records => Set<StableRecord>();
        public DbSet<StableRecordPart> Parts => Set<StableRecordPart>();
        public DbSet<StableRecordHistory> RecordHistory => Set<StableRecordHistory>();
        public DbSet<StableRecordPartHistory> PartHistory => Set<StableRecordPartHistory>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, ArchiveModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureEntities(modelBuilder);
            modelBuilder.ToTieredStore<StableRecord>(record => record.EffectiveAt, archivePath)
                .MatchBy(record => record.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                .PartitionBy(partitions => partitions.ByMonth(record => record.EffectiveAt))
                .WithReadModel<StableRecordHistory>()
                .Including<StableRecordPart>(record => record.Parts, parts => parts
                    .MatchBy(
                        part => new { part.RecordExternalKey, part.PartCode },
                        TierMatchKeyUniqueness.ExternallyEnforced)
                    .WithReadModel<StableRecordPartHistory>());
        }
    }

    private sealed class CompositeForeignKeyContext(string dbPath, string archivePath) : DbContext, IArchiveContext
    {
        public DbSet<CompositeRoot> Roots => Set<CompositeRoot>();
        public DbSet<CompositeChild> Children => Set<CompositeChild>();
        public DbSet<CompositeRootHistory> RootHistory => Set<CompositeRootHistory>();
        public DbSet<CompositeChildHistory> ChildHistory => Set<CompositeChildHistory>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, ArchiveModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CompositeRoot>(builder =>
            {
                builder.ToTable("composite_roots");
                builder.HasKey(root => new { root.TenantId, root.Id });
                builder.HasMany(root => root.Children)
                    .WithOne(child => child.Root)
                    .HasForeignKey(child => new { child.TenantId, child.RootId })
                    .HasPrincipalKey(root => new { root.TenantId, root.Id });
            });
            modelBuilder.Entity<CompositeChild>(builder =>
            {
                builder.ToTable("composite_children");
                builder.HasKey(child => child.Id);
            });
            modelBuilder.ToTieredStore<CompositeRoot>(root => root.EffectiveAt, archivePath)
                .MatchBy(root => root.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                .WithReadModel<CompositeRootHistory>()
                .Including<CompositeChild>(root => root.Children, child => child
                    .MatchBy(part => part.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                    .WithReadModel<CompositeChildHistory>());
        }
    }

    private sealed class DateOnlyLifecycleContext(string dbPath, string archivePath) : DbContext, IArchiveContext
    {
        public DbSet<DateOnlyLifecycle> Rows => Set<DateOnlyLifecycle>();
        public DbSet<DateOnlyLifecycleHistory> History => Set<DateOnlyLifecycleHistory>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, ArchiveModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DateOnlyLifecycle>().ToTable("date_only_rows");
            modelBuilder.ToTieredStore<DateOnlyLifecycle>(row => row.ArchivedOn, archivePath)
                .WithReadModel<DateOnlyLifecycleHistory>();
        }
    }

    private sealed class PrimaryKeyRecordContext(string dbPath, string archivePath) : DbContext, IArchiveContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, ArchiveModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureEntities(modelBuilder);
            modelBuilder.ToTieredStore<StableRecord>(record => record.EffectiveAt, archivePath)
                .PartitionBy(partitions => partitions.ByMonth(record => record.EffectiveAt))
                .WithReadModel<StableRecordHistory>()
                .Including<StableRecordPart>(
                    record => record.Parts,
                    parts => parts.WithReadModel<StableRecordPartHistory>());
        }
    }

    private static void ConfigureEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StableRecord>(builder =>
        {
            builder.ToTable("stable_records");
            builder.HasKey(record => record.Id);
            builder.HasMany(record => record.Parts).WithOne(part => part.Record).HasForeignKey(part => part.RecordId);
        });
        modelBuilder.Entity<StableRecordPart>(builder =>
        {
            builder.ToTable("stable_record_parts");
            builder.HasKey(part => part.Id);
        });
    }

    private sealed class NullableMatchRecord
    {
        public int Id { get; set; }
        public string? ExternalId { get; set; }
        public DateTime EffectiveAt { get; set; }
    }

    private sealed class NullableMatchKeyContext(string dbPath, string archivePath) : DbContext, IArchiveContext
    {
        public DbSet<NullableMatchRecord> Records => Set<NullableMatchRecord>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, ArchiveModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NullableMatchRecord>(builder =>
            {
                builder.ToTable("nullable_match_records");
                builder.HasKey(record => record.Id);
            });
            modelBuilder.ToTieredStore<NullableMatchRecord>(record => record.EffectiveAt, archivePath)
                .MatchBy(record => record.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced);
        }
    }

    private sealed class UnprovenMatchKeyContext(string dbPath, string archivePath) : DbContext, IArchiveContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, ArchiveModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NullableMatchRecord>(builder =>
            {
                builder.ToTable("nullable_match_records");
                builder.HasKey(record => record.Id);
            });
            modelBuilder.ToTieredStore<NullableMatchRecord>(record => record.EffectiveAt, archivePath)
                .MatchBy(record => record.ExternalId);
        }
    }

    private sealed class DeclaredUniqueMatchKeyContext(string dbPath, string archivePath) : DbContext, IArchiveContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, ArchiveModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NullableMatchRecord>(builder =>
            {
                builder.ToTable("nullable_match_records");
                builder.HasKey(record => record.Id);
                builder.HasIndex(record => record.ExternalId).IsUnique();
            });
            modelBuilder.ToTieredStore<NullableMatchRecord>(record => record.EffectiveAt, archivePath)
                .MatchBy(record => record.ExternalId);
        }
    }

    private sealed class InvalidMatchKeyUniquenessContext(string dbPath, string archivePath) : DbContext, IArchiveContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, ArchiveModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NullableMatchRecord>(builder =>
            {
                builder.ToTable("nullable_match_records");
                builder.HasKey(record => record.Id);
            });
            modelBuilder.ToTieredStore<NullableMatchRecord>(record => record.EffectiveAt, archivePath)
                .MatchBy(record => record.ExternalId, (TierMatchKeyUniqueness)999);
        }
    }

    private interface IArchiveContext
    {
        string ArchivePath { get; }
    }

    private sealed class ArchiveModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context is IArchiveContext archive
                ? (context.GetType(), archive.ArchivePath, designTime)
                : (object)(context.GetType(), designTime);
    }
}
