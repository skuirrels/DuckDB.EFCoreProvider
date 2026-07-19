using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DuckDB.EFCoreProvider.Extensions;

public static partial class DuckDBArchiveExtensions
{
    /// <summary>
    ///     Plans a replacement active cold generation that retains rows on or after a lifecycle boundary plus exact
    ///     caller-supplied declared-partition scopes. Planning is read-only and assigns no business meaning to either.
    /// </summary>
    public static async Task<TierArchiveRetentionPlan> PlanArchiveRetentionAsync<TRoot>(
        this DatabaseFacade database,
        TierArchiveRetentionOptions options,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var (context, sql, archiveFileProbe, _) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            return (await BuildArchiveRetentionPlanAsync(
                    connection,
                    sql,
                    archiveFileProbe,
                    aggregate,
                    options,
                    cancellationToken)
                .ConfigureAwait(false)).Plan;
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }
    }

    /// <summary>Synchronous version of <c>PlanArchiveRetentionAsync</c>.</summary>
    public static TierArchiveRetentionPlan PlanArchiveRetention<TRoot>(
        this DatabaseFacade database,
        TierArchiveRetentionOptions options)
        where TRoot : class
        => database.PlanArchiveRetentionAsync<TRoot>(options).GetAwaiter().GetResult();

    /// <summary>
    ///     Publishes a planned retention-trimmed cold generation without changing hot tables or deleting any object
    ///     in the input generation. The supplied plan is rejected if its active generation or contract is stale.
    /// </summary>
    public static async Task<TierArchiveResult> PublishArchiveRetentionAsync<TRoot>(
        this DatabaseFacade database,
        TierArchiveRetentionPlan plan,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(plan);
        if (database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "PublishArchiveRetentionAsync writes an immutable Parquet generation and cannot run inside a caller transaction.");
        }

        var (context, sql, archiveFileProbe, failureInjector) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        EnsureRetentionBinding(plan, aggregate);

        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            var activeGenerationId = ReadArchiveRevision(connection, sql, aggregate.ControlKey) ?? "base";
            var activeArchiveBasePath = ReadActiveArchiveBasePath(connection, sql, aggregate);
            if (!string.Equals(plan.ExpectedOutputGenerationId, plan.InputGenerationId, StringComparison.Ordinal)
                && string.Equals(activeGenerationId, plan.ExpectedOutputGenerationId, StringComparison.Ordinal))
            {
                return BuildPublishedRetentionRetryResult(
                    connection,
                    archiveFileProbe,
                    aggregate,
                    plan,
                    activeArchiveBasePath,
                    activeGenerationId);
            }

            var options = new TierArchiveRetentionOptions
            {
                RetainFrom = plan.RequestedRetainFrom,
                RetainedPartitionScopes = plan.RetainedPartitionScopes,
                Writer = plan.Writer,
                Manifest = plan.Manifest,
            };
            var state = await BuildArchiveRetentionPlanAsync(
                    connection,
                    sql,
                    archiveFileProbe,
                    aggregate,
                    options,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(state.Plan.Fingerprint, plan.Fingerprint, StringComparison.Ordinal)
                || !string.Equals(state.Plan.InputGenerationId, plan.InputGenerationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The archive retention plan is stale because the active generation, exact file catalogue, "
                    + "configured contract, or selected row counts changed. Create and review a new plan.");
            }

            if (state.Plan.IsNoOp)
            {
                RegenerateViews(connection, sql, archiveFileProbe, aggregate, state.ActiveArchiveBasePath);
                return BuildRetentionNoOpResult(
                    connection,
                    archiveFileProbe,
                    aggregate,
                    state.Plan,
                    state.ActiveArchiveBasePath,
                    state.Plan.InputGenerationId);
            }

            var replacementBasePath = aggregate.ArchiveBasePath + "/_revisions/" + plan.ExpectedOutputGenerationId;
            var manifest = new DuckDBTierArchiveManifest(
                aggregate,
                TierArchiveOperation.RetentionTrim,
                state.Plan.Watermark,
                state.Plan.EffectiveRetainFrom,
                state.Plan.Watermark,
                replacementBasePath,
                plan.ExpectedOutputGenerationId,
                plan.Manifest);
            foreach (var node in aggregate.Nodes)
            {
                manifest.SetSelected(node, state.NodeStates[node].RetainedRows);
            }

            var stage = TierArchiveStage.Preflight;
            try
            {
                stage = TierArchiveStage.Copy;
                failureInjector.ThrowIfRequested(DuckDBTierFailurePoint.BeforeCopy, table: null);
                foreach (var node in aggregate.Nodes)
                {
                    var nodeState = state.NodeStates[node];
                    if (nodeState.RetainedRows == 0)
                    {
                        continue;
                    }

                    var nodePath = manifest.ArchivePath(node);
                    EnsureLocalArchiveDirectory(nodePath);
                    var copySql = node.IsRoot
                        ? DuckDBTierControl.ReconcileRootCopySql(
                            sql,
                            nodeState.SourceSql!,
                            node.Columns,
                            aggregate.RootTimestampColumn,
                            nodePath,
                            aggregate.Granularity,
                            aggregate.RootPartitions,
                            plan.Writer)
                        : DuckDBTierControl.ReconcileChildCopySql(
                            sql,
                            nodeState.SourceSql!,
                            nodePath,
                            aggregate.Granularity,
                            aggregate.RootPartitions,
                            plan.Writer);
                    await ExecuteNonQueryAsync(connection, copySql, cancellationToken).ConfigureAwait(false);
                }

                failureInjector.ThrowIfRequested(DuckDBTierFailurePoint.AfterCopy, table: null);
                stage = TierArchiveStage.Verify;
                failureInjector.ThrowIfRequested(DuckDBTierFailurePoint.BeforeVerify, table: null);
                var verifiedFiles = new Dictionary<DuckDBTierNode, IReadOnlyList<string>>();
                foreach (var node in aggregate.Nodes)
                {
                    var nodePath = manifest.ArchivePath(node);
                    var copied = archiveFileProbe.HasArchiveFiles(connection, nodePath)
                        ? await ExecuteCountAsync(
                                connection,
                                DuckDBTierControl.ArchiveRowCountSql(nodePath),
                                cancellationToken)
                            .ConfigureAwait(false)
                        : 0;
                    var selected = state.NodeStates[node].RetainedRows;
                    if (copied != selected)
                    {
                        throw new InvalidOperationException(
                            $"Archive retention verification failed for table '{node.Table}': selected "
                            + $"{selected} row(s), but found {copied} row(s) in replacement Parquet.");
                    }

                    var summary = copied == 0
                        ? new DuckDBArchiveFileSummary(0, 0, [], IsTruncated: false)
                        : archiveFileProbe.GetArchiveFileSummary(
                            connection,
                            nodePath,
                            new TierManifestOptions { Detail = TierManifestDetail.AllFiles });
                    EnsureExactFileSummary(node.Table, summary);
                    verifiedFiles[node] = summary.Files;
                    manifest.SetCopied(node, copied, summary);
                }

                failureInjector.ThrowIfRequested(DuckDBTierFailurePoint.AfterVerify, table: null);
                VerifyCandidateFileCatalogue(connection, archiveFileProbe, aggregate, manifest, verifiedFiles);
                stage = TierArchiveStage.Publish;
                failureInjector.ThrowIfRequested(DuckDBTierFailurePoint.BeforePublication, table: null);
                await PublishArchiveAsync(
                        connection,
                        sql,
                        archiveFileProbe,
                        aggregate,
                        replacementBasePath,
                        plan.ExpectedOutputGenerationId,
                        plan.Watermark,
                        useInternalTransaction: true,
                        cancellationToken)
                    .ConfigureAwait(false);
                failureInjector.ThrowIfRequested(DuckDBTierFailurePoint.AfterPublication, table: null);
                stage = TierArchiveStage.Checkpoint;
                await ExecuteNonQueryAsync(connection, "CHECKPOINT;", cancellationToken).ConfigureAwait(false);
                return manifest.Build(plan.Watermark, noOp: false, TierArchiveStage.Completed);
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
                    manifest.Build(plan.Watermark, noOp: false, stage),
                    exception);
            }
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }
    }

    /// <summary>Synchronous version of <c>PublishArchiveRetentionAsync</c>.</summary>
    public static TierArchiveResult PublishArchiveRetention<TRoot>(
        this DatabaseFacade database,
        TierArchiveRetentionPlan plan)
        where TRoot : class
        => database.PublishArchiveRetentionAsync<TRoot>(plan).GetAwaiter().GetResult();

    private static async Task<RetentionPlanningState> BuildArchiveRetentionPlanAsync(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        TierArchiveRetentionOptions options,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, DuckDBTierControl.ControlTable, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Tiered-storage aggregate '{aggregate.ControlKey}' has no active cold generation to trim.");
        }

        var watermark = ReadWatermark(connection, sql, aggregate.ControlKey)
            ?? throw new InvalidOperationException(
                $"Tiered-storage aggregate '{aggregate.ControlKey}' has no active cold generation to trim.");
        var activeArchiveBasePath = ReadActiveArchiveBasePath(connection, sql, aggregate);
        var inputGenerationId = ReadArchiveRevision(connection, sql, aggregate.ControlKey) ?? "base";
        EnsureArchiveSpecCompatible(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
        EnsurePartitionSpecCompatible(connection, sql, archiveFileProbe, aggregate, activeArchiveBasePath);
        await ThrowIfAmbiguousSharedBindingsAsync(connection, sql, aggregate, cancellationToken).ConfigureAwait(false);

        var effectiveRetainFrom = DuckDBTierControl.AlignCutoff(options.RetainFrom, aggregate.Granularity);
        var normalizedScopes = NormalizeRetentionScopes(options.RetainedPartitionScopes);
        var boundaries = normalizedScopes.Select(scope =>
            DuckDBTierMaintenanceBoundary.Create(sql, aggregate, scope, tombstones: [])).ToArray();
        var rootScopePredicate = CombineRetentionPredicates(
            boundaries.Select(boundary => boundary.RootScopePredicate("c")));
        var archiveScopePredicate = CombineRetentionPredicates(
            boundaries.Select(boundary => boundary.ArchivePartitionScopePredicate("c")));

        var nodeStates = new Dictionary<DuckDBTierNode, RetentionNodeState>();
        var fingerprint = new StringBuilder()
            .Append("retention-v1\n")
            .Append(aggregate.ControlKey).Append('\n')
            .Append(aggregate.BindingId).Append('\n')
            .Append(inputGenerationId).Append('\n')
            .Append(activeArchiveBasePath).Append('\n')
            .Append(watermark.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(options.RetainFrom.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(effectiveRetainFrom.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(aggregate.ArchiveSpec).Append('\n')
            .Append(aggregate.PartitionSpec).Append('\n')
            .Append(JsonSerializer.Serialize(options.Writer)).Append('\n')
            .Append(JsonSerializer.Serialize(options.Manifest)).Append('\n');
        foreach (var scope in normalizedScopes)
        {
            fingerprint.Append(CanonicalScope(scope)).Append('\n');
        }

        foreach (var node in aggregate.Nodes)
        {
            var nodePath = DuckDBTierArchiveManifest.NodeArchivePath(activeArchiveBasePath, node.Table);
            var hasFiles = archiveFileProbe.HasArchiveFiles(connection, nodePath);
            var summary = hasFiles
                ? archiveFileProbe.GetArchiveFileSummary(
                    connection,
                    nodePath,
                    new TierManifestOptions { Detail = TierManifestDetail.AllFiles })
                : new DuckDBArchiveFileSummary(0, 0, [], IsTruncated: false);
            EnsureExactFileSummary(node.Table, summary);
            EnsurePublishedFileCatalogueMatches(
                connection,
                sql,
                aggregate.ControlKey,
                inputGenerationId,
                node.Table,
                summary.Files);
            var inputRows = hasFiles
                ? await ExecuteCountAsync(
                        connection,
                        DuckDBTierControl.ArchiveRowCountSql(nodePath),
                        cancellationToken)
                    .ConfigureAwait(false)
                : 0;
            var source = !hasFiles
                ? null
                : node.IsRoot
                    ? DuckDBTierControl.RetentionRootSourceSql(
                        sql,
                        node.Columns,
                        aggregate.RootTimestampColumn,
                        nodePath,
                        aggregate.RootPartitions,
                        effectiveRetainFrom,
                        watermark,
                        rootScopePredicate)
                    : DuckDBTierControl.RetentionChildSourceSql(
                        sql,
                        nodePath,
                        aggregate.RootTimestampColumn,
                        aggregate.Granularity,
                        aggregate.RootPartitions,
                        effectiveRetainFrom,
                        watermark,
                        archiveScopePredicate);
            var retainedRows = source is null
                ? 0
                : await ExecuteCountAsync(
                        connection,
                        DuckDBTierControl.ReconcileSourceCountSql(source),
                        cancellationToken)
                    .ConfigureAwait(false);
            if (source is not null && retainedRows > 0)
            {
                var nullKeys = await ExecuteCountAsync(
                        connection,
                        DuckDBTierControl.SourceNullMatchKeyCountSql(sql, source, node.KeyColumns),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (nullKeys > 0)
                {
                    throw new InvalidOperationException(
                        $"Archive retention source for table '{node.Table}' contains {nullKeys} row(s) with a null configured match key.");
                }

                var duplicateKeys = await ExecuteCountAsync(
                        connection,
                        DuckDBTierControl.SourceDuplicateMatchKeyCountSql(sql, source, node.KeyColumns),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (duplicateKeys > 0)
                {
                    throw new InvalidOperationException(
                        $"Archive retention source for table '{node.Table}' contains {duplicateKeys} duplicate configured match-key group(s).");
                }
            }

            var excludedRows = inputRows - retainedRows;
            if (excludedRows < 0)
            {
                throw new InvalidOperationException(
                    $"Archive retention planning found more retained rows than input rows for table '{node.Table}'.");
            }

            nodeStates[node] = new RetentionNodeState(source, inputRows, retainedRows, excludedRows, summary);
            fingerprint.Append(node.BindingId).Append('|')
                .Append(node.Table).Append('|')
                .Append(inputRows.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(retainedRows.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(summary.FileCount.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(summary.TotalBytes.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var file in summary.Files.Order(StringComparer.Ordinal))
            {
                fingerprint.Append(file).Append('\n');
            }
        }

        foreach (var node in aggregate.Nodes.Where(candidate => !candidate.IsRoot))
        {
            var nodeState = nodeStates[node];
            if (nodeState.RetainedRows == 0)
            {
                continue;
            }

            var parentHop = node.ChainToRoot[0];
            var parent = aggregate.Nodes.Single(candidate =>
                candidate.Table == parentHop.PrincipalTable
                && candidate.Schema == parentHop.PrincipalSchema
                && candidate.ChainToRoot.Count == node.ChainToRoot.Count - 1);
            var parentSource = nodeStates[parent].SourceSql;
            var orphanRows = parentSource is null
                ? nodeState.RetainedRows
                : await ExecuteCountAsync(
                        connection,
                        DuckDBTierControl.SourceOrphanCountSql(
                            sql,
                            nodeState.SourceSql!,
                            parentSource,
                            parentHop),
                        cancellationToken)
                    .ConfigureAwait(false);
            if (orphanRows > 0)
            {
                throw new InvalidOperationException(
                    $"Archive retention source for table '{node.Table}' contains {orphanRows} retained row(s) "
                    + $"without a retained parent in table '{parent.Table}'.");
            }
        }

        var fingerprintValue = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));
        var noOp = nodeStates.Values.All(node => node.ExcludedRows == 0);
        var expectedOutputGenerationId = noOp
            ? inputGenerationId
            : "retention-" + fingerprintValue[..24].ToLowerInvariant();
        var nodes = aggregate.Nodes.Select(node =>
        {
            var state = nodeStates[node];
            return new TierArchiveRetentionNodePlan(
                node.Entity.ClrType,
                node.Table,
                node.Schema,
                state.InputRows,
                state.RetainedRows,
                state.ExcludedRows,
                state.InputFiles.FileCount,
                state.InputFiles.TotalBytes)
            {
                BindingId = node.BindingId,
            };
        }).ToArray();
        var plan = new TierArchiveRetentionPlan(
            aggregate.ControlKey,
            fingerprintValue,
            BindingInfo(aggregate),
            options.RetainFrom,
            effectiveRetainFrom,
            watermark,
            inputGenerationId,
            expectedOutputGenerationId,
            DuckDBTierArchiveManifest.RedactCredentials(activeArchiveBasePath),
            normalizedScopes,
            nodes,
            nodeStates.Values.Sum(node => node.InputFiles.FileCount),
            nodeStates.Values.Sum(node => node.InputFiles.TotalBytes),
            options.Writer,
            options.Manifest);
        return new RetentionPlanningState(plan, activeArchiveBasePath, nodeStates);
    }

    private static void EnsureRetentionBinding(TierArchiveRetentionPlan plan, DuckDBTierAggregate aggregate)
    {
        if (!string.Equals(plan.ControlKey, aggregate.ControlKey, StringComparison.Ordinal)
            || plan.Binding.RootEntityType != aggregate.Root.Entity.ClrType
            || !string.Equals(plan.Binding.BindingId, aggregate.BindingId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The archive retention plan does not belong to the configured tiered-storage root binding.");
        }
    }

    private static IReadOnlyList<TierMaintenanceScope> NormalizeRetentionScopes(
        IReadOnlyList<TierMaintenanceScope> scopes)
        => scopes
            .GroupBy(CanonicalScope, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(CanonicalScope, StringComparer.Ordinal)
            .ToArray();

    private static string CanonicalScope(TierMaintenanceScope scope)
        => string.Join(
            "|",
            scope.PartitionValues.Select(pair =>
                pair.Key + "=" + (pair.Value is null
                    ? "null"
                    : JsonSerializer.Serialize(pair.Value, pair.Value.GetType()))));

    private static string? CombineRetentionPredicates(IEnumerable<string?> predicates)
    {
        var selected = predicates.Where(predicate => !string.IsNullOrWhiteSpace(predicate)).ToArray();
        return selected.Length == 0
            ? null
            : string.Join(" OR ", selected.Select(predicate => $"({predicate})"));
    }

    private static void EnsureExactFileSummary(string table, DuckDBArchiveFileSummary summary)
    {
        if (summary.IsTruncated || summary.Files.Count != summary.FileCount)
        {
            throw new InvalidOperationException(
                $"Archive retention requires an exact physical file catalogue for table '{table}'.");
        }
    }

    private static void EnsurePublishedFileCatalogueMatches(
        DuckDBConnection connection,
        ISqlGenerationHelper sql,
        string controlKey,
        string generationId,
        string table,
        IReadOnlyList<string> physicalFiles)
    {
        var cataloguedFiles = ReadCataloguedNodeFiles(connection, sql, controlKey, generationId, table) ?? [];
        if (!cataloguedFiles.SequenceEqual(physicalFiles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Archive retention cannot plan from generation '{generationId}' because the provider's exact "
                + $"file catalogue does not match the physical files for table '{table}'. Reconcile the active "
                + "generation before retrying.");
        }
    }

    private static void VerifyCandidateFileCatalogue(
        DuckDBConnection connection,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        DuckDBTierArchiveManifest manifest,
        IReadOnlyDictionary<DuckDBTierNode, IReadOnlyList<string>> expectedFiles)
    {
        foreach (var node in aggregate.Nodes)
        {
            var nodePath = manifest.ArchivePath(node);
            var actual = archiveFileProbe.HasArchiveFiles(connection, nodePath)
                ? archiveFileProbe.GetArchiveFiles(connection, nodePath)
                : [];
            if (!actual.SequenceEqual(expectedFiles[node], StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Archive retention file catalogue changed before publication for table '{node.Table}'.");
            }
        }
    }

    private static TierArchiveResult BuildPublishedRetentionRetryResult(
        DuckDBConnection connection,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        TierArchiveRetentionPlan plan,
        string activeArchiveBasePath,
        string activeGenerationId)
        => BuildRetentionNoOpResult(
            connection,
            archiveFileProbe,
            aggregate,
            plan,
            activeArchiveBasePath,
            activeGenerationId);

    private static TierArchiveResult BuildRetentionNoOpResult(
        DuckDBConnection connection,
        IDuckDBArchiveFileProbe archiveFileProbe,
        DuckDBTierAggregate aggregate,
        TierArchiveRetentionPlan plan,
        string archiveBasePath,
        string generationId)
    {
        var manifest = new DuckDBTierArchiveManifest(
            aggregate,
            TierArchiveOperation.RetentionTrim,
            plan.Watermark,
            plan.EffectiveRetainFrom,
            plan.Watermark,
            archiveBasePath,
            generationId,
            plan.Manifest);
        foreach (var node in aggregate.Nodes)
        {
            var planned = plan.Nodes.Single(candidate => candidate.BindingId == node.BindingId);
            manifest.SetSelected(node, planned.RetainedRows);
            var nodePath = manifest.ArchivePath(node);
            var summary = archiveFileProbe.HasArchiveFiles(connection, nodePath)
                ? archiveFileProbe.GetArchiveFileSummary(connection, nodePath, plan.Manifest)
                : new DuckDBArchiveFileSummary(0, 0, [], IsTruncated: false);
            manifest.SetCopied(node, planned.RetainedRows, summary);
        }

        return manifest.Build(plan.Watermark, noOp: true, TierArchiveStage.Completed);
    }

    private sealed record RetentionPlanningState(
        TierArchiveRetentionPlan Plan,
        string ActiveArchiveBasePath,
        IReadOnlyDictionary<DuckDBTierNode, RetentionNodeState> NodeStates);

    private sealed record RetentionNodeState(
        string? SourceSql,
        long InputRows,
        long RetainedRows,
        long ExcludedRows,
        DuckDBArchiveFileSummary InputFiles);
}
