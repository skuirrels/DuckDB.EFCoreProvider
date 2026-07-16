using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Extensions;

public static partial class DuckDBArchiveExtensions
{
    /// <summary>Returns a bounded page of configured hot/cold match keys that require reconciliation.</summary>
    public static async Task<TierConflictPage> GetArchiveConflictsAsync<TRoot>(
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
                    .ConfigureAwait(false))
            {
                return new TierConflictPage(0, offset, limit, []);
            }

            var watermark = ReadWatermark(connection, sql, aggregate.ControlKey);
            if (watermark is null)
            {
                return new TierConflictPage(0, offset, limit, []);
            }

            var activeBasePath = await TableColumnExistsAsync(
                    connection,
                    DuckDBTierControl.ControlTable,
                    "active_archive_path",
                    cancellationToken)
                .ConfigureAwait(false)
                ? ReadActiveArchiveBasePath(connection, sql, aggregate)
                : aggregate.ArchiveBasePath;
            var nodes = new List<(DuckDBTierNode Node, string Path, IReadOnlyList<string> Columns, long Count)>();
            var total = 0L;
            foreach (var node in aggregate.Nodes)
            {
                var path = DuckDBTierArchiveManifest.NodeArchivePath(activeBasePath, node.Table);
                if (!archiveFileProbe.HasArchiveFiles(connection, path))
                {
                    continue;
                }

                var archiveColumns = archiveFileProbe.GetArchiveColumns(connection, path);
                var countSql = node.IsRoot
                    ? DuckDBTierControl.RootConflictCountSql(
                        sql,
                        node.Table,
                        node.Schema,
                        node.KeyColumns,
                        node.ComparisonColumns,
                        aggregate.RootTimestampColumn,
                        path,
                        watermark.Value,
                        aggregate.RootPartitions,
                        archiveColumns)
                    : DuckDBTierControl.ChildConflictCountSql(
                        sql,
                        node.Table,
                        node.Schema,
                        node.KeyColumns,
                        node.ComparisonColumns,
                        aggregate.RootTimestampColumn,
                        aggregate.ControlKey,
                        path,
                        aggregate.Granularity,
                        aggregate.RootPartitions,
                        archiveColumns);
                var count = await ExecuteCountAsync(connection, countSql, cancellationToken).ConfigureAwait(false);
                nodes.Add((node, path, archiveColumns, count));
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

                var query = entry.Node.IsRoot
                    ? DuckDBTierControl.RootConflictKeysSql(
                        sql,
                        entry.Node.Table,
                        entry.Node.Schema,
                        entry.Node.KeyColumns,
                        entry.Node.ComparisonColumns,
                        aggregate.RootTimestampColumn,
                        entry.Path,
                        watermark.Value,
                        aggregate.RootPartitions,
                        checked((int)remainingOffset),
                        take,
                        entry.Columns)
                    : DuckDBTierControl.ChildConflictKeysSql(
                        sql,
                        entry.Node.Table,
                        entry.Node.Schema,
                        entry.Node.KeyColumns,
                        entry.Node.ComparisonColumns,
                        aggregate.RootTimestampColumn,
                        aggregate.ControlKey,
                        entry.Path,
                        aggregate.Granularity,
                        aggregate.RootPartitions,
                        checked((int)remainingOffset),
                        take,
                        entry.Columns);
                await using var command = connection.CreateCommand();
                command.CommandText = query;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var propertyNames = MatchPropertyNames(entry.Node);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                    for (var i = 0; i < propertyNames.Count; i++)
                    {
                        values[propertyNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }

                    keys.Add(new TierConflictKey(
                        entry.Node.Entity.ClrType,
                        entry.Node.Table,
                        values));
                }

                remainingOffset = 0;
                if (keys.Count == limit)
                {
                    break;
                }
            }

            return new TierConflictPage(total, offset, limit, keys);
        }
        finally
        {
            await CloseTrackedAsync(database, openedHere).ConfigureAwait(false);
        }
    }

    /// <summary>Streams conflict identities in bounded pages without materialising the full conflict set.</summary>
    public static async IAsyncEnumerable<TierConflictPage> StreamArchiveConflictsAsync<TRoot>(
        this DatabaseFacade database,
        int pageSize = 100,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TRoot : class
    {
        var offset = 0;
        while (true)
        {
            var page = await database.GetArchiveConflictsAsync<TRoot>(
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

    private static IReadOnlyList<string> MatchPropertyNames(DuckDBTierNode node)
    {
        var configured = node.Entity.GetTieredStoreMatchProperties();
        return configured.Count == 0
            ? node.Entity.FindPrimaryKey()!.Properties.Select(property => property.Name).ToArray()
            : configured;
    }
}
