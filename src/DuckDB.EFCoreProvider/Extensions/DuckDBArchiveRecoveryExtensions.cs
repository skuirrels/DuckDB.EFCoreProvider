using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Globalization;
using System.Text;

namespace DuckDB.EFCoreProvider.Extensions;

public static partial class DuckDBArchiveExtensions
{
    private const int RecoveryCheckpointFormatVersion = 1;

    /// <summary>
    ///     Captures persistable evidence for the currently active generation. Persist the returned checkpoint outside
    ///     the DuckDB database before relying on Provider recovery after local control or catalogue loss.
    /// </summary>
    public static Task<TierArchiveRecoveryCheckpoint> CaptureArchiveRecoveryCheckpointAsync<TRoot>(
        this DatabaseFacade database,
        CancellationToken cancellationToken = default)
        where TRoot : class
        => ExecuteTieredOperationAsync<TRoot, TierArchiveRecoveryCheckpoint>(
            database,
            "CaptureArchiveRecoveryCheckpoint",
            () => CaptureArchiveRecoveryCheckpointImplementationAsync<TRoot>(database, cancellationToken));

    private static async Task<TierArchiveRecoveryCheckpoint> CaptureArchiveRecoveryCheckpointImplementationAsync<TRoot>(
        DatabaseFacade database,
        CancellationToken cancellationToken)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        var (context, sql, archiveFileProbe, _) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            if (!await TableExistsAsync(connection, DuckDBTierControl.ControlTable, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw MissingRecoveryControlEvidence(aggregate.ControlKey);
            }

            var watermark = ReadWatermark(connection, sql, aggregate.ControlKey);
            if (watermark is null)
            {
                throw MissingRecoveryControlEvidence(aggregate.ControlKey);
            }

            var generationId = ReadArchiveRevision(connection, sql, aggregate.ControlKey) ?? "base";
            var activeArchivePath = ReadActiveArchiveBasePath(connection, sql, aggregate);
            var expectedArchivePath = ResolveRecoveryArchivePath(aggregate, generationId);
            if (!string.Equals(
                    NormalizeArchivePath(activeArchivePath),
                    NormalizeArchivePath(expectedArchivePath),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Tiered-storage aggregate '{aggregate.ControlKey}' has an active archive path that does not "
                    + "match its Provider-owned generation layout. Recovery evidence was not captured.");
            }

            var bootstrap = ReadBootstrapWindow(connection, sql, aggregate.ControlKey);
            var nodes = await CaptureRecoveryNodeEvidenceAsync(
                    connection,
                    sql,
                    archiveFileProbe,
                    aggregate,
                    activeArchivePath,
                    watermark.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            var checkpoint = new TierArchiveRecoveryCheckpoint(
                RecoveryCheckpointFormatVersion,
                aggregate.ControlKey,
                aggregate.BindingId,
                generationId,
                watermark.Value,
                bootstrap?.FromInclusive,
                bootstrap?.CutoffExclusive,
                Sha256(aggregate.ArchiveSpec),
                Sha256(aggregate.PartitionSpec),
                nodes,
                string.Empty);
            return checkpoint with { Fingerprint = RecoveryCheckpointFingerprint(checkpoint) };
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }
    }

    /// <summary>Synchronous version of <c>CaptureArchiveRecoveryCheckpointAsync</c>.</summary>
    public static TierArchiveRecoveryCheckpoint CaptureArchiveRecoveryCheckpoint<TRoot>(
        this DatabaseFacade database)
        where TRoot : class
        => database.CaptureArchiveRecoveryCheckpointAsync<TRoot>().GetAwaiter().GetResult();

    /// <summary>
    ///     Re-derives the selected generation path and validates an external checkpoint against the current model,
    ///     current control state when present, and exact Parquet evidence. Planning is read-only.
    /// </summary>
    public static Task<TierArchiveRecoveryPlan> PlanArchiveRecoveryAsync<TRoot>(
        this DatabaseFacade database,
        TierArchiveRecoveryCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
        where TRoot : class
        => ExecuteTieredOperationAsync<TRoot, TierArchiveRecoveryPlan>(
            database,
            "PlanArchiveRecovery",
            () => PlanArchiveRecoveryImplementationAsync<TRoot>(database, checkpoint, cancellationToken));

    private static async Task<TierArchiveRecoveryPlan> PlanArchiveRecoveryImplementationAsync<TRoot>(
        DatabaseFacade database,
        TierArchiveRecoveryCheckpoint checkpoint,
        CancellationToken cancellationToken)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(checkpoint);
        var (context, sql, archiveFileProbe, _) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        ValidateRecoveryCheckpoint(checkpoint, aggregate);

        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            var archivePath = ResolveRecoveryArchivePath(aggregate, checkpoint.ActiveGenerationId);
            await ValidateExistingRecoveryControlAsync(
                    connection,
                    sql,
                    aggregate,
                    checkpoint,
                    archivePath,
                    cancellationToken)
                .ConfigureAwait(false);
            await ValidateRecoveryMarkerAsync(
                    connection,
                    aggregate,
                    checkpoint.ActiveGenerationId,
                    archivePath,
                    cancellationToken)
                .ConfigureAwait(false);

            var nodes = await CaptureRecoveryNodeEvidenceAsync(
                    connection,
                    sql,
                    archiveFileProbe,
                    aggregate,
                    archivePath,
                    checkpoint.Watermark,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!checkpoint.Nodes.SequenceEqual(nodes))
            {
                throw new InvalidOperationException(
                    $"Archive recovery checkpoint for '{aggregate.ControlKey}' is stale because its exact file, "
                    + "size, row-count, or node evidence changed.");
            }

            var binding = BindingInfo(aggregate);
            return new TierArchiveRecoveryPlan(
                checkpoint,
                binding,
                DuckDBTierArchiveManifest.RedactCredentials(archivePath),
                archivePath,
                RecoveryPlanFingerprint(checkpoint, binding, archivePath, nodes));
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }
    }

    /// <summary>Synchronous version of <c>PlanArchiveRecoveryAsync</c>.</summary>
    public static TierArchiveRecoveryPlan PlanArchiveRecovery<TRoot>(
        this DatabaseFacade database,
        TierArchiveRecoveryCheckpoint checkpoint)
        where TRoot : class
        => database.PlanArchiveRecoveryAsync<TRoot>(checkpoint).GetAwaiter().GetResult();

    /// <summary>
    ///     Revalidates and atomically restores Provider control, generation, exact-file catalogue, and generated-view
    ///     state from a reviewed recovery plan. No Parquet object is created, changed, or deleted.
    /// </summary>
    public static Task<TierArchiveGenerationInventory> ApplyArchiveRecoveryAsync<TRoot>(
        this DatabaseFacade database,
        TierArchiveRecoveryPlan plan,
        CancellationToken cancellationToken = default)
        where TRoot : class
        => ExecuteTieredOperationAsync<TRoot, TierArchiveGenerationInventory>(
            database,
            "ApplyArchiveRecovery",
            () => ApplyArchiveRecoveryImplementationAsync<TRoot>(database, plan, cancellationToken));

    private static async Task<TierArchiveGenerationInventory> ApplyArchiveRecoveryImplementationAsync<TRoot>(
        DatabaseFacade database,
        TierArchiveRecoveryPlan plan,
        CancellationToken cancellationToken)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(plan);
        if (database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "ApplyArchiveRecoveryAsync atomically rebuilds Provider control and catalogue state and cannot run "
                + "inside a caller transaction.");
        }

        var refreshed = await database.PlanArchiveRecoveryAsync<TRoot>(plan.Checkpoint, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(plan.Fingerprint, refreshed.Fingerprint, StringComparison.Ordinal)
            || plan.Binding != refreshed.Binding
            || !string.Equals(plan.ArchivePath, refreshed.ArchivePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The archive recovery plan is stale because its binding, resolved generation, or exact file evidence "
                + "changed. Create and review a new plan.");
        }

        var (context, sql, archiveFileProbe, _) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            await ExecuteNonQueryAsync(connection, DuckDBTierControl.ControlTableDdl(sql), cancellationToken)
                .ConfigureAwait(false);
            BootstrapArchiveWindow? bootstrap = plan.Checkpoint.BootstrapFromInclusive is { } from
                                                && plan.Checkpoint.BootstrapToExclusive is { } to
                ? new BootstrapArchiveWindow(from, to)
                : null;
            await PublishArchiveAsync(
                    connection,
                    sql,
                    archiveFileProbe,
                    aggregate,
                    refreshed.ProviderArchivePath,
                    plan.Checkpoint.ActiveGenerationId == "base" ? null : plan.Checkpoint.ActiveGenerationId,
                    plan.Checkpoint.Watermark,
                    useInternalTransaction: true,
                    cancellationToken,
                    bootstrap)
                .ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, "CHECKPOINT;", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }

        return await database.GetArchiveGenerationInventoryAsync<TRoot>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Synchronous version of <c>ApplyArchiveRecoveryAsync</c>.</summary>
    public static TierArchiveGenerationInventory ApplyArchiveRecovery<TRoot>(
        this DatabaseFacade database,
        TierArchiveRecoveryPlan plan)
        where TRoot : class
        => database.ApplyArchiveRecoveryAsync<TRoot>(plan).GetAwaiter().GetResult();

    private static async Task<IReadOnlyList<TierArchiveRecoveryNodeCheckpoint>> CaptureRecoveryNodeEvidenceAsync(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        string archiveBasePath,
        DateTime watermark,
        CancellationToken cancellationToken)
    {
        var nodes = new List<TierArchiveRecoveryNodeCheckpoint>(aggregate.Nodes.Count);
        foreach (var node in aggregate.Nodes)
        {
            var nodePath = DuckDBTierArchiveManifest.NodeArchivePath(archiveBasePath, node.Table);
            var hasFiles = archiveFileProbe.HasArchiveFiles(connection, nodePath);
            var files = hasFiles ? archiveFileProbe.GetArchiveFiles(connection, nodePath) : [];
            var summary = hasFiles
                ? archiveFileProbe.GetArchiveFileSummary(
                    connection,
                    nodePath,
                    new TierManifestOptions { Detail = TierManifestDetail.Summary })
                : new DuckDBArchiveFileSummary(0, 0, [], IsTruncated: false);
            var rowCount = hasFiles
                ? await ExecuteCountAsync(
                        connection,
                        DuckDBTierControl.ArchiveRowCountSql(nodePath),
                        cancellationToken)
                    .ConfigureAwait(false)
                : 0;
            if (node.IsRoot && hasFiles)
            {
                var outsideWatermark = await ExecuteCountAsync(
                        connection,
                        DuckDBTierControl.ArchiveRowsOutsideWatermarkSql(
                            sql,
                            nodePath,
                            aggregate.RootTimestampColumn,
                            watermark),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (outsideWatermark > 0)
                {
                    throw new InvalidOperationException(
                        $"Archive generation for '{aggregate.ControlKey}' contains {outsideWatermark} root row(s) "
                        + "outside the checkpoint watermark and cannot be recovered safely.");
                }
            }

            nodes.Add(new TierArchiveRecoveryNodeCheckpoint(
                node.Table,
                node.Schema,
                node.BindingId,
                rowCount,
                summary.FileCount,
                summary.TotalBytes,
                FileCatalogueFingerprint(files)));
        }

        return nodes;
    }

    private static async Task ValidateExistingRecoveryControlAsync(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        DuckDBTierAggregate aggregate,
        TierArchiveRecoveryCheckpoint checkpoint,
        string archivePath,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, DuckDBTierControl.ControlTable, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        var currentWatermark = ReadWatermark(connection, sql, aggregate.ControlKey);
        if (currentWatermark is null)
        {
            return;
        }

        var currentGenerationId = ReadArchiveRevision(connection, sql, aggregate.ControlKey) ?? "base";
        var currentArchivePath = ReadActiveArchiveBasePath(connection, sql, aggregate);
        var currentBootstrap = ReadBootstrapWindow(connection, sql, aggregate.ControlKey);
        BootstrapArchiveWindow? checkpointBootstrap = checkpoint.BootstrapFromInclusive is { } checkpointFrom
                                                       && checkpoint.BootstrapToExclusive is { } checkpointTo
            ? new BootstrapArchiveWindow(checkpointFrom, checkpointTo)
            : null;
        if (currentWatermark.Value != checkpoint.Watermark
            || !string.Equals(currentGenerationId, checkpoint.ActiveGenerationId, StringComparison.Ordinal)
            || !string.Equals(
                NormalizeArchivePath(currentArchivePath),
                NormalizeArchivePath(archivePath),
                StringComparison.Ordinal)
            || currentBootstrap != checkpointBootstrap)
        {
            throw new InvalidOperationException(
                $"Tiered-storage aggregate '{aggregate.ControlKey}' already has different authoritative active "
                + "generation evidence. Recovery will not replace it.");
        }
    }

    private static async Task ValidateRecoveryMarkerAsync(
        DuckDBConnection connection,
        DuckDBTierAggregate aggregate,
        string generationId,
        string archivePath,
        CancellationToken cancellationToken)
    {
        if (!IsRemoteArchive(archivePath) || generationId == "base")
        {
            return;
        }

        var marker = await TryReadCandidateMarkerAsync(
                connection,
                CandidateMarkerPath(archivePath),
                cancellationToken)
            .ConfigureAwait(false);
        if (marker is null)
        {
            return;
        }

        var expected = CreateCandidateMarker(aggregate, generationId, marker.Operation, marker.StartedAtUtc);
        if (!CandidateMarkersMatch(marker, expected, includeStartedAt: true))
        {
            throw new InvalidOperationException(
                $"Archive recovery generation '{generationId}' has a marker for a different Provider binding or "
                + "contract and cannot be adopted.");
        }
    }

    private static void ValidateRecoveryCheckpoint(
        TierArchiveRecoveryCheckpoint checkpoint,
        DuckDBTierAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(checkpoint.Nodes);
        if (checkpoint.FormatVersion != RecoveryCheckpointFormatVersion)
        {
            throw new ArgumentException(
                $"Archive recovery checkpoint format {checkpoint.FormatVersion} is not supported.",
                nameof(checkpoint));
        }

        if (!string.Equals(checkpoint.ControlKey, aggregate.ControlKey, StringComparison.Ordinal)
            || !string.Equals(checkpoint.BindingId, aggregate.BindingId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The archive recovery checkpoint belongs to a different root-scoped Provider binding.",
                nameof(checkpoint));
        }

        _ = ResolveRecoveryArchivePath(aggregate, checkpoint.ActiveGenerationId);
        if (checkpoint.Watermark != DuckDBTierControl.AlignCutoff(checkpoint.Watermark, aggregate.Granularity))
        {
            throw new ArgumentException(
                "The archive recovery watermark is not aligned to the configured granularity.",
                nameof(checkpoint));
        }

        if ((checkpoint.BootstrapFromInclusive is null) != (checkpoint.BootstrapToExclusive is null)
            || checkpoint.BootstrapFromInclusive is { } from
            && checkpoint.BootstrapToExclusive is { } to
            && (from >= to
                || to > checkpoint.Watermark
                || from != DuckDBTierControl.AlignCutoff(from, aggregate.Granularity)
                || to != DuckDBTierControl.AlignCutoff(to, aggregate.Granularity)))
        {
            throw new ArgumentException(
                "The archive recovery checkpoint contains an incomplete or invalid bootstrap window.",
                nameof(checkpoint));
        }

        if (!string.Equals(checkpoint.ArchiveContractFingerprint, Sha256(aggregate.ArchiveSpec), StringComparison.Ordinal)
            || !string.Equals(
                checkpoint.PartitionContractFingerprint,
                Sha256(aggregate.PartitionSpec),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Archive recovery checkpoint for '{aggregate.ControlKey}' does not match the current archive or "
                + "partition contract.");
        }

        var expectedNodeIdentities = aggregate.Nodes
            .Select(node => (node.Table, node.Schema, node.BindingId))
            .ToArray();
        var checkpointNodeIdentities = checkpoint.Nodes
            .Select(node => (node.Table, node.Schema, node.BindingId))
            .ToArray();
        if (!expectedNodeIdentities.SequenceEqual(checkpointNodeIdentities))
        {
            throw new InvalidOperationException(
                $"Archive recovery checkpoint for '{aggregate.ControlKey}' does not match the configured aggregate graph.");
        }

        if (!string.Equals(checkpoint.Fingerprint, RecoveryCheckpointFingerprint(checkpoint), StringComparison.Ordinal))
        {
            throw new ArgumentException("The archive recovery checkpoint fingerprint is invalid.", nameof(checkpoint));
        }
    }

    private static string ResolveRecoveryArchivePath(DuckDBTierAggregate aggregate, string generationId)
    {
        if (string.IsNullOrWhiteSpace(generationId)
            || generationId.Length > 128
            || generationId is "." or ".."
            || generationId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new ArgumentException(
                "The active archive generation identifier is not a valid Provider generation identifier.",
                nameof(generationId));
        }

        return generationId == "base"
            ? aggregate.ArchiveBasePath
            : aggregate.ArchiveBasePath + "/_revisions/" + generationId;
    }

    private static string RecoveryCheckpointFingerprint(TierArchiveRecoveryCheckpoint checkpoint)
    {
        var builder = new StringBuilder()
            .Append("tier-recovery-checkpoint-v1\n")
            .Append(checkpoint.FormatVersion.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(checkpoint.ControlKey).Append('\n')
            .Append(checkpoint.BindingId).Append('\n')
            .Append(checkpoint.ActiveGenerationId).Append('\n')
            .Append(checkpoint.Watermark.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(checkpoint.BootstrapFromInclusive?.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(checkpoint.BootstrapToExclusive?.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(checkpoint.ArchiveContractFingerprint).Append('\n')
            .Append(checkpoint.PartitionContractFingerprint).Append('\n');
        foreach (var node in checkpoint.Nodes)
        {
            builder.Append(node.Table).Append('|')
                .Append(node.Schema).Append('|')
                .Append(node.BindingId).Append('|')
                .Append(node.RowCount.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(node.FileCount.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(node.TotalBytes.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(node.FileCatalogueFingerprint).Append('\n');
        }

        return Sha256(builder.ToString());
    }

    private static string RecoveryPlanFingerprint(
        TierArchiveRecoveryCheckpoint checkpoint,
        TieredStorageBindingInfo binding,
        string archivePath,
        IReadOnlyList<TierArchiveRecoveryNodeCheckpoint> nodes)
        => Sha256(
            "tier-recovery-plan-v1\n"
            + checkpoint.Fingerprint + "\n"
            + binding.BindingId + "\n"
            + NormalizeArchivePath(archivePath) + "\n"
            + string.Join(
                "\n",
                nodes.Select(node =>
                    node.BindingId + "|"
                    + node.RowCount.ToString(CultureInfo.InvariantCulture) + "|"
                    + node.FileCount.ToString(CultureInfo.InvariantCulture) + "|"
                    + node.TotalBytes.ToString(CultureInfo.InvariantCulture) + "|"
                    + node.FileCatalogueFingerprint)));

    private static string FileCatalogueFingerprint(IEnumerable<string> files)
        => Sha256(string.Join("\n", files.Order(StringComparer.Ordinal)));

    private static InvalidOperationException MissingRecoveryControlEvidence(string controlKey)
        => new(
            $"Tiered-storage aggregate '{controlKey}' has no authoritative active-generation control evidence. "
            + "Capture checkpoints before control loss or restore a previously exported checkpoint.");
}