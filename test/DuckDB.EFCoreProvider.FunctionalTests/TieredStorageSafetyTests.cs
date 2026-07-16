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
        using var context = new StableOrderContext(Path.Combine(_root, "nullable.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.AddRange(
            Order("pending", completedDate: null),
            Order("january", new DateTime(2024, 1, 15)),
            Order("february", new DateTime(2024, 2, 15)));
        context.SaveChanges();

        var first = await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 28));

        Assert.Equal(new DateTime(2024, 2, 1), first.Watermark);
        Assert.Equal(1, first.RowsArchived);
        Assert.Equal(TierArchiveOperation.Archive, first.Operation);
        Assert.Null(first.PreviousWatermark);
        Assert.Equal(DateTime.MinValue, first.WindowStart);
        Assert.Equal(new DateTime(2024, 2, 1), first.WindowEnd);
        Assert.Equal(TierArchiveStage.Completed, first.Stage);
        var rootEvidence = Assert.Single(first.Nodes, node => node.Table == "stable_orders");
        Assert.Equal(1, rootEvidence.SelectedRows);
        Assert.Equal(1, rootEvidence.CopiedRows);
        Assert.Equal(1, rootEvidence.DeletedRows);
        Assert.NotEmpty(rootEvidence.Files);
        context.ChangeTracker.Clear();
        Assert.Equal(2, context.Orders.Count());
        Assert.Single(context.Orders.Where(order => order.CompletedDate == null));
        Assert.Equal(3, context.OrderHistory.Count());
        Assert.True(Directory.Exists(Path.Combine(
            archivePath, "stable_orders", "CompletedDate_month=2024-01-01")));
        var archiveSpec = context.Database.SqlQueryRaw<string>(
            "SELECT archive_spec AS \"Value\" FROM __duckdb_tier_control WHERE name = 'stable_orders'").Single();
        Assert.Contains("\"LifecycleColumn\":\"CompletedDate\"", archiveSpec);
        Assert.Contains("\"MatchKeyColumns\":[\"ExternalId\"]", archiveSpec);

        var pending = context.Orders.Single(order => order.ExternalId == "pending");
        pending.CompletedDate = new DateTime(2024, 2, 20);
        context.SaveChanges();

        var second = await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 3, 19));

        Assert.Equal(new DateTime(2024, 3, 1), second.Watermark);
        Assert.Equal(2, second.RowsArchived);
        context.ChangeTracker.Clear();
        Assert.Empty(context.Orders);
        Assert.Equal(3, context.OrderHistory.Count());
        Assert.Equal(2, context.OrderHistory.Count(
            order => order.CompletedDate >= new DateTime(2024, 2, 1)
                     && order.CompletedDate < new DateTime(2024, 3, 1)));
        Assert.Contains(
            "CompletedDate_month",
            context.OrderHistory.Where(order => order.CompletedDate >= new DateTime(2024, 2, 1)).ToQueryString());
    }

    [Fact]
    public async Task Null_only_lifecycle_set_advances_watermark_without_moving_or_hiding_rows()
    {
        var archivePath = Path.Combine(_root, "null-only-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "null-only.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order("pending", completedDate: null));
        context.SaveChanges();

        var result = await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));
        var retry = await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));

        Assert.Equal(0, result.RowsArchived);
        Assert.Equal(new DateTime(2024, 2, 1), result.Watermark);
        Assert.True(retry.NoOp);
        Assert.Single(context.Orders);
        Assert.Single(context.OrderHistory);
        Assert.False(Directory.Exists(Path.Combine(archivePath, "stable_orders")));
    }

    [Fact]
    public async Task Stable_root_and_composite_child_keys_recover_a_surrogate_key_replay()
    {
        var archivePath = Path.Combine(_root, "replay-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "replay.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order(
            "order-1",
            new DateTime(2024, 1, 15),
            new StableOrderItem { ExternalOrderId = "order-1", LineCode = "A", Amount = 12m }));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));

        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_orders (\"Id\", \"CompletedDate\", \"ExternalId\", \"Status\") "
            + "VALUES (101, TIMESTAMP '2024-01-15', 'order-1', 'complete');");
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_order_items (\"Id\", \"Amount\", \"ExternalOrderId\", \"LineCode\", \"OrderId\") "
            + "VALUES (201, 12, 'order-1', 'A', 101);");

        // Model a stop after watermark publication but before view publication: retry must repair the views
        // before it removes the replayed hot graph.
        context.Database.ExecuteSqlRaw(
            "CREATE OR REPLACE VIEW stable_orders_tiered AS SELECT * FROM stable_orders;");
        context.Database.ExecuteSqlRaw(
            "CREATE OR REPLACE VIEW stable_order_items_tiered AS SELECT * FROM stable_order_items;");

        var retry = await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));

        Assert.True(retry.NoOp);
        context.ChangeTracker.Clear();
        Assert.Empty(context.Orders);
        Assert.Empty(context.Items);
        Assert.Single(context.OrderHistory);
        Assert.Single(context.ItemHistory);
        Assert.Equal(
            ["ExternalId"],
            context.Model.FindEntityType(typeof(StableOrder))!.GetTieredStoreMatchProperties());
        Assert.Equal(
            ["ExternalOrderId", "LineCode"],
            context.Model.FindEntityType(typeof(StableOrderItem))!.GetTieredStoreMatchProperties());
    }

    [Fact]
    public async Task Corrected_archived_key_is_rejected_and_preserved_hot()
    {
        var archivePath = Path.Combine(_root, "correction-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "correction.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order("order-1", new DateTime(2024, 1, 15)));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_orders (\"Id\", \"CompletedDate\", \"ExternalId\", \"Status\") "
            + "VALUES (101, TIMESTAMP '2024-01-15', 'order-1', 'corrected');");

        var exception = await Assert.ThrowsAsync<TierArchivedKeyConflictException>(
            () => context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1)));

        Assert.Equal(typeof(StableOrder), exception.EntityType);
        Assert.Equal(1, exception.ConflictingRows);
        Assert.Equal("stable_orders", exception.Binding?.ControlKey);
        Assert.Contains("control 'stable_orders'", exception.Message);
        Assert.Equal("corrected", context.Orders.Single().Status);
    }

    [Fact]
    public async Task Reopened_archived_key_is_rejected_and_preserved_hot()
    {
        var archivePath = Path.Combine(_root, "reopened-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "reopened.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order("order-1", new DateTime(2024, 1, 15)));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_orders (\"Id\", \"CompletedDate\", \"ExternalId\", \"Status\") "
            + "VALUES (101, NULL, 'order-1', 'complete');");

        var exception = await Assert.ThrowsAsync<TierArchivedKeyConflictException>(
            () => context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1)));

        Assert.Equal(typeof(StableOrder), exception.EntityType);
        Assert.Null(context.Orders.Single().CompletedDate);
    }

    [Fact]
    public async Task Corrected_composite_child_key_is_rejected_and_preserved_hot()
    {
        var archivePath = Path.Combine(_root, "child-correction-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "child-correction.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order(
            "order-1",
            new DateTime(2024, 1, 15),
            new StableOrderItem { ExternalOrderId = "order-1", LineCode = "A", Amount = 12m }));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_orders (\"Id\", \"CompletedDate\", \"ExternalId\", \"Status\") "
            + "VALUES (101, TIMESTAMP '2024-01-15', 'order-1', 'complete');");
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_order_items (\"Id\", \"Amount\", \"ExternalOrderId\", \"LineCode\", \"OrderId\") "
            + "VALUES (201, 99, 'order-1', 'A', 101);");

        var exception = await Assert.ThrowsAsync<TierArchivedKeyConflictException>(
            () => context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1)));

        Assert.Equal(typeof(StableOrderItem), exception.EntityType);
        Assert.Equal(99m, context.Items.Single().Amount);
    }

    [Fact]
    public async Task Approved_correction_and_late_unseen_row_publish_a_new_cold_generation()
    {
        var archivePath = Path.Combine(_root, "reconcile-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "reconcile.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order("order-1", new DateTime(2024, 1, 15)));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_orders (\"Id\", \"CompletedDate\", \"ExternalId\", \"Status\") VALUES "
            + "(101, TIMESTAMP '2024-01-15', 'order-1', 'corrected'), "
            + "(102, TIMESTAMP '2024-01-20', 'late-unseen', 'complete');");

        var result = await context.Database.ReconcileArchiveTierAsync<StableOrder>();

        Assert.Equal(TierArchiveOperation.Reconcile, result.Operation);
        Assert.False(result.NoOp);
        Assert.Equal(2, result.RowsArchived);
        Assert.NotNull(result.Revision);
        Assert.Contains("/_revisions/", result.ArchivePath.Replace('\\', '/'));
        var rootEvidence = Assert.Single(result.Nodes, node => node.Table == "stable_orders");
        Assert.Equal(2, rootEvidence.SelectedRows);
        Assert.Equal(2, rootEvidence.CopiedRows);
        Assert.Equal(2, rootEvidence.DeletedRows);
        Assert.Empty(context.Orders);
        Assert.Equal(2, context.OrderHistory.Count());
        Assert.Equal("corrected", context.OrderHistory.Single(order => order.ExternalId == "order-1").Status);
        Assert.Equal("complete", context.OrderHistory.Single(order => order.ExternalId == "late-unseen").Status);
        Assert.True(Directory.Exists(Path.Combine(archivePath, "stable_orders")));
    }

    [Fact]
    public async Task Approved_composite_child_correction_is_reconciled_with_its_root_replay()
    {
        var archivePath = Path.Combine(_root, "reconcile-child-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "reconcile-child.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order(
            "order-1",
            new DateTime(2024, 1, 15),
            new StableOrderItem { ExternalOrderId = "order-1", LineCode = "A", Amount = 12m }));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_orders (\"Id\", \"CompletedDate\", \"ExternalId\", \"Status\") "
            + "VALUES (101, TIMESTAMP '2024-01-15', 'order-1', 'complete');");
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_order_items (\"Id\", \"Amount\", \"ExternalOrderId\", \"LineCode\", \"OrderId\") "
            + "VALUES (201, 99, 'order-1', 'A', 101);");

        var result = await context.Database.ReconcileArchiveTierAsync<StableOrder>();

        Assert.Equal(1, result.RowsArchived);
        Assert.Empty(context.Orders);
        Assert.Empty(context.Items);
        Assert.Single(context.OrderHistory);
        Assert.Equal(99m, context.ItemHistory.Single().Amount);
        Assert.All(result.Nodes, node => Assert.Equal(node.SelectedRows, node.CopiedRows));
    }

    [Fact]
    public async Task Scoped_reconciliation_changes_only_the_selected_root_key()
    {
        var archivePath = Path.Combine(_root, "scoped-reconcile-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "scoped-reconcile.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.AddRange(
            Order("order-1", new DateTime(2024, 1, 15)),
            Order("order-2", new DateTime(2024, 1, 16)));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_orders (\"Id\", \"CompletedDate\", \"ExternalId\", \"Status\") VALUES "
            + "(101, TIMESTAMP '2024-01-15', 'order-1', 'corrected-1'), "
            + "(102, TIMESTAMP '2024-01-16', 'order-2', 'corrected-2');");

        var result = await context.Database.ReconcileArchiveTierAsync<StableOrder>(
            new TierReconciliationOptions
            {
                Scope = TierMaintenanceScope.ForRootMatchKeys(
                    TierRowIdentity.For<StableOrder>(
                        new Dictionary<string, object?> { ["ExternalId"] = "order-1" })),
            });

        Assert.False(result.NoOp);
        Assert.Equal("corrected-1", context.OrderHistory.Single(order => order.ExternalId == "order-1").Status);
        Assert.Equal("complete", context.OrderHistory.Single(order => order.ExternalId == "order-2").Status);
        var conflicts = await context.Database.GetArchiveConflictsAsync<StableOrder>();
        var conflict = Assert.Single(conflicts.Keys);
        Assert.Equal("order-2", conflict.Values["ExternalId"]);
    }

    [Fact]
    public async Task Root_tombstone_cascades_through_the_declared_cold_aggregate()
    {
        var archivePath = Path.Combine(_root, "tombstone-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "tombstone.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order(
            "order-1",
            new DateTime(2024, 1, 15),
            new StableOrderItem { ExternalOrderId = "order-1", LineCode = "A", Amount = 12m }));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));

        await context.Database.ReconcileArchiveTierAsync<StableOrder>(
            new TierReconciliationOptions
            {
                Tombstones =
                [
                    TierRowIdentity.For<StableOrder>(
                        new Dictionary<string, object?> { ["ExternalId"] = "order-1" }),
                ],
            });

        Assert.Empty(context.OrderHistory);
        Assert.Empty(context.ItemHistory);
    }

    [Fact]
    public async Task Restore_moves_an_exact_cold_graph_back_to_hot_and_removes_it_from_cold()
    {
        var archivePath = Path.Combine(_root, "restore-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "restore.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order(
            "order-1",
            new DateTime(2024, 1, 15),
            new StableOrderItem { ExternalOrderId = "order-1", LineCode = "A", Amount = 12m }));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));

        var result = await context.Database.RestoreArchiveTierAsync<StableOrder>(
            new TierRestoreOptions
            {
                Scope = TierMaintenanceScope.ForRootMatchKeys(
                    TierRowIdentity.For<StableOrder>(
                        new Dictionary<string, object?> { ["ExternalId"] = "order-1" })),
            });

        Assert.Equal(1, result.RootsSelected);
        Assert.Equal(1, result.RootsInserted);
        Assert.Equal(2, result.RowsInserted);
        Assert.Equal(TierArchiveOperation.Restore, result.Publication.Operation);
        Assert.Single(context.Orders);
        Assert.Single(context.Items);
        Assert.Single(context.OrderHistory);
        Assert.Single(context.ItemHistory);
    }

    [Fact]
    public async Task Bounded_manifest_inventory_compaction_and_local_preflight_are_generic()
    {
        var archivePath = Path.Combine(_root, "maintenance-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "maintenance.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order("order-1", new DateTime(2024, 1, 15)));
        context.SaveChanges();

        var archived = await context.Database.ArchiveTierAsync<StableOrder>(
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
        var node = Assert.Single(archived.Nodes, evidence => evidence.Table == "stable_orders");
        Assert.Empty(node.Files);
        Assert.True(node.FileCount > 0);
        Assert.True(node.FilesTruncated);

        var inventory = await context.Database.GetArchiveGenerationInventoryAsync<StableOrder>();
        var active = Assert.Single(inventory.Generations, generation =>
            generation.State == TierArchiveGenerationState.Active);
        Assert.Equal(inventory.ActiveGenerationId, active.GenerationId);
        Assert.True(active.FileCount > 0);

        var compacted = await context.Database.CompactArchiveTierAsync<StableOrder>(
            new TierCompactionOptions
            {
                Writer = new TierParquetWriterOptions { Compression = "zstd", CompressionLevel = 5 },
            });
        Assert.Equal(TierArchiveOperation.Compact, compacted.Operation);
        Assert.NotNull(compacted.Revision);

        var preflight = await context.Database.PreflightTieredStorageAsync<StableOrder>(
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
        using var context = new StableOrderContext(Path.Combine(_root, "empty-preflight.duckdb"), archivePath);

        var preflight = await context.Database.PreflightTieredStorageAsync<StableOrder>();

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
        using var context = new StableOrderContext(
            Path.Combine(_root, "writer-validation.duckdb"),
            Path.Combine(_root, "writer-validation-archive"));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => context.Database.ArchiveTierAsync<StableOrder>(
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
        using var context = new StableOrderContext(
            Path.Combine(_root, "diagnostics.duckdb"),
            Path.Combine(_root, "diagnostics-archive"));

        var contract = await context.Database.InspectArchiveContractAsync<StableOrder>();
        var conflicts = await context.Database.GetArchiveConflictsAsync<StableOrder>();
        var inventory = await context.Database.GetArchiveGenerationInventoryAsync<StableOrder>();

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
            CompletedAt = new DateTime(2024, 1, 15),
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
        using var context = new StableOrderContext(Path.Combine(_root, "reconcile-reopened.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order("order-1", new DateTime(2024, 1, 15)));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));
        context.Database.ExecuteSqlRaw(
            "INSERT INTO stable_orders (\"Id\", \"CompletedDate\", \"ExternalId\", \"Status\") "
            + "VALUES (101, NULL, 'order-1', 'reopened');");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.ReconcileArchiveTierAsync<StableOrder>());

        Assert.Contains("separate restore workflow", exception.Message);
        Assert.Single(context.Orders);
        Assert.Equal(2, context.OrderHistory.Count());
    }

    [Fact]
    public async Task Late_unseen_row_remains_hot_on_an_ordinary_no_op_archive()
    {
        var archivePath = Path.Combine(_root, "late-hot-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "late-hot.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order("order-1", new DateTime(2024, 1, 15)));
        context.SaveChanges();
        await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));
        context.Orders.Add(Order("late-unseen", new DateTime(2024, 1, 20)));
        context.SaveChanges();

        var result = await context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));

        Assert.True(result.NoOp);
        Assert.Single(context.Orders);
        Assert.Equal(2, context.OrderHistory.Count());
    }

    [Fact]
    public async Task Match_key_change_is_rejected_after_cold_files_exist()
    {
        var dbPath = Path.Combine(_root, "key-layout.duckdb");
        var archivePath = Path.Combine(_root, "key-layout-archive");
        using (var original = new StableOrderContext(dbPath, archivePath))
        {
            original.Database.EnsureCreated();
            original.Orders.Add(Order("order-1", new DateTime(2024, 1, 15)));
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));
        }

        using var changed = new PrimaryKeyOrderContext(dbPath, archivePath);
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
        using (var original = new StableOrderContext(dbPath, originalPath))
        {
            original.Database.EnsureCreated();
            original.Orders.Add(Order("order-1", new DateTime(2024, 1, 15)));
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1));
        }

        using var changed = new StableOrderContext(dbPath, Path.Combine(_root, "different-archive"));
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
        context.Orders.Add(new NullableMatchOrder
        {
            ExternalId = null,
            CompletedDate = new DateTime(2024, 1, 15),
        });
        context.SaveChanges();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.ArchiveTierAsync<NullableMatchOrder>(new DateTime(2024, 2, 1)));

        Assert.Contains("NULL configured match-key component", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(archivePath, "nullable_match_orders")));
        Assert.Single(context.Orders);
    }

    [Fact]
    public async Task Null_child_match_key_is_rejected_before_any_aggregate_table_is_copied()
    {
        var archivePath = Path.Combine(_root, "null-child-key-archive");
        using var context = new StableOrderContext(Path.Combine(_root, "null-child-key.duckdb"), archivePath);
        context.Database.EnsureCreated();
        context.Orders.Add(Order(
            "order-1",
            new DateTime(2024, 1, 15),
            new StableOrderItem { ExternalOrderId = null, LineCode = "A", Amount = 12m }));
        context.SaveChanges();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.ArchiveTierAsync<StableOrder>(new DateTime(2024, 2, 1)));

        Assert.Contains("stable_order_items", exception.Message);
        Assert.False(Directory.Exists(Path.Combine(archivePath, "stable_orders")));
        Assert.Single(context.Orders);
        Assert.Single(context.Items);
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

        var entity = context.Model.FindEntityType(typeof(NullableMatchOrder))!;

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

    private static StableOrder Order(
        string externalId,
        DateTime? completedDate,
        params StableOrderItem[] items)
        => new()
        {
            ExternalId = externalId,
            CompletedDate = completedDate,
            Status = "complete",
            Items = [.. items],
        };

    private sealed class StableOrder
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime? CompletedDate { get; set; }
        public string Status { get; set; } = null!;
        public List<StableOrderItem> Items { get; set; } = [];
    }

    private sealed class StableOrderItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public StableOrder? Order { get; set; }
        public string? ExternalOrderId { get; set; }
        public string LineCode { get; set; } = null!;
        public decimal Amount { get; set; }
    }

    private sealed class CompositeRoot
    {
        public int TenantId { get; set; }
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime CompletedAt { get; set; }
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
        public DateTime CompletedAt { get; set; }
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

    private sealed class StableOrderHistory
    {
        public int Id { get; set; }
        public string ExternalId { get; set; } = null!;
        public DateTime? CompletedDate { get; set; }
        public string Status { get; set; } = null!;
    }

    private sealed class StableOrderItemHistory
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string? ExternalOrderId { get; set; }
        public string LineCode { get; set; } = null!;
        public decimal Amount { get; set; }
    }

    private sealed class StableOrderContext(string dbPath, string archivePath) : DbContext, IArchiveContext
    {
        public DbSet<StableOrder> Orders => Set<StableOrder>();
        public DbSet<StableOrderItem> Items => Set<StableOrderItem>();
        public DbSet<StableOrderHistory> OrderHistory => Set<StableOrderHistory>();
        public DbSet<StableOrderItemHistory> ItemHistory => Set<StableOrderItemHistory>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, ArchiveModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureEntities(modelBuilder);
            modelBuilder.ToTieredStore<StableOrder>(order => order.CompletedDate, archivePath)
                .MatchBy(order => order.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                .PartitionBy(partitions => partitions.ByMonth(order => order.CompletedDate))
                .WithReadModel<StableOrderHistory>()
                .Including<StableOrderItem>(order => order.Items, items => items
                    .MatchBy(
                        item => new { item.ExternalOrderId, item.LineCode },
                        TierMatchKeyUniqueness.ExternallyEnforced)
                    .WithReadModel<StableOrderItemHistory>());
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
            modelBuilder.ToTieredStore<CompositeRoot>(root => root.CompletedAt, archivePath)
                .MatchBy(root => root.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
                .WithReadModel<CompositeRootHistory>()
                .Including<CompositeChild>(root => root.Children, child => child
                    .MatchBy(item => item.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced)
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

    private sealed class PrimaryKeyOrderContext(string dbPath, string archivePath) : DbContext, IArchiveContext
    {
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, ArchiveModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureEntities(modelBuilder);
            modelBuilder.ToTieredStore<StableOrder>(order => order.CompletedDate, archivePath)
                .PartitionBy(partitions => partitions.ByMonth(order => order.CompletedDate))
                .WithReadModel<StableOrderHistory>()
                .Including<StableOrderItem>(
                    order => order.Items,
                    items => items.WithReadModel<StableOrderItemHistory>());
        }
    }

    private static void ConfigureEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StableOrder>(builder =>
        {
            builder.ToTable("stable_orders");
            builder.HasKey(order => order.Id);
            builder.HasMany(order => order.Items).WithOne(item => item.Order).HasForeignKey(item => item.OrderId);
        });
        modelBuilder.Entity<StableOrderItem>(builder =>
        {
            builder.ToTable("stable_order_items");
            builder.HasKey(item => item.Id);
        });
    }

    private sealed class NullableMatchOrder
    {
        public int Id { get; set; }
        public string? ExternalId { get; set; }
        public DateTime CompletedDate { get; set; }
    }

    private sealed class NullableMatchKeyContext(string dbPath, string archivePath) : DbContext, IArchiveContext
    {
        public DbSet<NullableMatchOrder> Orders => Set<NullableMatchOrder>();
        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, ArchiveModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NullableMatchOrder>(builder =>
            {
                builder.ToTable("nullable_match_orders");
                builder.HasKey(order => order.Id);
            });
            modelBuilder.ToTieredStore<NullableMatchOrder>(order => order.CompletedDate, archivePath)
                .MatchBy(order => order.ExternalId, TierMatchKeyUniqueness.ExternallyEnforced);
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
            modelBuilder.Entity<NullableMatchOrder>(builder =>
            {
                builder.ToTable("nullable_match_orders");
                builder.HasKey(order => order.Id);
            });
            modelBuilder.ToTieredStore<NullableMatchOrder>(order => order.CompletedDate, archivePath)
                .MatchBy(order => order.ExternalId);
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
            modelBuilder.Entity<NullableMatchOrder>(builder =>
            {
                builder.ToTable("nullable_match_orders");
                builder.HasKey(order => order.Id);
                builder.HasIndex(order => order.ExternalId).IsUnique();
            });
            modelBuilder.ToTieredStore<NullableMatchOrder>(order => order.CompletedDate, archivePath)
                .MatchBy(order => order.ExternalId);
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
            modelBuilder.Entity<NullableMatchOrder>(builder =>
            {
                builder.ToTable("nullable_match_orders");
                builder.HasKey(order => order.Id);
            });
            modelBuilder.ToTieredStore<NullableMatchOrder>(order => order.CompletedDate, archivePath)
                .MatchBy(order => order.ExternalId, (TierMatchKeyUniqueness)999);
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
