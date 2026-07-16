using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     The outcome of a <see cref="DuckDBArchiveExtensions.ArchiveTierAsync{TRoot}" /> call.
/// </summary>
/// <param name="RowsArchived">The number of aggregate roots written to the cold Parquet archive by this run.</param>
/// <param name="Watermark">The archive watermark after this run: aggregates whose root is before it live in Parquet.</param>
/// <param name="ArchivePath">The cold-archive root directory.</param>
/// <param name="NoOp"><see langword="true" /> if the cutoff was at or before the existing watermark.</param>
public readonly record struct TierArchiveResult(long RowsArchived, DateTime Watermark, string ArchivePath, bool NoOp);

/// <summary>
///     Raised when a hot row uses a stable tier match key that is already published in the cold archive but
///     carries a different representation. The provider rejects corrections and reopened aggregates rather
///     than silently hiding the hot version or deleting it during retry cleanup.
/// </summary>
public sealed class TierArchivedKeyConflictException : InvalidOperationException
{
    /// <summary>Creates a conflict exception for a tiered table.</summary>
    public TierArchivedKeyConflictException(Type entityType, string table, long conflictingRows)
        : base(
            $"Tiered-storage table '{table}' has {conflictingRows} hot row(s) whose configured match key is "
            + "already present in the published cold archive with different values. Corrections and reopened "
            + "aggregates require quarantine or an explicit archive-migration workflow; no rows were archived or deleted.")
    {
        EntityType = entityType;
        Table = table;
        ConflictingRows = conflictingRows;
    }

    /// <summary>The mapped entity type whose hot rows conflict with cold data.</summary>
    public Type EntityType { get; }

    /// <summary>The physical hot table.</summary>
    public string Table { get; }

    /// <summary>The number of conflicting hot rows detected.</summary>
    public long ConflictingRows { get; }
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
    /// <summary>
    ///     Creates the tier control table and the union view for every configured tiered table (roots and
    ///     children) that has a read model, if they do not already exist. Called automatically by
    ///     <c>EnsureCreated()</c>; call it yourself after <c>Migrate()</c>. Safe to call repeatedly.
    /// </summary>
    public static void EnsureTieredStoresCreated(this DatabaseFacade database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var (context, sql, archiveFileProbe) = Services(database);
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
                EnsureArchiveSpecCompatible(connection, sql, archiveFileProbe, aggregate);
                EnsurePartitionSpecCompatible(connection, sql, archiveFileProbe, aggregate);
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
                RegenerateViews(connection, sql, archiveFileProbe, aggregate);
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
    public static async Task<TierArchiveResult> ArchiveTierAsync<TRoot>(
        this DatabaseFacade database,
        DateTime cutoff,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        var (context, sql, archiveFileProbe) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        var aligned = DuckDBTierControl.AlignCutoff(cutoff, aggregate.Granularity);
        var archivePath = aggregate.Root.ArchiveSubPath;
        // DuckDB's immediate foreign-key checks do not allow a dependent and then its principal to be
        // deleted in the same transaction. Multi-table aggregates therefore delete leaf-to-root in separate
        // autocommit statements; the hot/cold anti-join makes every partial-cleanup state crash-safe.
        if (database.CurrentTransaction is not null && aggregate.Nodes.Count > 1)
        {
            throw new InvalidOperationException(
                "Archiving a multi-table tiered aggregate cannot run inside an existing transaction because "
                + "DuckDB checks foreign keys immediately. Run ArchiveTierAsync outside the caller transaction.");
        }

        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();

        try
        {
            await ExecuteNonQueryAsync(connection, DuckDBTierControl.ControlTableDdl(sql), cancellationToken).ConfigureAwait(false);
            EnsureArchiveSpecCompatible(connection, sql, archiveFileProbe, aggregate);
            EnsurePartitionSpecCompatible(connection, sql, archiveFileProbe, aggregate);
            await ExecuteNonQueryAsync(
                connection,
                DuckDBTierControl.UpsertPartitionLayoutSql(
                    sql, aggregate.ControlKey, archivePath, aggregate.Granularity, aggregate.PartitionSpec),
                cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(
                connection,
                DuckDBTierControl.UpsertArchiveSpecSql(
                    sql, aggregate.ControlKey, archivePath, aggregate.ArchiveSpec),
                cancellationToken).ConfigureAwait(false);

            var current = ReadWatermark(connection, sql, aggregate.ControlKey);
            if (current is { } publishedWatermark)
            {
                await ThrowIfArchivedConflictsAsync(
                    connection, sql, archiveFileProbe, aggregate, publishedWatermark, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (current is { } watermark && aligned <= watermark)
            {
                // A process may stop after publishing the watermark but before every view is regenerated. Repair
                // the read surface before retry cleanup can remove the last hot copy of any archived row.
                RegenerateViews(connection, sql, archiveFileProbe, aggregate);
                await DeleteAggregateAsync(
                    connection, sql, archiveFileProbe, aggregate, watermark, cancellationToken).ConfigureAwait(false);
                await ExecuteNonQueryAsync(connection, "CHECKPOINT;", cancellationToken).ConfigureAwait(false);
                return new TierArchiveResult(0, watermark, archivePath, NoOp: true);
            }

            var from = current ?? DateTime.MinValue;
            var rows = Convert.ToInt64(
                await ExecuteScalarAsync(connection, CountRootWindowSql(sql, aggregate, from, aligned), cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);

            if (rows > 0)
            {
                foreach (var node in aggregate.Nodes)
                {
                    var nullMatchKeys = Convert.ToInt64(
                        await ExecuteScalarAsync(
                                connection,
                                DuckDBTierControl.NullMatchKeyCountSql(
                                    sql, node.Table, node.Schema, node.KeyColumns, node.ChainToRoot,
                                    aggregate.RootTimestampColumn, from, aligned),
                                cancellationToken)
                            .ConfigureAwait(false),
                        CultureInfo.InvariantCulture);
                    if (nullMatchKeys > 0)
                    {
                        throw new InvalidOperationException(
                            $"Tiered-storage table '{node.Table}' has {nullMatchKeys} archiveable row(s) with a NULL "
                            + "configured match-key component. Match keys must be non-null for every archiveable row; "
                            + "no archive files were written by this run.");
                    }
                }

                foreach (var node in aggregate.Nodes)
                {
                    // DuckDB does not create the archive root's parent directories for a local COPY, so create
                    // them here. Remote object stores (s3://, gcs://, azure://, …) have no directories and
                    // DuckDB writes the keys directly, so this step is skipped for them.
                    if (!IsRemoteArchive(node.ArchiveSubPath))
                    {
                        Directory.CreateDirectory(node.ArchiveSubPath);
                    }

                    await ExecuteNonQueryAsync(connection, CopySql(sql, aggregate, node, from, aligned), cancellationToken).ConfigureAwait(false);
                }
            }

            await ExecuteNonQueryAsync(
                connection,
                DuckDBTierControl.UpsertWatermarkSql(
                    sql, aggregate.ControlKey, aligned, archivePath, aggregate.Granularity, aggregate.PartitionSpec),
                cancellationToken).ConfigureAwait(false);
            RegenerateViews(connection, sql, archiveFileProbe, aggregate);
            await DeleteAggregateAsync(
                connection, sql, archiveFileProbe, aggregate, aligned, cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, "CHECKPOINT;", cancellationToken).ConfigureAwait(false);

            return new TierArchiveResult(rows, aligned, archivePath, NoOp: false);
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }
    }

    /// <summary>Synchronous version of <see cref="ArchiveTierAsync{TRoot}" />.</summary>
    public static TierArchiveResult ArchiveTier<TRoot>(this DatabaseFacade database, DateTime cutoff)
        where TRoot : class
        => database.ArchiveTierAsync<TRoot>(cutoff).GetAwaiter().GetResult();

    /// <summary>
    ///     Deletes cold-archive partitions of the whole aggregate whose period is before <paramref name="olderThan" />
    ///     (aligned down to the granularity). The hot tables and watermark are untouched.
    /// </summary>
    /// <returns>The number of partition directories deleted across all aggregate tables.</returns>
    public static int PurgeArchiveOlderThan<TRoot>(this DatabaseFacade database, DateTime olderThan)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        var (context, sql, archiveFileProbe) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot)) ?? throw NotConfigured(typeof(TRoot));

        if (IsRemoteArchive(aggregate.Root.ArchiveSubPath))
        {
            throw new NotSupportedException(
                $"PurgeArchiveOlderThan is not supported for the remote archive '{aggregate.Root.ArchiveSubPath}'. "
                + "Object stores cannot delete files through DuckDB; enforce retention with a bucket lifecycle rule on "
                + "the archive prefix instead.");
        }

        var cutoff = DuckDBTierControl.AlignCutoff(olderThan, aggregate.Granularity);
        var deleted = aggregate.Nodes.Sum(node => PurgePartitions(node.ArchiveSubPath, aggregate, cutoff));

        if (deleted > 0)
        {
            // Regenerate the views so a table whose archive is now completely empty falls back to a hot-only
            // view instead of leaving a cold read_parquet() branch over a glob that matches no files (which
            // would throw on every query).
            var openedHere = OpenTracked(database);
            try
            {
                RegenerateViews((DuckDBConnection)database.GetDbConnection(), sql, archiveFileProbe, aggregate);
            }
            finally
            {
                CloseTracked(database, openedHere);
            }
        }

        return deleted;
    }

    private static string CopySql(ISqlGenerationHelper sql, DuckDBTierAggregate aggregate, DuckDBTierNode node, DateTime from, DateTime cutoff)
        => node.IsRoot
            ? DuckDBTierControl.ArchiveCopySql(
                sql, node.Table, node.Schema, node.Columns, aggregate.RootTimestampColumn, node.ArchiveSubPath,
                aggregate.Granularity, from, cutoff, aggregate.RootPartitions)
            : DuckDBTierControl.ArchiveChildCopySql(
                sql, node.Table, node.Schema, node.Columns, node.ChainToRoot, aggregate.RootTimestampColumn,
                node.ArchiveSubPath, aggregate.Granularity, from, cutoff, aggregate.RootPartitions);

    private static void RegenerateViews(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate)
    {
        var hasWatermark = ReadWatermark(connection, sql, aggregate.ControlKey) is not null;

        foreach (var node in aggregate.Nodes)
        {
            if (node.ViewName is null)
            {
                continue;
            }

            var includeCold = hasWatermark && archiveFileProbe.HasArchiveFiles(connection, node.ArchiveSubPath);
            var viewSql = node.IsRoot
                ? DuckDBTierControl.ViewSql(
                    sql, node.ViewName, node.Table, node.Schema, node.Columns, node.KeyColumns,
                    aggregate.RootTimestampColumn, aggregate.ControlKey, node.ArchiveSubPath,
                    aggregate.Granularity, includeCold, aggregate.RootPartitions)
                : DuckDBTierControl.ChildViewSql(
                    sql, node.ViewName, node.Table, node.Schema, node.Columns, node.KeyColumns, node.ChainToRoot,
                    aggregate.RootTimestampColumn, aggregate.ControlKey, node.ArchiveSubPath, aggregate.Granularity,
                    includeCold, aggregate.IncludeHotChildFilter, aggregate.RootPartitions);
            ExecuteNonQuery(connection, viewSql);
        }
    }

    private static void EnsurePartitionSpecCompatible(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate)
    {
        var recorded = ExecuteScalar(connection, DuckDBTierControl.ReadPartitionSpecSql(sql, aggregate.ControlKey)) as string;
        var recordedGranularity = ExecuteScalar(
            connection,
            DuckDBTierControl.ReadGranularitySql(sql, aggregate.ControlKey)) as string;
        if (PartitionLayoutsMatch(recorded, recordedGranularity, aggregate))
        {
            return;
        }

        if (!aggregate.Nodes.Any(node => archiveFileProbe.HasArchiveFiles(connection, node.ArchiveSubPath)))
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
        DuckDBTierAggregate aggregate)
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

        if (!HasExistingArchive(connection, archiveFileProbe, aggregate, recordedPath, recorded))
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
        string? recordedPath,
        DuckDBTierArchiveContract? recorded)
    {
        if (aggregate.Nodes.Any(node => archiveFileProbe.HasArchiveFiles(connection, node.ArchiveSubPath)))
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
        DuckDBTierAggregate aggregate,
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
            if (!archiveFileProbe.HasArchiveFiles(connection, node.ArchiveSubPath))
            {
                continue;
            }

            var deleteSql = node.IsRoot
                ? DuckDBTierControl.DeleteHotSql(
                    sql, node.Table, node.Schema, node.KeyColumns, node.ComparisonColumns, aggregate.RootTimestampColumn,
                    node.ArchiveSubPath, cutoff, aggregate.RootPartitions)
                : DuckDBTierControl.DeleteChildSql(
                    sql, node.Table, node.Schema, node.KeyColumns, node.ComparisonColumns, node.ChainToRoot,
                    aggregate.RootTimestampColumn, node.ArchiveSubPath, cutoff);
            await ExecuteNonQueryAsync(connection, deleteSql, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ThrowIfArchivedConflictsAsync(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        DateTime watermark,
        CancellationToken cancellationToken)
    {
        foreach (var node in aggregate.Nodes)
        {
            if (!archiveFileProbe.HasArchiveFiles(connection, node.ArchiveSubPath))
            {
                continue;
            }

            var commandText = node.IsRoot
                ? DuckDBTierControl.RootConflictCountSql(
                    sql, node.Table, node.Schema, node.KeyColumns, node.ComparisonColumns,
                    aggregate.RootTimestampColumn, node.ArchiveSubPath, watermark, aggregate.RootPartitions)
                : DuckDBTierControl.ChildConflictCountSql(
                    sql, node.Table, node.Schema, node.KeyColumns, node.ComparisonColumns,
                    aggregate.RootTimestampColumn, aggregate.ControlKey, node.ArchiveSubPath,
                    aggregate.Granularity, aggregate.RootPartitions);
            var conflicts = Convert.ToInt64(
                await ExecuteScalarAsync(connection, commandText, cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (conflicts > 0)
            {
                throw new TierArchivedKeyConflictException(node.Entity.ClrType, node.Table, conflicts);
            }
        }
    }

    private static string CountRootWindowSql(ISqlGenerationHelper sql, DuckDBTierAggregate aggregate, DateTime from, DateTime cutoff)
    {
        var ts = sql.DelimitIdentifier(aggregate.RootTimestampColumn);
        return $"SELECT count(*) FROM {sql.DelimitIdentifier(aggregate.Root.Table)} "
               + $"WHERE {ts} IS NOT NULL AND {ts} >= TIMESTAMP '{from.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)}' "
               + $"AND {ts} < TIMESTAMP '{cutoff.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)}';";
    }

    /// <summary>
    ///     Returns <see langword="true" /> if the archive path targets a remote object store (has a URL scheme
    ///     such as <c>s3://</c>, <c>gcs://</c>, <c>r2://</c>, <c>azure://</c>, <c>http(s)://</c>) rather than a
    ///     local or mounted filesystem path.
    /// </summary>
    private static bool IsRemoteArchive(string archivePath)
        => RemoteArchiveSchemeRegex().IsMatch(archivePath);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*://")]
    private static partial Regex RemoteArchiveSchemeRegex();

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

    private static (DbContext Context, ISqlGenerationHelper Sql, IDuckDBArchiveFileProbe ArchiveFileProbe) Services(DatabaseFacade database)
        => (database.GetService<ICurrentDbContext>().Context, database.GetService<ISqlGenerationHelper>(), database.GetService<IDuckDBArchiveFileProbe>());

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
}