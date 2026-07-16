using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DuckDB.EFCoreProvider.Extensions;

public static partial class DuckDBArchiveExtensions
{
    /// <summary>Compares the persisted archive contract with the aggregate's current EF Core metadata.</summary>
    public static async Task<TierArchiveContractInspection> InspectArchiveContractAsync<TRoot>(
        this DatabaseFacade database,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        var (context, sql, _, _) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            var hasPersistedContract = await TableExistsAsync(
                    connection,
                    DuckDBTierControl.ControlTable,
                    cancellationToken)
                .ConfigureAwait(false)
                && await TableColumnExistsAsync(
                        connection,
                        DuckDBTierControl.ControlTable,
                        "archive_spec",
                        cancellationToken)
                    .ConfigureAwait(false);
            var persistedJson = hasPersistedContract
                ? await ExecuteScalarAsync(
                        connection,
                        DuckDBTierControl.ReadArchiveSpecSql(sql, aggregate.ControlKey),
                        cancellationToken)
                    .ConfigureAwait(false) as string
                : null;
            return InspectArchiveContract(aggregate, persistedJson);
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Produces an immutable, fingerprinted rewrite plan after validating every required caller mapping.
    /// </summary>
    public static async Task<TierArchiveRewritePlan> PlanArchiveContractRewriteAsync<TRoot>(
        this DatabaseFacade database,
        TierArchiveRewriteOptions options,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var inspection = await database.InspectArchiveContractAsync<TRoot>(cancellationToken).ConfigureAwait(false);
        var (context, _, _, _) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        ValidateRewriteMappings(aggregate, inspection, options.Columns);
        return new TierArchiveRewritePlan(
            aggregate.ControlKey,
            RewriteFingerprint(inspection, options.Columns, options.Writer),
            inspection,
            options.Columns.ToArray(),
            options.Writer);
    }

    /// <summary>
    ///     Executes a previously reviewed rewrite plan into a verified immutable generation and atomically
    ///     switches generated views to it.
    /// </summary>
    public static async Task<TierArchiveResult> RewriteArchiveContractAsync<TRoot>(
        this DatabaseFacade database,
        TierArchiveRewritePlan plan,
        TierManifestOptions? manifestOptions = null,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        manifestOptions ??= TierManifestOptions.Default;
        manifestOptions.Validate();
        if (database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "RewriteArchiveContractAsync writes and publishes an external Parquet generation and cannot run "
                + "inside a caller transaction.");
        }

        var refreshed = await database.PlanArchiveContractRewriteAsync<TRoot>(
                new TierArchiveRewriteOptions
                {
                    Columns = plan.ColumnRewrites,
                    Writer = plan.Writer,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(plan.ControlKey, refreshed.ControlKey, StringComparison.Ordinal)
            || !string.Equals(plan.Fingerprint, refreshed.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The archive contract or rewrite mappings changed after the plan was produced. Generate and review "
                + "a new rewrite plan before execution.");
        }

        var (context, sql, archiveFileProbe, failureInjector) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            await ExecuteNonQueryAsync(connection, DuckDBTierControl.ControlTableDdl(sql), cancellationToken)
                .ConfigureAwait(false);
            var watermark = ReadWatermark(connection, sql, aggregate.ControlKey);
            var activeBasePath = ReadActiveArchiveBasePath(connection, sql, aggregate);
            var activeRevision = ReadArchiveRevision(connection, sql, aggregate.ControlKey);
            if (watermark is null || plan.Inspection.Differences.Count == 0)
            {
                var noOp = new DuckDBTierArchiveManifest(
                    aggregate,
                    TierArchiveOperation.RewriteContract,
                    watermark,
                    DateTime.MinValue,
                    watermark ?? DateTime.MinValue,
                    activeBasePath,
                    activeRevision,
                    manifestOptions);
                CaptureArchiveFiles(connection, archiveFileProbe, aggregate, noOp);
                return noOp.Build(watermark ?? DateTime.MinValue, noOp: true, TierArchiveStage.Completed);
            }

            var persisted = JsonSerializer.Deserialize<DuckDBTierArchiveContract>(
                plan.Inspection.PersistedContractJson
                ?? throw new InvalidOperationException("No persisted archive contract is available to rewrite."))
                ?? throw new InvalidOperationException("The persisted archive contract is unreadable.");
            var revision = CreateArchiveRevision();
            var replacementBasePath = aggregate.ArchiveBasePath + "/_revisions/" + revision;
            var manifest = new DuckDBTierArchiveManifest(
                aggregate,
                TierArchiveOperation.RewriteContract,
                watermark,
                DateTime.MinValue,
                watermark.Value,
                replacementBasePath,
                revision,
                manifestOptions);
            var stage = TierArchiveStage.Preflight;
            try
            {
                await ThrowIfAmbiguousSharedBindingsAsync(
                        connection,
                        sql,
                        aggregate,
                        cancellationToken)
                    .ConfigureAwait(false);

                var sources = new Dictionary<DuckDBTierNode, string>();
                foreach (var node in aggregate.Nodes)
                {
                    var activeNodePath = DuckDBTierArchiveManifest.NodeArchivePath(activeBasePath, node.Table);
                    if (!archiveFileProbe.HasArchiveFiles(connection, activeNodePath))
                    {
                        manifest.SetSelected(node, 0);
                        continue;
                    }

                    var oldNode = persisted.Nodes.Single(contractNode =>
                        contractNode.Table == node.Table && contractNode.Schema == node.Schema);
                    var source = BuildContractRewriteSourceSql(
                        sql,
                        node,
                        oldNode,
                        activeNodePath,
                        aggregate.Granularity,
                        aggregate.RootPartitions,
                        plan.ColumnRewrites);
                    sources[node] = source;
                    manifest.SetSelected(
                        node,
                        await ExecuteCountAsync(
                                connection,
                                DuckDBTierControl.ReconcileSourceCountSql(source),
                                cancellationToken)
                            .ConfigureAwait(false));
                }

                stage = TierArchiveStage.Copy;
                foreach (var node in aggregate.Nodes)
                {
                    if (!sources.TryGetValue(node, out var source) || manifest.SelectedRows(node) == 0)
                    {
                        continue;
                    }

                    var nodePath = manifest.ArchivePath(node);
                    EnsureLocalArchiveDirectory(nodePath);
                    var copySql = node.IsRoot
                        ? DuckDBTierControl.ReconcileRootCopySql(
                            sql,
                            source,
                            node.Columns,
                            aggregate.RootTimestampColumn,
                            nodePath,
                            aggregate.Granularity,
                            aggregate.RootPartitions,
                            plan.Writer)
                        : DuckDBTierControl.ReconcileChildCopySql(
                            sql,
                            source,
                            nodePath,
                            aggregate.Granularity,
                            aggregate.RootPartitions,
                            plan.Writer);
                    await ExecuteNonQueryAsync(connection, copySql, cancellationToken).ConfigureAwait(false);
                }

                stage = TierArchiveStage.Verify;
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
                    if (copied != manifest.SelectedRows(node))
                    {
                        throw new InvalidOperationException(
                            $"Archive-contract rewrite verification failed for table '{node.Table}': selected "
                            + $"{manifest.SelectedRows(node)} row(s), but found {copied} row(s).");
                    }

                    manifest.SetCopied(
                        node,
                        copied,
                        copied == 0
                            ? new DuckDBArchiveFileSummary(0, 0, [], IsTruncated: false)
                            : archiveFileProbe.GetArchiveFileSummary(connection, nodePath, manifest.ManifestOptions));
                }

                failureInjector.ThrowIfRequested(DuckDBTierFailurePoint.AfterCopy, table: null);
                stage = TierArchiveStage.Publish;
                await PublishArchiveAsync(
                        connection,
                        sql,
                        archiveFileProbe,
                        aggregate,
                        replacementBasePath,
                        revision,
                        watermark.Value,
                        useInternalTransaction: true,
                        cancellationToken)
                    .ConfigureAwait(false);
                await ExecuteNonQueryAsync(connection, "CHECKPOINT;", cancellationToken).ConfigureAwait(false);
                return manifest.Build(watermark.Value, noOp: false, TierArchiveStage.Completed);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TierAmbiguousBindingException)
            {
                throw;
            }
            catch (TierArchiveOperationException)
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

    private static TierArchiveContractInspection InspectArchiveContract(
        DuckDBTierAggregate target,
        string? persistedJson)
    {
        if (persistedJson is null)
        {
            return new TierArchiveContractInspection(
                target.ControlKey,
                null,
                target.ArchiveSpec,
                IsCompatible: true,
                []);
        }

        DuckDBTierArchiveContract persisted;
        try
        {
            persisted = JsonSerializer.Deserialize<DuckDBTierArchiveContract>(persistedJson)
                ?? throw new JsonException("Archive contract is null.");
        }
        catch (JsonException exception)
        {
            return new TierArchiveContractInspection(
                target.ControlKey,
                persistedJson,
                target.ArchiveSpec,
                IsCompatible: false,
                [
                    new TierArchiveContractDifference(
                        TierArchiveContractDifferenceKind.PhysicalLayoutChanged,
                        target.ControlKey,
                        null,
                        "Persisted archive metadata is unreadable: " + exception.Message),
                ]);
        }

        var differences = new List<TierArchiveContractDifference>();
        if (persisted.ControlKey != target.ControlKey
            || persisted.ArchivePath != target.Root.ArchiveSubPath
            || persisted.Granularity != target.Granularity
            || persisted.LifecycleColumn != target.RootTimestampColumn
            || persisted.PartitionSpec != target.PartitionSpec)
        {
            differences.Add(new TierArchiveContractDifference(
                TierArchiveContractDifferenceKind.PhysicalLayoutChanged,
                target.ControlKey,
                null,
                "Control key, archive path, lifecycle column, granularity, or partition layout changed."));
        }

        var targetNodes = target.Nodes.ToDictionary(node => (node.Table, node.Schema));
        var persistedNodes = persisted.Nodes.ToDictionary(node => (node.Table, node.Schema));
        foreach (var oldNode in persistedNodes)
        {
            if (!targetNodes.ContainsKey(oldNode.Key))
            {
                differences.Add(new TierArchiveContractDifference(
                    TierArchiveContractDifferenceKind.AggregateLayoutChanged,
                    oldNode.Value.Table,
                    null,
                    "A persisted aggregate table is absent from the target model."));
            }
        }

        foreach (var targetNode in targetNodes)
        {
            if (!persistedNodes.TryGetValue(targetNode.Key, out var oldNode))
            {
                differences.Add(new TierArchiveContractDifference(
                    TierArchiveContractDifferenceKind.AggregateLayoutChanged,
                    targetNode.Value.Table,
                    null,
                    "The target model adds an aggregate table with no persisted source."));
                continue;
            }

            if (!oldNode.MatchKeyColumns.SequenceEqual(targetNode.Value.KeyColumns, StringComparer.Ordinal))
            {
                differences.Add(new TierArchiveContractDifference(
                    TierArchiveContractDifferenceKind.MatchKeyChanged,
                    targetNode.Value.Table,
                    null,
                    "The configured match-key column layout changed."));
            }

            var oldColumns = oldNode.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
            var newColumns = targetNode.Value.ColumnDefinitions.ToDictionary(column => column.Name, StringComparer.Ordinal);
            foreach (var oldColumn in oldColumns.Values.Where(column => !newColumns.ContainsKey(column.Name)))
            {
                differences.Add(new TierArchiveContractDifference(
                    TierArchiveContractDifferenceKind.ColumnRemoved,
                    targetNode.Value.Table,
                    oldColumn.Name,
                    $"Archived column '{oldColumn.Name}' is absent from the target model."));
            }

            foreach (var newColumn in newColumns.Values)
            {
                if (!oldColumns.TryGetValue(newColumn.Name, out var oldColumn))
                {
                    differences.Add(new TierArchiveContractDifference(
                        TierArchiveContractDifferenceKind.ColumnAdded,
                        targetNode.Value.Table,
                        newColumn.Name,
                        $"Target column '{newColumn.Name}' is new and is "
                        + (newColumn.IsNullable ? "nullable." : "required.")));
                    continue;
                }

                if (!string.Equals(oldColumn.StoreType, newColumn.StoreType, StringComparison.OrdinalIgnoreCase))
                {
                    differences.Add(new TierArchiveContractDifference(
                        TierArchiveContractDifferenceKind.ColumnTypeChanged,
                        targetNode.Value.Table,
                        newColumn.Name,
                        $"Store type changed from '{oldColumn.StoreType}' to '{newColumn.StoreType}'."));
                }

                if (oldColumn.IsNullable && !newColumn.IsNullable)
                {
                    differences.Add(new TierArchiveContractDifference(
                        TierArchiveContractDifferenceKind.NullabilityTightened,
                        targetNode.Value.Table,
                        newColumn.Name,
                        "A nullable archived column became required."));
                }
            }
        }

        var compatible = differences.All(difference =>
            difference.Kind == TierArchiveContractDifferenceKind.ColumnAdded
            && target.Nodes
                .Single(node => node.Table == difference.Node)
                .ColumnDefinitions
                .Single(column => column.Name == difference.Column)
                .IsNullable);
        return new TierArchiveContractInspection(
            target.ControlKey,
            persistedJson,
            target.ArchiveSpec,
            compatible,
            differences);
    }

    private static void ValidateRewriteMappings(
        DuckDBTierAggregate aggregate,
        TierArchiveContractInspection inspection,
        IReadOnlyList<TierArchiveColumnRewrite> rewrites)
    {
        if (inspection.PersistedContractJson is null)
        {
            if (inspection.Differences.Count > 0)
            {
                throw new InvalidOperationException("Persisted archive metadata is unavailable or unreadable.");
            }

            return;
        }

        if (inspection.Differences.Any(difference =>
                difference.Kind is TierArchiveContractDifferenceKind.AggregateLayoutChanged
                    or TierArchiveContractDifferenceKind.MatchKeyChanged
                    or TierArchiveContractDifferenceKind.PhysicalLayoutChanged))
        {
            throw new InvalidOperationException(
                "Aggregate-layout, match-key, control-key, archive-path, lifecycle, granularity, and partition "
                + "changes are ambiguous and cannot be inferred by the provider.");
        }

        var duplicate = rewrites.GroupBy(
                rewrite => (rewrite.EntityType, rewrite.TargetProperty),
                EqualityComparer<(Type, string)>.Default)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Target property '{duplicate.Key.Item1.Name}.{duplicate.Key.Item2}' is mapped more than once.",
                nameof(rewrites));
        }

        foreach (var rewrite in rewrites)
        {
            var node = aggregate.Nodes.SingleOrDefault(node => node.Entity.ClrType == rewrite.EntityType)
                ?? throw new ArgumentException(
                    $"Entity type '{rewrite.EntityType.Name}' is not part of aggregate '{aggregate.ControlKey}'.",
                    nameof(rewrites));
            if (node.Entity.FindProperty(rewrite.TargetProperty) is null)
            {
                throw new ArgumentException(
                    $"Target property '{rewrite.EntityType.Name}.{rewrite.TargetProperty}' is not mapped.",
                    nameof(rewrites));
            }

            if (rewrite.SourceColumn is null && rewrite.ConstantValue is null)
            {
                throw new ArgumentException(
                    $"Rewrite '{rewrite.EntityType.Name}.{rewrite.TargetProperty}' must specify SourceColumn or a non-null ConstantValue.",
                    nameof(rewrites));
            }
        }

        foreach (var difference in inspection.Differences.Where(difference =>
                     difference.Kind is TierArchiveContractDifferenceKind.ColumnTypeChanged
                         or TierArchiveContractDifferenceKind.NullabilityTightened
                         or TierArchiveContractDifferenceKind.ColumnAdded))
        {
            var node = aggregate.Nodes.Single(node => node.Table == difference.Node);
            var column = node.ColumnDefinitions.Single(column => column.Name == difference.Column);
            var hasMapping = rewrites.Any(rewrite =>
                rewrite.EntityType == node.Entity.ClrType
                && rewrite.TargetProperty == column.PropertyName);
            if (!hasMapping
                && (difference.Kind != TierArchiveContractDifferenceKind.ColumnAdded || !column.IsNullable))
            {
                throw new InvalidOperationException(
                    $"Archive rewrite requires an explicit mapping for '{node.Entity.ClrType.Name}.{column.PropertyName}'.");
            }
        }
    }

    private static string RewriteFingerprint(
        TierArchiveContractInspection inspection,
        IReadOnlyList<TierArchiveColumnRewrite> rewrites,
        TierParquetWriterOptions writer)
    {
        var input = new StringBuilder()
            .AppendLine(inspection.ControlKey)
            .AppendLine(inspection.PersistedContractJson)
            .AppendLine(inspection.TargetContractJson)
            .AppendLine(JsonSerializer.Serialize(writer));
        foreach (var rewrite in rewrites.OrderBy(
                     rewrite => rewrite.EntityType.AssemblyQualifiedName + "|" + rewrite.TargetProperty,
                     StringComparer.Ordinal))
        {
            input.Append(rewrite.EntityType.AssemblyQualifiedName).Append('|')
                .Append(rewrite.TargetProperty).Append('|')
                .Append(rewrite.SourceColumn).Append('|')
                .Append(rewrite.ConstantValue?.GetType().AssemblyQualifiedName).Append('|')
                .Append(JsonSerializer.Serialize(rewrite.ConstantValue))
                .AppendLine();
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())));
    }

    private static string BuildContractRewriteSourceSql(
        ISqlGenerationHelper sql,
        DuckDBTierNode node,
        DuckDBTierArchiveNodeContract oldNode,
        string archivePath,
        TierGranularity granularity,
        IReadOnlyList<DuckDBTierPartitionColumn> rootPartitions,
        IReadOnlyList<TierArchiveColumnRewrite> rewrites)
    {
        const string alias = "c";
        var oldColumns = oldNode.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
        var projections = new List<string>(node.ColumnDefinitions.Count + rootPartitions.Count);
        foreach (var column in node.ColumnDefinitions)
        {
            var rewrite = rewrites.SingleOrDefault(candidate =>
                candidate.EntityType == node.Entity.ClrType
                && candidate.TargetProperty == column.PropertyName);
            if (rewrite is not null)
            {
                if (rewrite.SourceColumn is { } sourceColumn)
                {
                    if (!oldColumns.ContainsKey(sourceColumn))
                    {
                        throw new InvalidOperationException(
                            $"Source column '{sourceColumn}' does not exist in persisted table '{oldNode.Table}'.");
                    }

                    projections.Add(
                        $"CAST({alias}.{sql.DelimitIdentifier(sourceColumn)} AS {column.StoreType}) "
                        + $"AS {sql.DelimitIdentifier(column.Name)}");
                }
                else
                {
                    var property = node.Entity.FindProperty(column.PropertyName)!;
                    var literal = property.GetRelationalTypeMapping().GenerateSqlLiteral(rewrite.ConstantValue);
                    projections.Add(
                        $"CAST({literal} AS {column.StoreType}) AS {sql.DelimitIdentifier(column.Name)}");
                }

                continue;
            }

            projections.Add(oldColumns.ContainsKey(column.Name)
                ? $"{alias}.{sql.DelimitIdentifier(column.Name)}"
                : $"CAST(NULL AS {column.StoreType}) AS {sql.DelimitIdentifier(column.Name)}");
        }

        if (!node.IsRoot)
        {
            projections.AddRange(rootPartitions.Select(partition =>
                $"{alias}.{sql.DelimitIdentifier(partition.Name)}"));
            if (rootPartitions.Count == 0)
            {
                projections.AddRange(
                    (granularity == TierGranularity.Day
                        ? new[] { "year", "month", "day" }
                        : ["year", "month"])
                    .Select(column => $"{alias}.{sql.DelimitIdentifier(column)}"));
            }
        }

        return $"SELECT {string.Join(", ", projections)} FROM read_parquet("
               + $"'{DuckDBTierControl.ReadGlob(archivePath).Replace("'", "''")}', "
               + $"hive_partitioning = true, union_by_name = true) AS {alias}";
    }
}
