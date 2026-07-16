using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Globalization;

namespace DuckDB.EFCoreProvider.Extensions;

public static partial class DuckDBArchiveExtensions
{
    /// <summary>
    ///     Rewrites the complete active cold range into a verified immutable generation using explicit Parquet
    ///     sizing and compression controls. Logical rows and the watermark are unchanged.
    /// </summary>
    public static async Task<TierArchiveResult> CompactArchiveTierAsync<TRoot>(
        this DatabaseFacade database,
        TierCompactionOptions? options = null,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        options ??= new TierCompactionOptions();
        options.Validate();
        var result = await database.ReconcileArchiveTierAsync<TRoot>(
                new TierReconciliationOptions
                {
                    ForceRewrite = true,
                    Writer = options.Writer,
                    Manifest = options.Manifest,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return result with { Operation = TierArchiveOperation.Compact };
    }

    /// <summary>Synchronous version of <c>CompactArchiveTierAsync</c>.</summary>
    public static TierArchiveResult CompactArchiveTier<TRoot>(
        this DatabaseFacade database,
        TierCompactionOptions? options = null)
        where TRoot : class
        => database.CompactArchiveTierAsync<TRoot>(options).GetAwaiter().GetResult();

    /// <summary>
    ///     Restores an exact root-key or declared-partition selection into mapped hot tables and publishes a
    ///     replacement cold generation that omits the same aggregate scope.
    /// </summary>
    public static async Task<TierRestoreResult> RestoreArchiveTierAsync<TRoot>(
        this DatabaseFacade database,
        TierRestoreOptions options,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "RestoreArchiveTierAsync writes hot tables and publishes external Parquet files and cannot run "
                + "inside a caller transaction.");
        }

        var (context, sql, archiveFileProbe, _) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        var boundary = DuckDBTierMaintenanceBoundary.Create(sql, aggregate, options.Scope, []);
        var rootsSelected = 0L;
        var rootsInserted = 0L;
        var rowsInserted = 0L;
        var previousGenerationId = "base";

        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            await ExecuteNonQueryAsync(connection, DuckDBTierControl.ControlTableDdl(sql), cancellationToken)
                .ConfigureAwait(false);
            var activeBasePath = ReadActiveArchiveBasePath(connection, sql, aggregate);
            previousGenerationId = ReadArchiveRevision(connection, sql, aggregate.ControlKey) ?? "base";
            EnsureArchiveSpecCompatible(connection, sql, archiveFileProbe, aggregate, activeBasePath);
            EnsurePartitionSpecCompatible(connection, sql, archiveFileProbe, aggregate, activeBasePath);
            var watermark = ReadWatermark(connection, sql, aggregate.ControlKey);
            if (watermark is null)
            {
                var empty = new DuckDBTierArchiveManifest(
                    aggregate,
                    TierArchiveOperation.Restore,
                    null,
                    DateTime.MinValue,
                    DateTime.MinValue,
                    activeBasePath,
                    ReadArchiveRevision(connection, sql, aggregate.ControlKey),
                    options.Manifest);
                return new TierRestoreResult(
                    0,
                    0,
                    0,
                    previousGenerationId,
                    previousGenerationId,
                    empty.Build(DateTime.MinValue, noOp: true, TierArchiveStage.Completed));
            }

            var sources = new Dictionary<DuckDBTierNode, string>();
            foreach (var node in aggregate.Nodes)
            {
                var nodePath = DuckDBTierArchiveManifest.NodeArchivePath(activeBasePath, node.Table);
                if (!archiveFileProbe.HasArchiveFiles(connection, nodePath))
                {
                    continue;
                }

                var source = node.IsRoot
                    ? DuckDBTierControl.RestoreRootSourceSql(
                        sql,
                        node.Columns,
                        nodePath,
                        aggregate.RootPartitions,
                        boundary.RootScopePredicate("c")
                        ?? throw new InvalidOperationException("Restore scope did not produce a root predicate."))
                    : DuckDBTierControl.RestoreChildSourceSql(
                        sql,
                        node.Columns,
                        node.ChainToRoot,
                        nodePath,
                        activeBasePath,
                        boundary.RootScopePredicate(
                            "r" + (node.ChainToRoot.Count - 1).ToString(CultureInfo.InvariantCulture))
                        ?? throw new InvalidOperationException("Restore scope did not produce a child root predicate."));
                sources[node] = source;
                var selected = await ExecuteCountAsync(
                        connection,
                        DuckDBTierControl.ReconcileSourceCountSql(source),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (node.IsRoot)
                {
                    rootsSelected = selected;
                }

                var conflicts = await ExecuteCountAsync(
                        connection,
                        DuckDBTierControl.RestoreConflictCountSql(
                            sql,
                            node.Table,
                            node.Schema,
                            node.KeyColumns,
                            node.ComparisonColumns,
                            source),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (conflicts > 0)
                {
                    throw new InvalidOperationException(
                        $"Restore selection for table '{node.Table}' conflicts with {conflicts} existing hot "
                        + "representation(s). No existing hot row was overwritten.");
                }
            }

            if (rootsSelected == 0)
            {
                var noOp = new DuckDBTierArchiveManifest(
                    aggregate,
                    TierArchiveOperation.Restore,
                    watermark,
                    DateTime.MinValue,
                    watermark.Value,
                    activeBasePath,
                    ReadArchiveRevision(connection, sql, aggregate.ControlKey),
                    options.Manifest);
                CaptureArchiveFiles(connection, archiveFileProbe, aggregate, noOp);
                return new TierRestoreResult(
                    0,
                    0,
                    rowsInserted,
                    previousGenerationId,
                    previousGenerationId,
                    noOp.Build(watermark.Value, noOp: true, TierArchiveStage.Completed));
            }

            var transactionStarted = false;
            try
            {
                await ExecuteNonQueryAsync(connection, "BEGIN TRANSACTION;", cancellationToken).ConfigureAwait(false);
                transactionStarted = true;
                foreach (var node in aggregate.Nodes)
                {
                    if (!sources.TryGetValue(node, out var source))
                    {
                        continue;
                    }

                    var before = await ExecuteCountAsync(
                            connection,
                            DuckDBTierControl.HotTableCountSql(sql, node.Table, node.Schema),
                            cancellationToken)
                        .ConfigureAwait(false);
                    await ExecuteNonQueryAsync(
                            connection,
                            DuckDBTierControl.RestoreInsertSql(
                                sql,
                                node.Table,
                                node.Schema,
                                node.Columns,
                                node.KeyColumns,
                                source),
                            cancellationToken)
                        .ConfigureAwait(false);
                    var after = await ExecuteCountAsync(
                            connection,
                            DuckDBTierControl.HotTableCountSql(sql, node.Table, node.Schema),
                            cancellationToken)
                        .ConfigureAwait(false);
                    var inserted = Math.Max(0, after - before);
                    rowsInserted += inserted;
                    if (node.IsRoot)
                    {
                        rootsInserted = inserted;
                    }
                }

                var publication = await database.ReconcileArchiveTierAsync<TRoot>(
                        new TierReconciliationOptions
                        {
                            Scope = options.Scope,
                            OmitScopeFromCold = true,
                            UseExistingTransaction = true,
                            Writer = options.Writer,
                            Manifest = options.Manifest,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteNonQueryAsync(connection, "COMMIT;", CancellationToken.None).ConfigureAwait(false);
                transactionStarted = false;
                return new TierRestoreResult(
                    rootsSelected,
                    rootsInserted,
                    rowsInserted,
                    previousGenerationId,
                    publication.Revision ?? "base",
                    publication with { Operation = TierArchiveOperation.Restore });
            }
            catch
            {
                if (transactionStarted)
                {
                    try
                    {
                        await ExecuteNonQueryAsync(connection, "ROLLBACK;", CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // Preserve the restore/publication failure. The outer finally closes connections opened
                        // by this operation, and the active generation remains selected by persisted metadata.
                    }
                }

                throw;
            }
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }
    }

    /// <summary>Synchronous version of <c>RestoreArchiveTierAsync</c>.</summary>
    public static TierRestoreResult RestoreArchiveTier<TRoot>(
        this DatabaseFacade database,
        TierRestoreOptions options)
        where TRoot : class
        => database.RestoreArchiveTierAsync<TRoot>(options).GetAwaiter().GetResult();
}
