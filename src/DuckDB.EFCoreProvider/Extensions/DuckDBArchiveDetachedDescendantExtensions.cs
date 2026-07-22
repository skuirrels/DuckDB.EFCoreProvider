using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Extensions;

public static partial class DuckDBArchiveExtensions
{
    /// <summary>
    ///     Returns a bounded technical diagnostic of hot descendants whose configured parent chain exists only in
    ///     the active cold generation and whose stable key is not already cold. The Provider does not quarantine,
    ///     reconcile, approve, or assign business meaning to these rows.
    /// </summary>
    public static async Task<TierDetachedDescendantPage> GetArchiveDetachedDescendantsAsync<TRoot>(
        this DatabaseFacade database,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
        where TRoot : class
    {
        ArgumentNullException.ThrowIfNull(database);
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset cannot be negative.");
        }

        if (limit <= 0 || limit > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be between 1 and 10,000.");
        }

        var (context, sql, archiveFileProbe, _) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();
        try
        {
            if (!await TableExistsAsync(connection, DuckDBTierControl.ControlTable, cancellationToken)
                    .ConfigureAwait(false)
                || ReadWatermark(connection, sql, aggregate.ControlKey) is null)
            {
                return new TierDetachedDescendantPage(0, offset, limit, []);
            }

            var activeBasePath = await TableColumnExistsAsync(
                    connection,
                    DuckDBTierControl.ControlTable,
                    "active_archive_path",
                    cancellationToken)
                .ConfigureAwait(false)
                ? ReadActiveArchiveBasePath(connection, sql, aggregate)
                : aggregate.ArchiveBasePath;
            var nodes = new List<(DuckDBTierNode Node, string NodePath, bool HasColdNode, long Count)>();
            var total = 0L;
            foreach (var node in aggregate.Nodes.Where(candidate => !candidate.IsRoot))
            {
                var hasCompleteColdParentChain = node.ChainToRoot.All(hop =>
                    archiveFileProbe.HasArchiveFiles(
                        connection,
                        DuckDBTierArchiveManifest.NodeArchivePath(activeBasePath, hop.PrincipalTable)));
                if (!hasCompleteColdParentChain)
                {
                    continue;
                }

                var nodePath = DuckDBTierArchiveManifest.NodeArchivePath(activeBasePath, node.Table);
                var hasColdNode = archiveFileProbe.HasArchiveFiles(connection, nodePath);
                var count = await ExecuteCountAsync(
                        connection,
                        DuckDBTierControl.DetachedDescendantCountSql(
                            sql,
                            node.Table,
                            node.Schema,
                            node.KeyColumns,
                            node.ChainToRoot,
                            activeBasePath,
                            nodePath,
                            hasColdNode,
                            aggregate.RootPartitions),
                        cancellationToken)
                    .ConfigureAwait(false);
                nodes.Add((node, nodePath, hasColdNode, count));
                total += count;
            }

            var keys = new List<TierConflictKey>(limit);
            var remainingOffset = (long)offset;
            foreach (var entry in nodes)
            {
                if (remainingOffset >= entry.Count)
                {
                    remainingOffset -= entry.Count;
                    continue;
                }

                var take = Math.Min(limit - keys.Count, (int)Math.Min(int.MaxValue, entry.Count - remainingOffset));
                if (take <= 0)
                {
                    break;
                }

                await using var command = connection.CreateCommand();
                command.CommandText = DuckDBTierControl.DetachedDescendantKeysSql(
                    sql,
                    entry.Node.Table,
                    entry.Node.Schema,
                    entry.Node.KeyColumns,
                    entry.Node.ChainToRoot,
                    activeBasePath,
                    entry.NodePath,
                    entry.HasColdNode,
                    aggregate.RootPartitions,
                    checked((int)remainingOffset),
                    take);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var propertyNames = MatchPropertyNames(entry.Node);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                    for (var i = 0; i < propertyNames.Count; i++)
                    {
                        values[propertyNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }

                    keys.Add(new TierConflictKey(entry.Node.Entity.ClrType, entry.Node.Table, values));
                }

                remainingOffset = 0;
                if (keys.Count == limit)
                {
                    break;
                }
            }

            return new TierDetachedDescendantPage(total, offset, limit, keys);
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }
    }

    /// <summary>Streams detached-descendant diagnostic identities in bounded pages.</summary>
    public static async IAsyncEnumerable<TierDetachedDescendantPage> StreamArchiveDetachedDescendantsAsync<TRoot>(
        this DatabaseFacade database,
        int pageSize = 100,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TRoot : class
    {
        var offset = 0;
        while (true)
        {
            var page = await database.GetArchiveDetachedDescendantsAsync<TRoot>(
                    offset,
                    pageSize,
                    cancellationToken)
                .ConfigureAwait(false);
            yield return page;
            if (!page.HasMore || page.Keys.Count == 0)
            {
                yield break;
            }

            offset += page.Keys.Count;
        }
    }
}