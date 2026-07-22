using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Raised when a hot row uses a stable tier match key that is already published in the cold archive but
///     carries a different representation. The provider rejects corrections and reopened aggregates rather
///     than silently hiding the hot version or deleting it during retry cleanup.
/// </summary>
public sealed class TierArchivedKeyConflictException : InvalidOperationException
{
    /// <summary>Creates a conflict exception for a tiered table.</summary>
    public TierArchivedKeyConflictException(Type entityType, string table, long conflictingRows)
        : this(entityType, table, conflictingRows, binding: null)
    {
    }

    /// <summary>Creates a conflict exception with the root-scoped binding that detected it.</summary>
    public TierArchivedKeyConflictException(
        Type entityType,
        string table,
        long conflictingRows,
        TieredStorageBindingInfo binding)
        : this(entityType, table, conflictingRows, (TieredStorageBindingInfo?)binding)
    {
    }

    private TierArchivedKeyConflictException(
        Type entityType,
        string table,
        long conflictingRows,
        TieredStorageBindingInfo? binding)
        : base(CreateMessage(table, conflictingRows, binding))
    {
        EntityType = entityType;
        Table = table;
        ConflictingRows = conflictingRows;
        Binding = binding;
    }

    /// <summary>The mapped entity type whose hot rows conflict with cold data.</summary>
    public Type EntityType { get; }

    /// <summary>The physical hot table.</summary>
    public string Table { get; }

    /// <summary>The number of conflicting hot rows detected.</summary>
    public long ConflictingRows { get; }

    /// <summary>The root-scoped binding that detected the conflict, when supplied by the provider.</summary>
    public TieredStorageBindingInfo? Binding { get; }

    private static string CreateMessage(
        string table,
        long conflictingRows,
        TieredStorageBindingInfo? binding)
    {
        var bindingEvidence = binding is { } value
            ? $" for binding {TieredStorageBindingEvidence.Describe(value)}"
            : string.Empty;
        return $"Tiered-storage table '{table}'{bindingEvidence} has {conflictingRows} changed hot row(s) whose "
               + "stable key already exists in cold Parquet. The normal archive stopped and left the hot rows "
               + "untouched. Validate the change, then use ReconcileArchiveTierAsync for an approved cold "
               + "correction; reopened lifecycle changes still require a separate restore workflow.";
    }
}

/// <summary>
///     DuckDB tiered-storage maintenance on <see cref="DatabaseFacade" />: create the control table and union
///     views, offload aged aggregates (root + children) to Parquet, and purge expired partitions. Configure
///     with <c>ToTieredStore</c>.
/// </summary>
/// <remarks>
///     DuckDB is single-writer: run these from the writing process with no other writer active.
/// </remarks>
public static partial class DuckDBArchiveExtensions
{
    private static readonly HashSet<string> KnownArchiveSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "file",
        "s3",
        "gs",
        "gcs",
        "r2",
        "http",
        "https",
        "azure",
        "az",
        "abfs",
        "abfss",
    };

    /// <summary>
    ///     Creates the tier control table and the requested union view for every configured tiered table (roots and
    ///     children), whether the view has an EF read model or was registered directly. Called automatically by
    ///     <c>EnsureCreated()</c>; call it yourself after <c>Migrate()</c>. Safe to call repeatedly.
    /// </summary>
    public static void EnsureTieredStoresCreated(this DatabaseFacade database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var (context, sql, archiveFileProbe, _) = Services(database);
        var aggregates = DuckDBTierAggregate.ResolveAll(context.Model);
        if (aggregates.Count == 0)
        {
            return;
        }

        var openedHere = OpenTracked(database);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            ExecuteNonQuery(connection, DuckDBTierControl.ControlTableDdl(sql));
            foreach (var aggregate in aggregates)
            {
                var activeArchiveBasePath = ReadActiveArchiveBasePath(connection, sql, aggregate);
                EnsureArchiveSpecCompatible(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
                EnsurePartitionSpecCompatible(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
                ExecuteNonQuery(
                    connection,
                    DuckDBTierControl.UpsertPartitionLayoutSql(
                        sql,
                        aggregate.ControlKey,
                        aggregate.Root.ArchiveSubPath,
                        aggregate.Granularity,
                        aggregate.PartitionSpec));
                ExecuteNonQuery(
                    connection,
                    DuckDBTierControl.UpsertArchiveSpecSql(
                        sql, aggregate.ControlKey, aggregate.Root.ArchiveSubPath, aggregate.ArchiveSpec));
                RegenerateViews(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
            }
        }
        finally
        {
            CloseTracked(database, openedHere);
        }
    }

    /// <summary>
    ///     Offloads aggregates whose root is older than <paramref name="cutoff" /> to the cold Parquet archive
    ///     (root and all declared children), advances the watermark, refreshes the views, and deletes the
    ///     archived rows leaf→root. Idempotent and crash-safe.
    /// </summary>
    public static Task<TierArchiveResult> ArchiveTierAsync<TRoot>(
        this DatabaseFacade database,
        DateTime cutoff,
        CancellationToken cancellationToken = default)
        where TRoot : class
        => database.ArchiveTierAsync<TRoot>(cutoff, new TierArchiveOptions(), cancellationToken);

    /// <summary>
    ///     Offloads an archive window using explicit provider-owned Parquet writer and manifest controls.
    /// </summary>
    public static async Task<TierArchiveResult> ArchiveTierAsync<TRoot>(
        this DatabaseFacade database,
        DateTime cutoff,
        TierArchiveOptions options,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var (context, sql, archiveFileProbe, failureInjector) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));

        foreach (var node in aggregate.Nodes)
        {
            if (StructMappingHelper.HasStructMappedComplexProperties(node.Entity))
            {
                throw new NotSupportedException(
                    $"ArchiveTierAsync does not support entity '{node.Entity.ClrType.Name}' because it contains "
                    + "struct-mapped complex properties. STRUCT columns would be flattened in the cold Parquet "
                    + "archive, breaking tiered read queries.");
            }
        }

        var aligned = DuckDBTierControl.AlignCutoff(cutoff, aggregate.Granularity);
        // Parquet writes are external side effects and cannot be rolled back with a caller-owned database
        // transaction. Multi-table aggregates also require leaf-to-root autocommit deletes because DuckDB
        // checks foreign keys immediately. Keep the complete archive workflow outside caller transactions.
        if (database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "ArchiveTierAsync cannot run inside an existing transaction because external Parquet writes "
                + "cannot be rolled back with the database transaction. Run it outside the caller transaction.");
        }

        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();

        try
        {
            await ExecuteNonQueryAsync(connection, DuckDBTierControl.ControlTableDdl(sql), cancellationToken).ConfigureAwait(false);
            var activeArchiveBasePath = ReadActiveArchiveBasePath(connection, sql, aggregate);
            var revision = ReadArchiveRevision(connection, sql, aggregate.ControlKey);
            EnsureArchiveSpecCompatible(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
            EnsurePartitionSpecCompatible(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
            await ExecuteNonQueryAsync(
                connection,
                DuckDBTierControl.UpsertPartitionLayoutSql(
                    sql, aggregate.ControlKey, aggregate.Root.ArchiveSubPath, aggregate.Granularity, aggregate.PartitionSpec),
                cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(
                connection,
                DuckDBTierControl.UpsertArchiveSpecSql(
                    sql, aggregate.ControlKey, aggregate.Root.ArchiveSubPath, aggregate.ArchiveSpec),
                cancellationToken).ConfigureAwait(false);

            var current = ReadWatermark(connection, sql, aggregate.ControlKey);
            var from = current ?? DateTime.MinValue;
            var windowEnd = current is { } currentWatermark && aligned <= currentWatermark
                ? currentWatermark
                : aligned;
            var manifest = new DuckDBTierArchiveManifest(
                aggregate,
                TierArchiveOperation.Archive,
                current,
                from,
                windowEnd,
                activeArchiveBasePath,
                revision,
                options.Manifest);
            CaptureArchiveFiles(connection, archiveFileProbe, aggregate, manifest);
            var stage = TierArchiveStage.Preflight;

            try
            {
                await ThrowIfAmbiguousSharedBindingsAsync(
                        connection,
                        sql,
                        aggregate,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (current is { } publishedWatermark)
                {
                    await ThrowIfArchivedConflictsAsync(
                            connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath, publishedWatermark,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (current is { } watermark && aligned <= watermark)
                {
                    // A process may stop after publishing the watermark but before every view is regenerated. Repair
                    // the read surface before retry cleanup can remove the last hot copy of any archived row.
                    RegenerateViews(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
                    stage = TierArchiveStage.DeleteHot;
                    await DeleteAggregateAsync(
                            connection, sql, archiveFileProbe, failureInjector, aggregate, manifest, watermark,
                            cancellationToken)
                        .ConfigureAwait(false);
                    stage = TierArchiveStage.Checkpoint;
                    await ExecuteNonQueryAsync(connection, "CHECKPOINT;", cancellationToken).ConfigureAwait(false);
                    return manifest.Build(watermark, noOp: true, TierArchiveStage.Completed);
                }

                foreach (var node in aggregate.Nodes)
                {
                    var selected = await ExecuteCountAsync(
                            connection,
                            DuckDBTierControl.HotWindowCountSql(
                                sql, node.Table, node.Schema, node.ChainToRoot, aggregate.RootTimestampColumn,
                                from, aligned),
                            cancellationToken)
                        .ConfigureAwait(false);
                    manifest.SetSelected(node, selected);
                }

                if (manifest.Build(aligned, noOp: false, stage).RowsArchived > 0)
                {
                    await ValidateNonNullMatchKeysAsync(
                            connection, sql, aggregate, from, aligned, cancellationToken)
                        .ConfigureAwait(false);

                    stage = TierArchiveStage.Copy;
                    foreach (var node in aggregate.Nodes)
                    {
                        var nodeArchivePath = manifest.ArchivePath(node);
                        EnsureLocalArchiveDirectory(nodeArchivePath);
                        await ExecuteNonQueryAsync(
                                connection,
                                CopySql(sql, aggregate, node, nodeArchivePath, from, aligned, options.Writer),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    stage = TierArchiveStage.Verify;
                    foreach (var node in aggregate.Nodes)
                    {
                        var nodeArchivePath = manifest.ArchivePath(node);
                        var copied = archiveFileProbe.HasArchiveFiles(connection, nodeArchivePath)
                            ? await ExecuteCountAsync(
                                    connection,
                                    DuckDBTierControl.ArchiveWindowCountSql(
                                        sql, nodeArchivePath, node.IsRoot, aggregate.RootTimestampColumn,
                                        aggregate.Granularity, from, aligned, aggregate.RootPartitions),
                                    cancellationToken)
                                .ConfigureAwait(false)
                            : 0;
                        var selected = manifest.SelectedRows(node);
                        if (copied != selected)
                        {
                            throw new InvalidOperationException(
                                $"Tiered-storage copy verification failed for table '{node.Table}': selected "
                                + $"{selected} row(s), but found {copied} row(s) in the archive window.");
                        }

                        manifest.SetCopied(
                            node,
                            copied,
                            archiveFileProbe.HasArchiveFiles(connection, nodeArchivePath)
                                ? archiveFileProbe.GetArchiveFileSummary(
                                    connection,
                                    nodeArchivePath,
                                    manifest.ManifestOptions)
                                : new DuckDBArchiveFileSummary(0, 0, [], IsTruncated: false));
                    }

                    stage = TierArchiveStage.Copy;
                    failureInjector.ThrowIfRequested(DuckDBTierFailurePoint.AfterCopy, table: null);
                }

                stage = TierArchiveStage.Publish;
                await PublishArchiveAsync(
                        connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath, revision, aligned,
                        useInternalTransaction: true, cancellationToken)
                    .ConfigureAwait(false);
                failureInjector.ThrowIfRequested(DuckDBTierFailurePoint.AfterPublication, table: null);
                stage = TierArchiveStage.DeleteHot;
                await DeleteAggregateAsync(
                        connection, sql, archiveFileProbe, failureInjector, aggregate, manifest, aligned,
                        cancellationToken)
                    .ConfigureAwait(false);
                stage = TierArchiveStage.Checkpoint;
                await ExecuteNonQueryAsync(connection, "CHECKPOINT;", cancellationToken).ConfigureAwait(false);
                return manifest.Build(aligned, noOp: false, TierArchiveStage.Completed);
            }
            catch (TierArchivedKeyConflictException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TierArchiveOperationException)
            {
                throw;
            }
            catch when (stage == TierArchiveStage.Preflight)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new TierArchiveOperationException(
                    stage,
                    manifest.Build(current ?? aligned, noOp: false, stage),
                    exception);
            }
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Rebuilds the complete published cold range into a new immutable generation. Hot rows below the
    ///     watermark win by the configured stable match key, so approved corrections and unseen late rows are
    ///     incorporated without overwriting the active Parquet files in place.
    /// </summary>
    public static Task<TierArchiveResult> ReconcileArchiveTierAsync<TRoot>(
        this DatabaseFacade database,
        CancellationToken cancellationToken = default)
        where TRoot : class
        => database.ReconcileArchiveTierAsync<TRoot>(new TierReconciliationOptions(), cancellationToken);

    /// <summary>
    ///     Rebuilds the published cold range using an explicit technical scope, caller-authorised tombstones,
    ///     Parquet writer controls, and bounded manifest evidence.
    /// </summary>
    public static async Task<TierArchiveResult> ReconcileArchiveTierAsync<TRoot>(
        this DatabaseFacade database,
        TierReconciliationOptions options,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "ReconcileArchiveTierAsync publishes a complete cold generation and cannot run inside a caller transaction.");
        }

        var (context, sql, archiveFileProbe, failureInjector) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        var boundary = DuckDBTierMaintenanceBoundary.Create(
            sql,
            aggregate,
            options.Scope,
            options.Tombstones,
            options.OmitScopeFromCold);
        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            await ExecuteNonQueryAsync(connection, DuckDBTierControl.ControlTableDdl(sql), cancellationToken)
                .ConfigureAwait(false);
            var activeArchiveBasePath = ReadActiveArchiveBasePath(connection, sql, aggregate);
            var currentRevision = ReadArchiveRevision(connection, sql, aggregate.ControlKey);
            EnsureArchiveSpecCompatible(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
            EnsurePartitionSpecCompatible(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
            var watermark = ReadWatermark(connection, sql, aggregate.ControlKey);
            if (watermark is null)
            {
                var empty = new DuckDBTierArchiveManifest(
                    aggregate,
                    TierArchiveOperation.Reconcile,
                    previousWatermark: null,
                    DateTime.MinValue,
                    DateTime.MinValue,
                    activeArchiveBasePath,
                    currentRevision,
                    options.Manifest);
                CaptureArchiveFiles(connection, archiveFileProbe, aggregate, empty);
                return empty.Build(DateTime.MinValue, noOp: true, TierArchiveStage.Completed);
            }

            var revision = CreateArchiveRevision();
            var replacementBasePath = aggregate.ArchiveBasePath + "/_revisions/" + revision;
            var manifest = new DuckDBTierArchiveManifest(
                aggregate,
                TierArchiveOperation.Reconcile,
                watermark,
                DateTime.MinValue,
                watermark.Value,
                replacementBasePath,
                revision,
                options.Manifest);
            var stage = TierArchiveStage.Preflight;
            try
            {
                await ThrowIfAmbiguousSharedBindingsAsync(
                        connection,
                        sql,
                        aggregate,
                        cancellationToken)
                    .ConfigureAwait(false);

                var activeRootPath = DuckDBTierArchiveManifest.NodeArchivePath(
                    activeArchiveBasePath, aggregate.Root.Table);
                if (archiveFileProbe.HasArchiveFiles(connection, activeRootPath))
                {
                    var lifecycleChanges = await ExecuteCountAsync(
                            connection,
                            DuckDBTierControl.RootLifecycleChangeCountSql(
                                sql, aggregate.Root.Table, aggregate.Root.Schema, aggregate.Root.KeyColumns,
                                aggregate.RootTimestampColumn, activeRootPath, watermark.Value,
                                aggregate.RootPartitions,
                                CombineAnd(
                                    boundary.RootScopePredicate("h"),
                                    Negate(boundary.RootTombstonePredicate("h")))),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (lifecycleChanges > 0)
                    {
                        throw new InvalidOperationException(
                            $"Tiered-storage aggregate '{aggregate.ControlKey}' has {lifecycleChanges} archived "
                            + "root change(s) whose lifecycle date was cleared or moved. Cold reconciliation can "
                            + "replace payload corrections and late rows, but moving an aggregate back to hot "
                            + "requires a separate restore workflow.");
                    }
                }

                var hotRows = 0L;
                foreach (var node in aggregate.Nodes)
                {
                    var hotSource = node.IsRoot
                        ? DuckDBTierControl.ReconcileRootSourceSql(
                            sql,
                            node.Table,
                            node.Schema,
                            node.Columns,
                            node.KeyColumns,
                            aggregate.RootTimestampColumn,
                            string.Empty,
                            aggregate.Granularity,
                            watermark.Value,
                            false,
                            aggregate.RootPartitions,
                            boundary.RootScopePredicate("h"),
                            boundary.DirectTombstonePredicate(node, "h"))
                        : DuckDBTierControl.ReconcileChildSourceSql(
                            sql,
                            node.Table,
                            node.Schema,
                            node.Columns,
                            node.KeyColumns,
                            node.ChainToRoot,
                            aggregate.RootTimestampColumn,
                            string.Empty,
                            aggregate.Granularity,
                            watermark.Value,
                            false,
                            aggregate.RootPartitions,
                            boundary.RootScopePredicate("t" + node.ChainToRoot.Count.ToString(CultureInfo.InvariantCulture)),
                            boundary.DirectTombstonePredicate(node, "t0"),
                            boundary.RootTombstonePredicate(
                                "t" + node.ChainToRoot.Count.ToString(CultureInfo.InvariantCulture)));
                    hotRows += await ExecuteCountAsync(
                            connection,
                            DuckDBTierControl.ReconcileSourceCountSql(hotSource),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (hotRows == 0 && !boundary.HasTombstones && !options.ForceRewrite)
                {
                    RegenerateViews(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
                    var noOp = new DuckDBTierArchiveManifest(
                        aggregate,
                        TierArchiveOperation.Reconcile,
                        watermark,
                        DateTime.MinValue,
                        watermark.Value,
                        activeArchiveBasePath,
                        currentRevision,
                        options.Manifest);
                    CaptureArchiveFiles(connection, archiveFileProbe, aggregate, noOp);
                    return noOp.Build(watermark.Value, noOp: true, TierArchiveStage.Completed);
                }

                await ValidateNonNullMatchKeysAsync(
                        connection, sql, aggregate, DateTime.MinValue, watermark.Value, cancellationToken)
                    .ConfigureAwait(false);

                var sources = new Dictionary<DuckDBTierNode, string>();
                foreach (var node in aggregate.Nodes)
                {
                    var activeNodePath = DuckDBTierArchiveManifest.NodeArchivePath(activeArchiveBasePath, node.Table);
                    var includeCold = archiveFileProbe.HasArchiveFiles(connection, activeNodePath);
                    var source = node.IsRoot
                        ? DuckDBTierControl.ReconcileRootSourceSql(
                            sql, node.Table, node.Schema, node.Columns, node.KeyColumns,
                            aggregate.RootTimestampColumn, activeNodePath, aggregate.Granularity,
                            watermark.Value, includeCold, aggregate.RootPartitions,
                            boundary.RootScopePredicate("h"),
                            boundary.DirectTombstonePredicate(node, "h"),
                            boundary.DirectTombstonePredicate(node, "c"))
                        : DuckDBTierControl.ReconcileChildSourceSql(
                            sql, node.Table, node.Schema, node.Columns, node.KeyColumns, node.ChainToRoot,
                            aggregate.RootTimestampColumn, activeNodePath, aggregate.Granularity,
                            watermark.Value, includeCold, aggregate.RootPartitions,
                            boundary.RootScopePredicate(
                                "t" + node.ChainToRoot.Count.ToString(CultureInfo.InvariantCulture)),
                            boundary.DirectTombstonePredicate(node, "t0"),
                            boundary.RootTombstonePredicate(
                                "t" + node.ChainToRoot.Count.ToString(CultureInfo.InvariantCulture)),
                            boundary.DirectTombstonePredicate(node, "c"),
                            boundary.RootTombstonePredicate(
                                "r" + (node.ChainToRoot.Count - 1).ToString(CultureInfo.InvariantCulture)),
                            activeArchiveBasePath);
                    sources[node] = source;
                    manifest.SetSelected(
                        node,
                        await ExecuteCountAsync(
                                connection, DuckDBTierControl.ReconcileSourceCountSql(source), cancellationToken)
                            .ConfigureAwait(false));
                }

                stage = TierArchiveStage.Copy;
                foreach (var node in aggregate.Nodes)
                {
                    var selected = manifest.SelectedRows(node);
                    if (selected == 0)
                    {
                        continue;
                    }

                    var replacementNodePath = manifest.ArchivePath(node);
                    EnsureLocalArchiveDirectory(replacementNodePath);
                    var copySql = node.IsRoot
                        ? DuckDBTierControl.ReconcileRootCopySql(
                            sql, sources[node], node.Columns, aggregate.RootTimestampColumn,
                            replacementNodePath, aggregate.Granularity, aggregate.RootPartitions, options.Writer)
                        : DuckDBTierControl.ReconcileChildCopySql(
                            sql, sources[node], replacementNodePath, aggregate.Granularity,
                            aggregate.RootPartitions, options.Writer);
                    await ExecuteNonQueryAsync(connection, copySql, cancellationToken).ConfigureAwait(false);
                }

                stage = TierArchiveStage.Verify;
                foreach (var node in aggregate.Nodes)
                {
                    var nodePath = manifest.ArchivePath(node);
                    var copied = archiveFileProbe.HasArchiveFiles(connection, nodePath)
                        ? await ExecuteCountAsync(
                                connection, DuckDBTierControl.ArchiveRowCountSql(nodePath), cancellationToken)
                            .ConfigureAwait(false)
                        : 0;
                    var selected = manifest.SelectedRows(node);
                    if (copied != selected)
                    {
                        throw new InvalidOperationException(
                            $"Tiered-storage reconciliation verification failed for table '{node.Table}': selected "
                            + $"{selected} row(s), but found {copied} row(s) in replacement Parquet.");
                    }

                    manifest.SetCopied(
                        node,
                        copied,
                        copied == 0
                            ? new DuckDBArchiveFileSummary(0, 0, [], IsTruncated: false)
                            : archiveFileProbe.GetArchiveFileSummary(
                                connection,
                                nodePath,
                                manifest.ManifestOptions));
                }

                stage = TierArchiveStage.Copy;
                failureInjector.ThrowIfRequested(DuckDBTierFailurePoint.AfterCopy, table: null);
                stage = TierArchiveStage.Publish;
                await PublishArchiveAsync(
                        connection, sql, archiveFileProbe, aggregate, replacementBasePath, revision,
                        watermark.Value, useInternalTransaction: !options.UseExistingTransaction, cancellationToken)
                    .ConfigureAwait(false);
                failureInjector.ThrowIfRequested(DuckDBTierFailurePoint.AfterPublication, table: null);
                if (boundary.IsUnbounded)
                {
                    stage = TierArchiveStage.DeleteHot;
                    await DeleteAggregateAsync(
                            connection, sql, archiveFileProbe, failureInjector, aggregate, manifest, watermark.Value,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!options.UseExistingTransaction)
                {
                    stage = TierArchiveStage.Checkpoint;
                    await ExecuteNonQueryAsync(connection, "CHECKPOINT;", cancellationToken).ConfigureAwait(false);
                }

                return manifest.Build(watermark.Value, noOp: false, TierArchiveStage.Completed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TierArchiveOperationException)
            {
                throw;
            }
            catch when (stage == TierArchiveStage.Preflight)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new TierArchiveOperationException(
                    stage,
                    manifest.Build(watermark.Value, noOp: false, stage),
                    exception);
            }
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }
    }

    /// <summary>Synchronous version of <c>ReconcileArchiveTierAsync</c>.</summary>
    public static TierArchiveResult ReconcileArchiveTier<TRoot>(this DatabaseFacade database)
        where TRoot : class
        => database.ReconcileArchiveTierAsync<TRoot>().GetAwaiter().GetResult();

    /// <summary>Synchronous reconciliation with explicit maintenance options.</summary>
    public static TierArchiveResult ReconcileArchiveTier<TRoot>(
        this DatabaseFacade database,
        TierReconciliationOptions options)
        where TRoot : class
        => database.ReconcileArchiveTierAsync<TRoot>(options).GetAwaiter().GetResult();

    /// <summary>Synchronous version of <c>ArchiveTierAsync</c>.</summary>
    public static TierArchiveResult ArchiveTier<TRoot>(this DatabaseFacade database, DateTime cutoff)
        where TRoot : class
        => database.ArchiveTierAsync<TRoot>(cutoff).GetAwaiter().GetResult();

    /// <summary>Synchronous forward archive with explicit writer and manifest options.</summary>
    public static TierArchiveResult ArchiveTier<TRoot>(
        this DatabaseFacade database,
        DateTime cutoff,
        TierArchiveOptions options)
        where TRoot : class
        => database.ArchiveTierAsync<TRoot>(cutoff, options).GetAwaiter().GetResult();

    /// <summary>
    ///     Returns read-only provider evidence for active, previously published, and locally discoverable
    ///     unpublished archive generations. No cleanup or retention decision is made.
    /// </summary>
    public static async Task<TierArchiveGenerationInventory> GetArchiveGenerationInventoryAsync<TRoot>(
        this DatabaseFacade database,
        int representativeFiles = 25,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        if (representativeFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(representativeFiles),
                representativeFiles,
                "The representative-file limit must be greater than zero.");
        }

        var (context, sql, archiveFileProbe, _) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            var hasControlTable = await TableExistsAsync(
                    connection,
                    DuckDBTierControl.ControlTable,
                    cancellationToken)
                .ConfigureAwait(false);
            var watermark = hasControlTable
                ? ReadWatermark(connection, sql, aggregate.ControlKey)
                : null;
            var activePath = hasControlTable
                             && await TableColumnExistsAsync(
                                     connection,
                                     DuckDBTierControl.ControlTable,
                                     "active_archive_path",
                                     cancellationToken)
                                 .ConfigureAwait(false)
                ? ReadActiveArchiveBasePath(connection, sql, aggregate)
                : aggregate.ArchiveBasePath;
            var activeGenerationId = watermark is null
                ? string.Empty
                : hasControlTable
                  && await TableColumnExistsAsync(
                          connection,
                          DuckDBTierControl.ControlTable,
                          "archive_revision",
                          cancellationToken)
                      .ConfigureAwait(false)
                    ? ReadArchiveRevision(connection, sql, aggregate.ControlKey) ?? "base"
                    : "base";
            var generations = new List<TierArchiveGenerationInfo>();
            var recordedIds = new HashSet<string>(StringComparer.Ordinal);
            var recorded = new List<(string Id, string Path, DateTime Watermark, DateTime CreatedAt, long Files, long Bytes)>();

            var hasGenerationCatalogue = await TableExistsAsync(
                    connection,
                    DuckDBTierControl.GenerationTable,
                    cancellationToken)
                .ConfigureAwait(false)
                && await TableExistsAsync(
                        connection,
                        DuckDBTierControl.GenerationNodeTable,
                        cancellationToken)
                    .ConfigureAwait(false)
                && await TableExistsAsync(
                        connection,
                        DuckDBTierControl.GenerationFileTable,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (hasGenerationCatalogue)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = DuckDBTierControl.ReadGenerationInventorySql(sql, aggregate.ControlKey);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var generationId = reader.GetString(0);
                    recordedIds.Add(generationId);
                    recorded.Add((
                        generationId,
                        reader.GetString(1),
                        reader.GetDateTime(2),
                        reader.GetDateTime(3),
                        reader.GetInt64(4),
                        reader.GetInt64(5)));
                }
            }

            foreach (var generation in recorded)
            {
                var files = await ReadGenerationFilesAsync(
                        connection,
                        sql,
                        aggregate.ControlKey,
                        generation.Id,
                        representativeFiles,
                        cancellationToken)
                    .ConfigureAwait(false);
                generations.Add(new TierArchiveGenerationInfo(
                    generation.Id,
                    generation.Id == activeGenerationId
                        ? TierArchiveGenerationState.Active
                        : TierArchiveGenerationState.Published,
                    generation.Path,
                    generation.Watermark,
                    generation.CreatedAt,
                    generation.Files,
                    generation.Bytes,
                    files));
            }

            if (watermark is { } activeWatermark && !recordedIds.Contains(activeGenerationId))
            {
                long fileCount = 0;
                long totalBytes = 0;
                var files = new List<string>();
                foreach (var node in aggregate.Nodes)
                {
                    var nodePath = DuckDBTierArchiveManifest.NodeArchivePath(activePath, node.Table);
                    if (!archiveFileProbe.HasArchiveFiles(connection, nodePath))
                    {
                        continue;
                    }

                    var summary = archiveFileProbe.GetArchiveFileSummary(
                        connection,
                        nodePath,
                        new TierManifestOptions
                        {
                            Detail = TierManifestDetail.RepresentativeFiles,
                            MaxFilesPerNode = representativeFiles,
                        });
                    fileCount += summary.FileCount;
                    totalBytes += summary.TotalBytes;
                    files.AddRange(summary.Files.Take(Math.Max(0, representativeFiles - files.Count)));
                }

                generations.Add(new TierArchiveGenerationInfo(
                    activeGenerationId,
                    TierArchiveGenerationState.Active,
                    activePath,
                    activeWatermark,
                    DateTime.MinValue,
                    fileCount,
                    totalBytes,
                    files));
            }

            AddLocalUnpublishedCandidates(
                aggregate,
                watermark ?? DateTime.MinValue,
                activeGenerationId,
                recordedIds,
                representativeFiles,
                generations);
            return new TierArchiveGenerationInventory(
                aggregate.ControlKey,
                activeGenerationId,
                generations.OrderByDescending(generation => generation.CreatedAtUtc).ToArray())
            {
                Binding = BindingInfo(aggregate),
            };
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }
    }

    /// <summary>Synchronous version of <c>GetArchiveGenerationInventoryAsync</c>.</summary>
    public static TierArchiveGenerationInventory GetArchiveGenerationInventory<TRoot>(
        this DatabaseFacade database,
        int representativeFiles = 25)
        where TRoot : class
        => database.GetArchiveGenerationInventoryAsync<TRoot>(representativeFiles).GetAwaiter().GetResult();

    /// <summary>
    ///     Creates a read-only cleanup plan for caller-selected non-active generations. The application remains
    ///     responsible for retention, legal hold, and object-store deletion policy.
    /// </summary>
    public static async Task<TierArchiveCleanupPlan> PlanArchiveGenerationCleanupAsync<TRoot>(
        this DatabaseFacade database,
        IEnumerable<string> generationIds,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(generationIds);
        var selected = generationIds
            .Select(id => id?.Trim())
            .Where(id => !string.IsNullOrEmpty(id))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (selected.Length == 0)
        {
            throw new ArgumentException("At least one generation identifier is required.", nameof(generationIds));
        }

        var inventory = await database.GetArchiveGenerationInventoryAsync<TRoot>(
                representativeFiles: 1,
                cancellationToken)
            .ConfigureAwait(false);
        var byId = inventory.Generations.ToDictionary(generation => generation.GenerationId, StringComparer.Ordinal);
        var candidates = new List<TierArchiveCleanupCandidate>(selected.Length);
        foreach (var generationId in selected)
        {
            if (!byId.TryGetValue(generationId, out var generation))
            {
                throw new InvalidOperationException(
                    $"Archive generation '{generationId}' is not present in provider inventory.");
            }

            if (generation.State == TierArchiveGenerationState.Active)
            {
                throw new InvalidOperationException(
                    $"Archive generation '{generationId}' is active and cannot be a cleanup candidate.");
            }

            candidates.Add(new TierArchiveCleanupCandidate(
                generation.GenerationId,
                generation.State,
                generation.ArchivePath,
                generation.FileCount,
                generation.TotalBytes));
        }

        var fingerprintInput = inventory.ControlKey + "\n"
                               + inventory.ActiveGenerationId + "\n"
                               + string.Join(
                                   "\n",
                                   candidates.Select(candidate =>
                                       $"{candidate.GenerationId}|{candidate.State}|{candidate.ArchivePath}|"
                                       + $"{candidate.FileCount}|{candidate.TotalBytes}"));
        return new TierArchiveCleanupPlan(
            inventory.ControlKey,
            inventory.ActiveGenerationId,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))),
            candidates)
        {
            Binding = inventory.Binding,
        };
    }

    /// <summary>
    ///     Checks non-secret storage capabilities for a configured tiered aggregate. The default is read-only;
    ///     no credentials, secret definitions, or signed URLs are returned.
    /// </summary>
    public static async Task<TierStoragePreflightResult> PreflightTieredStorageAsync<TRoot>(
        this DatabaseFacade database,
        TierStoragePreflightOptions? options = null,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        options ??= new TierStoragePreflightOptions();
        var (context, sql, _, _) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        var scheme = ArchiveScheme(aggregate.ArchiveBasePath);
        var capabilities = new List<TierStorageCapabilityResult>();
        var knownScheme = KnownArchiveSchemes.Contains(scheme);
        capabilities.Add(new TierStorageCapabilityResult(
            TierStorageCapability.Scheme,
            knownScheme,
            knownScheme
                ? $"Archive scheme '{scheme}' is supported."
                : $"Archive scheme '{scheme}' is not recognised by tiered storage."));

        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            var ambiguity = await FindAmbiguousSharedBindingAsync(
                    connection,
                    sql,
                    aggregate,
                    cancellationToken)
                .ConfigureAwait(false);
            capabilities.Add(new TierStorageCapabilityResult(
                TierStorageCapability.BindingOwnership,
                ambiguity is null,
                ambiguity is null
                    ? "Every shared child row belongs to at most one configured root binding."
                    : ambiguity.Message));

            var requiredExtensions = RequiredArchiveExtensions(scheme);
            if (requiredExtensions.Count == 0)
            {
                capabilities.Add(new TierStorageCapabilityResult(
                    TierStorageCapability.ExtensionInstalled,
                    true,
                    "No external storage extension is required for this scheme."));
                capabilities.Add(new TierStorageCapabilityResult(
                    TierStorageCapability.ExtensionLoaded,
                    true,
                    "No external storage extension is required for this scheme."));
            }
            else
            {
                foreach (var extension in requiredExtensions)
                {
                    var installed = ReadExtensionState(connection, extension, "installed");
                    var loaded = ReadExtensionState(connection, extension, "loaded");
                    capabilities.Add(new TierStorageCapabilityResult(
                        TierStorageCapability.ExtensionInstalled,
                        installed,
                        $"DuckDB extension '{extension}' is {(installed ? "installed" : "not installed")}."));
                    capabilities.Add(new TierStorageCapabilityResult(
                        TierStorageCapability.ExtensionLoaded,
                        loaded,
                        $"DuckDB extension '{extension}' is {(loaded ? "loaded" : "not loaded")}."));
                }
            }

            long listedFiles;
            var listingSucceeded = false;
            try
            {
                listedFiles = Convert.ToInt64(
                    await ExecuteScalarAsync(
                            connection,
                            $"SELECT count(*) FROM glob('{DuckDBTierControl.ReadGlob(aggregate.ArchiveBasePath).Replace("'", "''")}');",
                            cancellationToken)
                        .ConfigureAwait(false),
                    CultureInfo.InvariantCulture);
                listingSucceeded = true;
                capabilities.Add(new TierStorageCapabilityResult(
                    TierStorageCapability.List,
                    true,
                    $"Archive prefix listing succeeded and found {listedFiles} Parquet object(s)."));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                listedFiles = 0;
                capabilities.Add(new TierStorageCapabilityResult(
                    TierStorageCapability.List,
                    false,
                    "Archive prefix listing failed: " + RedactDiagnosticMessage(exception)));
            }

            if (listedFiles == 0)
            {
                capabilities.Add(new TierStorageCapabilityResult(
                    TierStorageCapability.Read,
                    false,
                    listingSucceeded
                        ? "Read capability was not tested because no existing Parquet object was available."
                        : "Read capability was not tested because archive prefix listing failed.")
                {
                    WasTested = false,
                });
            }
            else
            {
                try
                {
                    await ExecuteScalarAsync(
                            connection,
                            $"SELECT 1 FROM read_parquet('{DuckDBTierControl.ReadGlob(aggregate.ArchiveBasePath).Replace("'", "''")}', "
                            + "hive_partitioning = true, union_by_name = true) LIMIT 1;",
                            cancellationToken)
                        .ConfigureAwait(false);
                    capabilities.Add(new TierStorageCapabilityResult(
                        TierStorageCapability.Read,
                        true,
                        "An existing Parquet object was read successfully."));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    capabilities.Add(new TierStorageCapabilityResult(
                        TierStorageCapability.Read,
                        false,
                        "Archive read failed: " + RedactDiagnosticMessage(exception)));
                }
            }

            if (options.ProbeWriteAndDelete)
            {
                await ProbeWriteAndDeleteAsync(
                        connection,
                        aggregate.ArchiveBasePath,
                        capabilities,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }

        return new TierStoragePreflightResult(
            scheme,
            RedactArchivePath(aggregate.ArchiveBasePath),
            capabilities)
        {
            Binding = BindingInfo(aggregate),
        };
    }

    /// <summary>
    ///     Deletes cold-archive partitions of the whole aggregate whose period is before <paramref name="olderThan" />
    ///     (aligned down to the granularity). The hot tables and watermark are untouched.
    /// </summary>
    /// <returns>The number of partition directories deleted across all aggregate tables.</returns>
    public static int PurgeArchiveOlderThan<TRoot>(this DatabaseFacade database, DateTime olderThan)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        var (context, sql, archiveFileProbe, _) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot)) ?? throw NotConfigured(typeof(TRoot));

        if (IsRemoteArchive(aggregate.ArchiveBasePath))
        {
            throw new NotSupportedException(
                $"PurgeArchiveOlderThan is not supported for the remote archive '{aggregate.Root.ArchiveSubPath}'. "
                + "Object stores cannot delete files through DuckDB; enforce retention with a bucket lifecycle rule on "
                + "the archive prefix instead.");
        }

        var cutoff = DuckDBTierControl.AlignCutoff(olderThan, aggregate.Granularity);
        var openedHere = OpenTracked(database);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            ExecuteNonQuery(connection, DuckDBTierControl.ControlTableDdl(sql));
            var activeArchiveBasePath = ReadActiveArchiveBasePath(connection, sql, aggregate);
            var deleted = aggregate.Nodes.Sum(
                node => PurgePartitions(
                    DuckDBTierArchiveManifest.NodeArchivePath(activeArchiveBasePath, node.Table), aggregate, cutoff));

            if (deleted > 0)
            {
                // Regenerate the views so a table whose archive is now completely empty falls back to a hot-only
                // view instead of leaving a cold read_parquet() branch over a glob that matches no files (which
                // would throw on every query).
                RegenerateViews(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
            }

            return deleted;
        }
        finally
        {
            CloseTracked(database, openedHere);
        }
    }

    private static string CopySql(
        ISqlGenerationHelper sql,
        DuckDBTierAggregate aggregate,
        DuckDBTierNode node,
        string archivePath,
        DateTime from,
        DateTime cutoff,
        TierParquetWriterOptions writerOptions)
        => node.IsRoot
            ? DuckDBTierControl.ArchiveCopySql(
                sql, node.Table, node.Schema, node.Columns, aggregate.RootTimestampColumn, archivePath,
                aggregate.Granularity, from, cutoff, aggregate.RootPartitions, writerOptions)
            : DuckDBTierControl.ArchiveChildCopySql(
                sql, node.Table, node.Schema, node.Columns, node.ChainToRoot, aggregate.RootTimestampColumn,
                archivePath, aggregate.Granularity, from, cutoff, aggregate.RootPartitions, writerOptions);

    private static void RegenerateViews(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        string activeArchiveBasePath)
    {
        var hasWatermark = ReadWatermark(connection, sql, aggregate.ControlKey) is not null;
        var activeGenerationId = ReadArchiveRevision(connection, sql, aggregate.ControlKey) ?? "base";
        foreach (var node in aggregate.Nodes.Where(node => node.IsRoot))
        {
            if (node.ViewName is null)
            {
                continue;
            }

            var archivePath = DuckDBTierArchiveManifest.NodeArchivePath(activeArchiveBasePath, node.Table);
            var includeCold = hasWatermark && archiveFileProbe.HasArchiveFiles(connection, archivePath);
            var cataloguedFiles = includeCold && IsRemoteArchive(archivePath)
                ? ReadCataloguedNodeFiles(
                    connection,
                    sql,
                    aggregate.ControlKey,
                    activeGenerationId,
                    node.Table)
                : null;
            ExecuteNonQuery(
                connection,
                DuckDBTierControl.ViewSql(
                    sql, node.ViewName, node.Table, node.Schema, node.Columns, node.KeyColumns,
                    aggregate.RootTimestampColumn, aggregate.ControlKey, archivePath,
                    aggregate.Granularity, includeCold, aggregate.RootPartitions, cataloguedFiles));
        }

        var allAggregates = DuckDBTierAggregate.ResolveAll(aggregate.Model);
        var childEntities = aggregate.Nodes
            .Where(node => !node.IsRoot && node.ViewName is not null)
            .Select(node => node.Entity.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var entityName in childEntities)
        {
            var entries = allAggregates
                .SelectMany(candidate => candidate.Nodes
                    .Where(node => !node.IsRoot
                                   && node.Entity.Name == entityName
                                   && node.ViewName is not null)
                    .Select(node => (Aggregate: candidate, Node: node)))
                .OrderBy(entry => entry.Node.BindingId, StringComparer.Ordinal)
                .ToArray();
            if (entries.Length == 0)
            {
                continue;
            }

            var prototype = entries[0].Node;
            var bindings = new List<DuckDBTierControl.TierChildViewBinding>(entries.Length);
            foreach (var entry in entries)
            {
                var candidate = entry.Aggregate;
                var candidateActiveBasePath = candidate.ControlKey == aggregate.ControlKey
                    ? activeArchiveBasePath
                    : ReadActiveArchiveBasePath(connection, sql, candidate);
                var candidateHasWatermark = ReadWatermark(connection, sql, candidate.ControlKey) is not null;
                var candidateGenerationId = ReadArchiveRevision(connection, sql, candidate.ControlKey) ?? "base";
                var candidateArchivePath = DuckDBTierArchiveManifest.NodeArchivePath(
                    candidateActiveBasePath,
                    entry.Node.Table);
                var candidateIncludeCold = candidateHasWatermark
                                           && archiveFileProbe.HasArchiveFiles(connection, candidateArchivePath);
                var candidateFiles = candidateIncludeCold && IsRemoteArchive(candidateArchivePath)
                    ? ReadCataloguedNodeFiles(
                        connection,
                        sql,
                        candidate.ControlKey,
                        candidateGenerationId,
                        entry.Node.Table)
                    : null;
                bindings.Add(new DuckDBTierControl.TierChildViewBinding(
                    entry.Node.BindingId,
                    entry.Node.ChainToRoot,
                    candidate.RootTimestampColumn,
                    candidate.ControlKey,
                    candidateArchivePath,
                    candidate.Granularity,
                    candidateIncludeCold,
                    candidate.IncludeHotChildFilter,
                    candidate.RootPartitions,
                    candidateFiles));
            }

            ExecuteNonQuery(
                connection,
                DuckDBTierControl.SharedChildViewSql(
                    sql,
                    prototype.ViewName!,
                    prototype.Table,
                    prototype.Schema,
                    prototype.Columns,
                    prototype.KeyColumns,
                    bindings));
        }
    }

    private static void EnsurePartitionSpecCompatible(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        string activeArchiveBasePath)
    {
        var recorded = ExecuteScalar(connection, DuckDBTierControl.ReadPartitionSpecSql(sql, aggregate.ControlKey)) as string;
        var recordedGranularity = ExecuteScalar(
            connection,
            DuckDBTierControl.ReadGranularitySql(sql, aggregate.ControlKey)) as string;
        if (PartitionLayoutsMatch(recorded, recordedGranularity, aggregate))
        {
            return;
        }

        if (!aggregate.Nodes.Any(
                node => archiveFileProbe.HasArchiveFiles(
                    connection,
                    DuckDBTierArchiveManifest.NodeArchivePath(activeArchiveBasePath, node.Table))))
        {
            return;
        }

        var configured = aggregate.RootPartitions.Count == 0
            ? "temporal partitions only"
            : string.Join(", ", aggregate.RootPartitions.Select(partition => partition.Name));
        var existingLayout = recorded is null && recordedGranularity is null
            ? "an unrecorded partition layout"
            : "a different partition layout";
        throw new InvalidOperationException(
            $"Tiered-storage aggregate '{aggregate.ControlKey}' is configured for {configured}, but its existing "
            + $"Parquet archive has {existingLayout}. Rewrite or clear the cold archive "
            + "before changing PartitionBy(...) or granularity; mixed Hive layouts are not supported.");
    }

    private static void EnsureArchiveSpecCompatible(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        string activeArchiveBasePath)
    {
        var recordedPath = ExecuteScalar(
            connection,
            DuckDBTierControl.ReadArchivePathSql(sql, aggregate.ControlKey)) as string;
        var recordedJson = ExecuteScalar(
            connection,
            DuckDBTierControl.ReadArchiveSpecSql(sql, aggregate.ControlKey)) as string;

        DuckDBTierArchiveContract? recorded = null;
        if (recordedJson is not null)
        {
            try
            {
                recorded = JsonSerializer.Deserialize<DuckDBTierArchiveContract>(recordedJson);
                if (recorded is null
                    || recorded.Nodes is null
                    || recorded.Nodes.Any(node => node.Columns is null || node.MatchKeyColumns is null))
                {
                    throw new JsonException("The archive contract is missing required fields.");
                }
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException(
                    $"Tiered-storage aggregate '{aggregate.ControlKey}' has unreadable persisted archive metadata. "
                    + "Repair or migrate the control row before writing more Parquet files.",
                    exception);
            }
        }

        if (!HasExistingArchive(
                connection, archiveFileProbe, aggregate, activeArchiveBasePath, recordedPath, recorded))
        {
            return;
        }

        var configuredPath = NormalizeArchivePath(aggregate.Root.ArchiveSubPath);
        var existingPath = NormalizeArchivePath(recorded?.ArchivePath ?? recordedPath ?? configuredPath);
        if (!string.Equals(existingPath, configuredPath, StringComparison.Ordinal))
        {
            throw ArchiveContractChanged(
                aggregate,
                $"archive path changed from '{existingPath}' to '{configuredPath}'");
        }

        // Archives created before the versioned contract was introduced can be adopted at their existing path.
        // The older partition metadata is still checked separately; subsequent changes are protected once the
        // current contract is persisted by the caller.
        if (recorded is null)
        {
            if (aggregate.Nodes.Any(node => node.Entity.GetTieredStoreMatchProperties().Count > 0))
            {
                throw ArchiveContractChanged(
                    aggregate,
                    "the existing legacy archive predates match-key metadata and cannot prove the configured MatchBy(...) layout");
            }

            return;
        }

        var configured = JsonSerializer.Deserialize<DuckDBTierArchiveContract>(aggregate.ArchiveSpec)
            ?? throw new InvalidOperationException("The generated tiered-storage archive contract is empty.");
        if (!ArchiveContractsMatch(recorded, configured, out var reason))
        {
            throw ArchiveContractChanged(aggregate, reason);
        }
    }

    private static bool HasExistingArchive(
        DuckDBConnection connection,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        string activeArchiveBasePath,
        string? recordedPath,
        DuckDBTierArchiveContract? recorded)
    {
        if (aggregate.Nodes.Any(
                node => archiveFileProbe.HasArchiveFiles(
                    connection,
                    DuckDBTierArchiveManifest.NodeArchivePath(activeArchiveBasePath, node.Table))))
        {
            return true;
        }

        var oldRootPath = recorded?.ArchivePath ?? recordedPath;
        if (oldRootPath is null)
        {
            return false;
        }

        if (archiveFileProbe.HasArchiveFiles(connection, oldRootPath))
        {
            return true;
        }

        if (recorded is null || recorded.Nodes.Count == 0)
        {
            return false;
        }

        var oldBasePath = ArchiveBasePath(oldRootPath, recorded.Nodes[0].Table);
        return recorded.Nodes.Any(
            node => archiveFileProbe.HasArchiveFiles(
                connection,
                oldBasePath + "/" + node.Table));
    }

    private static bool ArchiveContractsMatch(
        DuckDBTierArchiveContract recorded,
        DuckDBTierArchiveContract configured,
        out string reason)
    {
        if (recorded.Version != configured.Version)
        {
            reason = $"contract version changed from {recorded.Version} to {configured.Version}";
            return false;
        }

        // Partition and granularity compatibility have their own persisted layout validator and more actionable
        // migration error. Let it report those differences before comparing the remaining aggregate contract.
        if (recorded.Granularity != configured.Granularity
            || !string.Equals(recorded.PartitionSpec, configured.PartitionSpec, StringComparison.Ordinal))
        {
            reason = string.Empty;
            return true;
        }

        if (!string.Equals(recorded.ControlKey, configured.ControlKey, StringComparison.Ordinal)
            || !string.Equals(recorded.LifecycleColumn, configured.LifecycleColumn, StringComparison.Ordinal))
        {
            reason = "the lifecycle, granularity, control key, or partition layout changed";
            return false;
        }

        var recordedNodes = recorded.Nodes.ToDictionary(NodeIdentity, StringComparer.Ordinal);
        var configuredNodes = configured.Nodes.ToDictionary(NodeIdentity, StringComparer.Ordinal);
        if (recordedNodes.Count != configuredNodes.Count
            || recordedNodes.Keys.Any(key => !configuredNodes.ContainsKey(key)))
        {
            reason = "the aggregate table/include layout changed";
            return false;
        }

        foreach (var (identity, oldNode) in recordedNodes)
        {
            var newNode = configuredNodes[identity];
            if (!oldNode.MatchKeyColumns.SequenceEqual(newNode.MatchKeyColumns, StringComparer.Ordinal))
            {
                reason = $"match-key layout changed for '{identity}'";
                return false;
            }

            var oldComparisonColumns = oldNode.ComparisonColumns
                                       ?? oldNode.Columns.Select(column => column.Name).ToArray();
            var newComparisonColumns = newNode.ComparisonColumns ?? [];
            if (oldComparisonColumns.Any(
                    column => !newComparisonColumns.Contains(column, StringComparer.Ordinal))
                || newComparisonColumns.Any(
                    column => !oldComparisonColumns.Contains(column, StringComparer.Ordinal)
                              && oldNode.Columns.Any(
                                  oldColumn => string.Equals(oldColumn.Name, column, StringComparison.Ordinal))))
            {
                reason = $"match-key comparison layout changed for '{identity}'";
                return false;
            }

            var newColumns = newNode.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
            foreach (var oldColumn in oldNode.Columns)
            {
                if (!newColumns.TryGetValue(oldColumn.Name, out var newColumn))
                {
                    reason = $"archived column '{identity}.{oldColumn.Name}' was removed or renamed";
                    return false;
                }

                if (!StoreTypesMatch(oldColumn.StoreType, newColumn.StoreType))
                {
                    reason = $"archived column '{identity}.{oldColumn.Name}' changed type from "
                             + $"'{oldColumn.StoreType}' to '{newColumn.StoreType}'";
                    return false;
                }

                if (oldColumn.IsNullable && !newColumn.IsNullable)
                {
                    reason = $"archived column '{identity}.{oldColumn.Name}' changed from nullable to required";
                    return false;
                }
            }

            var oldColumnNames = oldNode.Columns.Select(column => column.Name).ToHashSet(StringComparer.Ordinal);
            var unsafeAddition = newNode.Columns.FirstOrDefault(
                column => !oldColumnNames.Contains(column.Name) && !column.IsNullable);
            if (unsafeAddition is not null)
            {
                reason = $"new archived column '{identity}.{unsafeAddition.Name}' is required; only nullable columns "
                         + "can be added without migrating existing Parquet files";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static string NodeIdentity(DuckDBTierArchiveNodeContract node)
        => (node.Schema is null ? string.Empty : node.Schema + ".") + node.Table;

    private static bool StoreTypesMatch(string left, string right)
        => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException ArchiveContractChanged(
        DuckDBTierAggregate aggregate,
        string reason)
        => new(
            $"Tiered-storage aggregate '{aggregate.ControlKey}' cannot use its existing Parquet archive because "
            + $"{reason}. Migrate or clear the cold archive before changing the path, MatchBy(...), included tables, "
            + "or archived schema.");

    private static string ArchiveBasePath(string rootArchivePath, string rootTable)
    {
        var normalized = NormalizeArchivePath(rootArchivePath);
        var suffix = "/" + rootTable;
        return normalized.EndsWith(suffix, StringComparison.Ordinal)
            ? normalized[..^suffix.Length]
            : normalized[..Math.Max(0, normalized.LastIndexOf('/'))];
    }

    private static string NormalizeArchivePath(string path)
        => path.Replace('\\', '/').TrimEnd('/');

    private static bool PartitionLayoutsMatch(
        string? recordedSpec,
        string? recordedGranularity,
        DuckDBTierAggregate aggregate)
    {
        if (string.Equals(recordedSpec, aggregate.PartitionSpec, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(
                recordedGranularity,
                aggregate.Granularity.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (recordedSpec is null)
        {
            // Control rows written before root-owned partitions existed describe the legacy temporal-only layout.
            return aggregate.RootPartitions.Count == 0;
        }

        try
        {
            // Accept the short-lived pre-versioned representation so development databases created while this
            // feature was being introduced can be upgraded without rewriting compatible files.
            var legacyColumns = JsonSerializer.Deserialize<string[]>(recordedSpec);
            return legacyColumns is not null
                && legacyColumns.SequenceEqual(aggregate.RootPartitionColumns, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task DeleteAggregateAsync(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        IDuckDBArchiveFileProbe archiveFileProbe,
        IDuckDBTierFailureInjector failureInjector,
        DuckDBTierAggregate aggregate,
        DuckDBTierArchiveManifest manifest,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        // Each delete autocommits when the caller has no transaction. This is required by DuckDB's immediate
        // FK enforcement: deleting all descendants and then a principal inside one transaction is rejected.
        // A crash between statements is safe because each view anti-joins hot rows already present in cold
        // Parquet, and a rerun removes any remaining hot rows.
        for (var i = aggregate.Nodes.Count - 1; i >= 0; i--)
        {
            var node = aggregate.Nodes[i];
            var archivePath = manifest.ArchivePath(node);
            if (!archiveFileProbe.HasArchiveFiles(connection, archivePath))
            {
                continue;
            }

            var rowsBefore = await ExecuteCountAsync(
                    connection, DuckDBTierControl.HotTableCountSql(sql, node.Table, node.Schema), cancellationToken)
                .ConfigureAwait(false);
            var archiveColumns = archiveFileProbe.GetArchiveColumns(connection, archivePath);
            var deleteSql = node.IsRoot
                ? DuckDBTierControl.DeleteHotSql(
                    sql, node.Table, node.Schema, node.KeyColumns, node.ComparisonColumns, aggregate.RootTimestampColumn,
                    archivePath, cutoff, aggregate.RootPartitions, archiveColumns)
                : DuckDBTierControl.DeleteChildSql(
                    sql, node.Table, node.Schema, node.KeyColumns, node.ComparisonColumns, node.ChainToRoot,
                    aggregate.RootTimestampColumn, archivePath, cutoff, archiveColumns);
            await ExecuteNonQueryAsync(connection, deleteSql, cancellationToken).ConfigureAwait(false);
            var rowsAfter = await ExecuteCountAsync(
                    connection, DuckDBTierControl.HotTableCountSql(sql, node.Table, node.Schema), cancellationToken)
                .ConfigureAwait(false);
            manifest.AddDeleted(node, Math.Max(0, rowsBefore - rowsAfter));
            failureInjector.ThrowIfRequested(DuckDBTierFailurePoint.AfterNodeDelete, node.Table);
        }
    }

    private static async Task ThrowIfArchivedConflictsAsync(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        string activeArchiveBasePath,
        DateTime watermark,
        CancellationToken cancellationToken)
    {
        foreach (var node in aggregate.Nodes)
        {
            var archivePath = DuckDBTierArchiveManifest.NodeArchivePath(activeArchiveBasePath, node.Table);
            if (!archiveFileProbe.HasArchiveFiles(connection, archivePath))
            {
                continue;
            }

            var archiveColumns = archiveFileProbe.GetArchiveColumns(connection, archivePath);
            var commandText = node.IsRoot
                ? DuckDBTierControl.RootConflictCountSql(
                    sql, node.Table, node.Schema, node.KeyColumns, node.ComparisonColumns,
                    aggregate.RootTimestampColumn, archivePath, watermark, aggregate.RootPartitions, archiveColumns)
                : DuckDBTierControl.ChildConflictCountSql(
                    sql, node.Table, node.Schema, node.KeyColumns, node.ComparisonColumns,
                    aggregate.RootTimestampColumn, aggregate.ControlKey, archivePath,
                    aggregate.Granularity, aggregate.RootPartitions, archiveColumns);
            var conflicts = await ExecuteCountAsync(connection, commandText, cancellationToken).ConfigureAwait(false);
            if (conflicts > 0)
            {
                throw new TierArchivedKeyConflictException(
                    node.Entity.ClrType,
                    node.Table,
                    conflicts,
                    BindingInfo(aggregate));
            }
        }
    }

    private static async Task ValidateNonNullMatchKeysAsync(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        DuckDBTierAggregate aggregate,
        DateTime from,
        DateTime cutoff,
        CancellationToken cancellationToken)
    {
        foreach (var node in aggregate.Nodes)
        {
            var nullMatchKeys = await ExecuteCountAsync(
                    connection,
                    DuckDBTierControl.NullMatchKeyCountSql(
                        sql, node.Table, node.Schema, node.KeyColumns, node.ChainToRoot,
                        aggregate.RootTimestampColumn, from, cutoff),
                    cancellationToken)
                .ConfigureAwait(false);
            if (nullMatchKeys > 0)
            {
                throw new InvalidOperationException(
                    $"Tiered-storage table '{node.Table}' has {nullMatchKeys} archiveable row(s) with a NULL "
                    + "configured match-key component. Match keys must be non-null for every archiveable row; "
                    + "no archive files were written by this run.");
            }
        }
    }

    private static async Task ThrowIfAmbiguousSharedBindingsAsync(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        DuckDBTierAggregate aggregate,
        CancellationToken cancellationToken)
    {
        var ambiguity = await FindAmbiguousSharedBindingAsync(
                connection,
                sql,
                aggregate,
                cancellationToken)
            .ConfigureAwait(false);
        if (ambiguity is not null)
        {
            throw ambiguity;
        }
    }

    private static async Task<TierAmbiguousBindingException?> FindAmbiguousSharedBindingAsync(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        DuckDBTierAggregate aggregate,
        CancellationToken cancellationToken)
    {
        var sharedChildren = DuckDBTierAggregate.ResolveAll(aggregate.Model)
            .SelectMany(candidate => candidate.Nodes
                .Where(node => !node.IsRoot)
                .Select(node => (Aggregate: candidate, Node: node)))
            .GroupBy(entry => entry.Node.Entity.Name, StringComparer.Ordinal)
            .Where(group => group.Select(entry => entry.Aggregate.BindingId).Distinct(StringComparer.Ordinal).Count() > 1
                            && group.Any(entry => entry.Aggregate.BindingId == aggregate.BindingId))
            .ToArray();

        foreach (var sharedChild in sharedChildren)
        {
            var entries = sharedChild
                .OrderBy(entry => entry.Node.BindingId, StringComparer.Ordinal)
                .ToArray();
            var prototype = entries[0].Node;
            if (!await TableExistsAsync(
                    connection,
                    prototype.Table,
                    prototype.Schema,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                continue;
            }

            var count = await ExecuteCountAsync(
                    connection,
                    DuckDBTierControl.AmbiguousChildBindingCountSql(
                        sql,
                        prototype.Table,
                        prototype.Schema,
                        entries.Select(entry => new DuckDBTierControl.TierOwnershipBinding(
                            entry.Node.BindingId,
                            entry.Node.ChainToRoot)).ToArray()),
                    cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                continue;
            }

            return new TierAmbiguousBindingException(
                prototype.Table,
                count,
                entries.Select(entry => new TieredStorageBindingInfo(
                    entry.Node.BindingId,
                    entry.Aggregate.Root.Entity.ClrType,
                    entry.Aggregate.ControlKey)));
        }

        return null;
    }

    private static TieredStorageBindingInfo BindingInfo(DuckDBTierAggregate aggregate)
        => new(aggregate.BindingId, aggregate.Root.Entity.ClrType, aggregate.ControlKey);

    private static void CaptureArchiveFiles(
        DuckDBConnection connection,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        DuckDBTierArchiveManifest manifest)
    {
        foreach (var node in aggregate.Nodes)
        {
            var archivePath = manifest.ArchivePath(node);
            if (archiveFileProbe.HasArchiveFiles(connection, archivePath))
            {
                manifest.SetFileSummary(
                    node,
                    archiveFileProbe.GetArchiveFileSummary(
                        connection,
                        archivePath,
                        manifest.ManifestOptions));
            }
        }
    }

    private static async Task PublishArchiveAsync(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        string activeArchiveBasePath,
        string? revision,
        DateTime watermark,
        bool useInternalTransaction,
        CancellationToken cancellationToken)
    {
        if (useInternalTransaction)
        {
            await ExecuteNonQueryAsync(connection, "BEGIN TRANSACTION;", cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var generationId = revision ?? "base";
            await ExecuteNonQueryAsync(
                    connection,
                    DuckDBTierControl.UpsertArchiveSpecSql(
                        sql,
                        aggregate.ControlKey,
                        aggregate.Root.ArchiveSubPath,
                        aggregate.ArchiveSpec),
                    cancellationToken)
                .ConfigureAwait(false);
            await ExecuteNonQueryAsync(
                    connection,
                    DuckDBTierControl.UpsertGenerationSql(
                        sql,
                        aggregate.ControlKey,
                        generationId,
                        activeArchiveBasePath,
                        watermark,
                        DateTime.UtcNow,
                        aggregate.ArchiveSpec,
                        aggregate.PartitionSpec),
                    cancellationToken)
                .ConfigureAwait(false);
            foreach (var node in aggregate.Nodes)
            {
                var archivePath = DuckDBTierArchiveManifest.NodeArchivePath(activeArchiveBasePath, node.Table);
                var hasFiles = archiveFileProbe.HasArchiveFiles(connection, archivePath);
                await ExecuteNonQueryAsync(
                        connection,
                        DuckDBTierControl.UpsertGenerationNodeSql(
                            sql,
                            aggregate.ControlKey,
                            generationId,
                            node.Entity.Name,
                            node.Table,
                            node.Schema,
                            archivePath,
                            hasFiles),
                        cancellationToken)
                    .ConfigureAwait(false);
                await ExecuteNonQueryAsync(
                        connection,
                        DuckDBTierControl.ReplaceGenerationFilesSql(
                            sql,
                            aggregate.ControlKey,
                            generationId,
                            node.Table,
                            archivePath,
                            hasFiles),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await ExecuteNonQueryAsync(
                    connection,
                    DuckDBTierControl.PublishArchiveSql(
                        sql,
                        aggregate.ControlKey,
                        watermark,
                        aggregate.Root.ArchiveSubPath,
                        activeArchiveBasePath,
                        revision,
                        aggregate.Granularity,
                        aggregate.PartitionSpec),
                    cancellationToken)
                .ConfigureAwait(false);
            RegenerateViews(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
            if (useInternalTransaction)
            {
                await ExecuteNonQueryAsync(connection, "COMMIT;", cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            if (useInternalTransaction)
            {
                try
                {
                    await ExecuteNonQueryAsync(connection, "ROLLBACK;", CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the publication failure. A failed rollback leaves the connection unusable and the
                    // outer operation will close it before the caller retries from persisted control metadata.
                }
            }

            throw;
        }
    }

    private static string ReadActiveArchiveBasePath(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        DuckDBTierAggregate aggregate)
        => ExecuteScalar(
               connection,
               DuckDBTierControl.ReadActiveArchivePathSql(sql, aggregate.ControlKey)) as string
           ?? aggregate.ArchiveBasePath;

    private static string? ReadArchiveRevision(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        string controlKey)
        => ExecuteScalar(connection, DuckDBTierControl.ReadArchiveRevisionSql(sql, controlKey)) as string;

    private static string CreateArchiveRevision()
        => DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)
           + "-"
           + Guid.NewGuid().ToString("N");

    private static async Task<IReadOnlyList<string>> ReadGenerationFilesAsync(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        string controlKey,
        string generationId,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DuckDBTierControl.ReadGenerationFilesSql(
            sql,
            controlKey,
            generationId,
            limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var files = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            files.Add(reader.GetString(0));
        }

        return files;
    }

    private static string ArchiveScheme(string archivePath)
    {
        var match = RemoteArchiveSchemeRegex().Match(archivePath);
        return match.Success
            ? archivePath[..archivePath.IndexOf("://", StringComparison.Ordinal)].ToLowerInvariant()
            : "file";
    }

    private static IReadOnlyList<string> RequiredArchiveExtensions(string scheme)
        => scheme.ToLowerInvariant() switch
        {
            "s3" or "gs" or "gcs" or "r2" or "http" or "https" => ["httpfs"],
            "azure" or "az" or "abfs" or "abfss" => ["azure"],
            _ => [],
        };

    private static bool ReadExtensionState(
        DuckDBConnection connection,
        string extension,
        string stateColumn)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {stateColumn} FROM duckdb_extensions() "
                              + $"WHERE extension_name = '{extension.Replace("'", "''")}';";
        var result = command.ExecuteScalar();
        return result is not null and not DBNull && Convert.ToBoolean(result, CultureInfo.InvariantCulture);
    }

    private static async Task ProbeWriteAndDeleteAsync(
        DuckDBConnection connection,
        string archiveBasePath,
        ICollection<TierStorageCapabilityResult> capabilities,
        CancellationToken cancellationToken)
    {
        if (IsRemoteArchive(archiveBasePath))
        {
            capabilities.Add(new TierStorageCapabilityResult(
                TierStorageCapability.Write,
                false,
                "A remote write probe was not attempted because DuckDB exposes no storage-neutral object delete operation."));
            capabilities.Add(new TierStorageCapabilityResult(
                TierStorageCapability.Delete,
                false,
                "Remote deletion must be validated through the host application's object-store control plane."));
            return;
        }

        var directory = Path.Combine(
            archiveBasePath,
            "_preflight",
            Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "probe.parquet");
        try
        {
            Directory.CreateDirectory(directory);
            await ExecuteNonQueryAsync(
                    connection,
                    $"COPY (SELECT 1 AS provider_probe) TO '{file.Replace("'", "''")}' (FORMAT PARQUET);",
                    cancellationToken)
                .ConfigureAwait(false);
            capabilities.Add(new TierStorageCapabilityResult(
                TierStorageCapability.Write,
                File.Exists(file),
                File.Exists(file)
                    ? "A disposable local Parquet probe was written successfully."
                    : "DuckDB completed the write command but the local probe file was not visible."));

            File.Delete(file);
            Directory.Delete(directory, recursive: true);
            capabilities.Add(new TierStorageCapabilityResult(
                TierStorageCapability.Delete,
                !File.Exists(file),
                !File.Exists(file)
                    ? "The disposable local probe was deleted successfully."
                    : "The disposable local probe remains after deletion."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            capabilities.Add(new TierStorageCapabilityResult(
                TierStorageCapability.Write,
                false,
                "Disposable local write failed: " + RedactDiagnosticMessage(exception)));
            capabilities.Add(new TierStorageCapabilityResult(
                TierStorageCapability.Delete,
                false,
                "Disposable local cleanup could not be verified."));
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Preserve the original preflight result. The unique _preflight directory is deliberately
                // isolated so the application can remove it without touching archive data.
            }
        }
    }

    private static string RedactArchivePath(string archivePath)
    {
        if (!IsRemoteArchive(archivePath))
        {
            return archivePath;
        }

        if (!Uri.TryCreate(archivePath, UriKind.Absolute, out var uri))
        {
            var separator = archivePath.IndexOf("://", StringComparison.Ordinal);
            return separator < 0
                ? "[REDACTED_URI]"
                : archivePath[..(separator + 3)] + "[REDACTED]";
        }

        var authority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        return $"{uri.Scheme}://{authority}{uri.AbsolutePath}";
    }

    private static string OneLineMessage(Exception exception)
        => exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();

    internal static string RedactDiagnosticMessage(Exception exception)
        => UriTokenRegex().Replace(
            OneLineMessage(exception),
            match =>
            {
                var token = match.Value;
                return Uri.TryCreate(token, UriKind.Absolute, out var uri)
                    ? RedactArchivePath(uri.GetLeftPart(UriPartial.Path))
                    : "[REDACTED_URI]";
            });

    private static string? CombineAnd(params string?[] predicates)
    {
        var selected = predicates.Where(predicate => !string.IsNullOrWhiteSpace(predicate)).ToArray();
        return selected.Length == 0
            ? null
            : string.Join(" AND ", selected.Select(predicate => $"({predicate})"));
    }

    private static string? Negate(string? predicate)
        => string.IsNullOrWhiteSpace(predicate) ? null : $"NOT ({predicate})";

    private static IReadOnlyList<string>? ReadCataloguedNodeFiles(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        string controlKey,
        string generationId,
        string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = DuckDBTierControl.ReadGenerationFilesSql(
            sql,
            controlKey,
            generationId,
            int.MaxValue,
            table);
        using var reader = command.ExecuteReader();
        var files = new List<string>();
        while (reader.Read())
        {
            files.Add(reader.GetString(0));
        }

        return files.Count == 0 ? null : files;
    }

    private static void AddLocalUnpublishedCandidates(
        DuckDBTierAggregate aggregate,
        DateTime watermark,
        string activeGenerationId,
        IReadOnlySet<string> recordedIds,
        int representativeFiles,
        ICollection<TierArchiveGenerationInfo> generations)
    {
        if (IsRemoteArchive(aggregate.ArchiveBasePath))
        {
            return;
        }

        var revisionsPath = Path.Combine(aggregate.ArchiveBasePath, "_revisions");
        if (!Directory.Exists(revisionsPath))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(revisionsPath))
        {
            var generationId = Path.GetFileName(directory);
            if (generationId == activeGenerationId || recordedIds.Contains(generationId))
            {
                continue;
            }

            var files = Directory.EnumerateFiles(directory, "*.parquet", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .ToArray();
            generations.Add(new TierArchiveGenerationInfo(
                generationId,
                TierArchiveGenerationState.UnpublishedCandidate,
                directory,
                watermark,
                Directory.GetCreationTimeUtc(directory),
                files.LongLength,
                files.Sum(file => new FileInfo(file).Length),
                files.Take(representativeFiles).ToArray()));
        }
    }

    private static void EnsureLocalArchiveDirectory(string archivePath)
    {
        if (!IsRemoteArchive(archivePath))
        {
            Directory.CreateDirectory(archivePath);
        }
    }

    private static async Task<long> ExecuteCountAsync(
        DuckDBConnection connection,
        string commandText,
        CancellationToken cancellationToken)
        => Convert.ToInt64(
            await ExecuteScalarAsync(connection, commandText, cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

    /// <summary>
    ///     Returns <see langword="true" /> if the archive path targets a remote object store (has a URL scheme
    ///     such as <c>s3://</c>, <c>gcs://</c>, <c>r2://</c>, <c>azure://</c>, <c>http(s)://</c>) rather than a
    ///     local or mounted filesystem path.
    /// </summary>
    private static bool IsRemoteArchive(string archivePath)
        => RemoteArchiveSchemeRegex().IsMatch(archivePath);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*://")]
    private static partial Regex RemoteArchiveSchemeRegex();

    [GeneratedRegex("[A-Za-z][A-Za-z0-9+.-]*://[^\\s'\"\\)\\]]+")]
    private static partial Regex UriTokenRegex();

    private static int PurgePartitions(string archivePath, DuckDBTierAggregate aggregate, DateTime cutoff)
    {
        var root = archivePath.TrimEnd('/', '\\');
        if (!Directory.Exists(root))
        {
            return 0;
        }

        if (aggregate.RootPartitions.Count > 0)
        {
            var lifecycle = aggregate.RootPartitions.Single(partition =>
                partition.PropertyName == aggregate.Root.Entity.GetTieredStoreTimestamp()
                && partition.Transform is TierPartitionTransform.Value or TierPartitionTransform.Month or TierPartitionTransform.Day);
            var prefix = lifecycle.Name + "=";
            var directories = Directory.GetDirectories(root, prefix + "*", SearchOption.AllDirectories);
            var deletedDerived = 0;
            foreach (var directory in directories)
            {
                var encoded = Path.GetFileName(directory)[prefix.Length..];
                if (TryParsePartitionDate(Uri.UnescapeDataString(encoded), out var partition) && partition < cutoff)
                {
                    Directory.Delete(directory, recursive: true);
                    deletedDerived++;
                }
            }

            return deletedDerived;
        }

        var deleted = 0;
        foreach (var yearDir in Directory.GetDirectories(root, "year=*"))
        {
            if (!TryParsePart(yearDir, "year=", out var year))
            {
                continue;
            }

            foreach (var monthDir in Directory.GetDirectories(yearDir, "month=*"))
            {
                if (!TryParsePart(monthDir, "month=", out var month))
                {
                    continue;
                }

                if (aggregate.Granularity == Metadata.TierGranularity.Day)
                {
                    foreach (var dayDir in Directory.GetDirectories(monthDir, "day=*"))
                    {
                        if (TryParsePart(dayDir, "day=", out var day)
                            && TryCreatePartitionDate(year, month, day, out var partition)
                            && partition < cutoff)
                        {
                            Directory.Delete(dayDir, recursive: true);
                            deleted++;
                        }
                    }
                }
                else if (TryCreatePartitionDate(year, month, 1, out var partition) && partition < cutoff)
                {
                    Directory.Delete(monthDir, recursive: true);
                    deleted++;
                }
            }
        }

        return deleted;
    }

    private static bool TryParsePartitionDate(string value, out DateTime date)
        => DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out date);

    private static bool TryCreatePartitionDate(int year, int month, int day, out DateTime date)
    {
        date = default;

        if (year is < 1 or > 9999 || month is < 1 or > 12)
        {
            return false;
        }

        if (day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateTime(year, month, day);
        return true;
    }

    private static bool TryParsePart(string directory, string prefix, out int value)
    {
        var name = Path.GetFileName(directory);
        return int.TryParse(
            name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static InvalidOperationException NotConfigured(Type clrType)
        => new($"'{clrType.Name}' is not configured as a tiered-storage root. Call modelBuilder.ToTieredStore<{clrType.Name}>(...) in OnModelCreating.");

    private static (
        DbContext Context,
        ISqlGenerationHelper Sql,
        IDuckDBArchiveFileProbe ArchiveFileProbe,
        IDuckDBTierFailureInjector FailureInjector) Services(DatabaseFacade database)
        => (
            database.GetService<ICurrentDbContext>().Context,
            database.GetService<ISqlGenerationHelper>(),
            database.GetService<IDuckDBArchiveFileProbe>(),
            database.GetService<IDuckDBTierFailureInjector>());

    private static DateTime? ReadWatermark(DuckDBConnection connection, ISqlGenerationHelper sql, string controlKey)
        => ExecuteScalar(connection, DuckDBTierControl.ReadWatermarkSql(sql, controlKey)) is DateTime dt ? dt : null;

    // Open/close through the EF Core database facade (not the raw ADO connection) so registered connection
    // interceptors run — in particular any that load DuckDB's httpfs extension and configure object-store
    // (s3://, gcs://, azure://) credentials before the archive reads or writes remote Parquet.
    private static bool OpenTracked(DatabaseFacade database)
    {
        if (database.GetDbConnection().State == ConnectionState.Open)
        {
            return false;
        }

        database.OpenConnection();
        return true;
    }

    private static void CloseTracked(DatabaseFacade database, bool openedHere)
    {
        if (openedHere)
        {
            database.CloseConnection();
        }
    }

    private static async Task<bool> OpenTrackedAsync(DatabaseFacade database, CancellationToken cancellationToken)
    {
        if (database.GetDbConnection().State == ConnectionState.Open)
        {
            return false;
        }

        await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task CloseTrackedAsync(DatabaseFacade database, bool openedHere)
    {
        if (openedHere)
        {
            await database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static void ExecuteNonQuery(DuckDBConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(DuckDBConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = command.ExecuteScalar();
        return result is DBNull ? null : result;
    }

    private static async Task ExecuteNonQueryAsync(DuckDBConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ExecuteScalarAsync(DuckDBConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is DBNull ? null : result;
    }

    private static async Task<bool> TableExistsAsync(
        DuckDBConnection connection,
        string table,
        CancellationToken cancellationToken)
        => await TableExistsAsync(connection, table, schema: null, cancellationToken).ConfigureAwait(false);

    private static async Task<bool> TableExistsAsync(
        DuckDBConnection connection,
        string table,
        string? schema,
        CancellationToken cancellationToken)
        => Convert.ToInt64(
                await ExecuteScalarAsync(
                        connection,
                        "SELECT count(*) FROM duckdb_tables() WHERE database_name = current_database() "
                        + "AND schema_name = "
                        + (schema is null ? "current_schema()" : $"'{schema.Replace("'", "''")}'")
                        + " AND table_name = "
                        + $"'{table.Replace("'", "''")}';",
                        cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture)
            > 0;

    private static async Task<bool> TableColumnExistsAsync(
        DuckDBConnection connection,
        string table,
        string column,
        CancellationToken cancellationToken)
        => Convert.ToInt64(
                await ExecuteScalarAsync(
                        connection,
                        "SELECT count(*) FROM duckdb_columns() WHERE database_name = current_database() "
                        + "AND schema_name = current_schema() AND table_name = "
                        + $"'{table.Replace("'", "''")}' AND column_name = '{column.Replace("'", "''")}';",
                        cancellationToken)
                    .ConfigureAwait(false),
                CultureInfo.InvariantCulture)
            > 0;
}
