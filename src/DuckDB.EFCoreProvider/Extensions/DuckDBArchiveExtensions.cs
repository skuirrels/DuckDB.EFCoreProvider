using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Globalization;

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
///     DuckDB tiered-storage maintenance on <see cref="DatabaseFacade" />: create the control table and union
///     views, offload aged aggregates (root + children) to Parquet, and purge expired partitions. Configure
///     with <see cref="DuckDBTieredStoreExtensions.ToTieredStore{TRoot}" />.
/// </summary>
/// <remarks>
///     DuckDB is single-writer: run these from the writing process with no other writer active.
/// </remarks>
public static class DuckDBArchiveExtensions
{
    /// <summary>
    ///     Creates the tier control table and the union view for every configured tiered table (roots and
    ///     children) that has a read model, if they do not already exist. Called automatically by
    ///     <c>EnsureCreated()</c>; call it yourself after <c>Migrate()</c>. Safe to call repeatedly.
    /// </summary>
    public static void EnsureTieredStoresCreated(this DatabaseFacade database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var (context, sql) = Services(database);
        var aggregates = DuckDBTierAggregate.ResolveAll(context.Model);
        if (aggregates.Count == 0)
        {
            return;
        }

        var connection = (DuckDBConnection)database.GetDbConnection();
        var openedHere = Open(connection);
        try
        {
            ExecuteNonQuery(connection, DuckDBTierControl.ControlTableDdl(sql));
            foreach (var aggregate in aggregates)
            {
                RegenerateViews(connection, sql, aggregate);
            }
        }
        finally
        {
            Close(connection, openedHere);
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
        var (context, sql) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot))
            ?? throw NotConfigured(typeof(TRoot));
        var aligned = DuckDBTierControl.AlignCutoff(cutoff, aggregate.Granularity);
        var archivePath = aggregate.Root.ArchiveSubPath;
        // Only manage our own delete transaction when the caller has not already opened one on this connection
        // (a raw BEGIN inside an existing transaction would fail).
        var ownTransaction = database.CurrentTransaction is null;

        var connection = (DuckDBConnection)database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await ExecuteNonQueryAsync(connection, DuckDBTierControl.ControlTableDdl(sql), cancellationToken).ConfigureAwait(false);

            var current = ReadWatermark(connection, sql, aggregate.ControlKey);
            if (current is { } watermark && aligned <= watermark)
            {
                await DeleteAggregateAsync(connection, sql, aggregate, watermark, ownTransaction, cancellationToken).ConfigureAwait(false);
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
                    Directory.CreateDirectory(node.ArchiveSubPath);
                    await ExecuteNonQueryAsync(connection, CopySql(sql, aggregate, node, from, aligned), cancellationToken).ConfigureAwait(false);
                }
            }

            await ExecuteNonQueryAsync(connection, DuckDBTierControl.UpsertWatermarkSql(sql, aggregate.ControlKey, aligned, archivePath, aggregate.Granularity), cancellationToken).ConfigureAwait(false);
            RegenerateViews(connection, sql, aggregate);
            await DeleteAggregateAsync(connection, sql, aggregate, aligned, ownTransaction, cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, "CHECKPOINT;", cancellationToken).ConfigureAwait(false);

            return new TierArchiveResult(rows, aligned, archivePath, NoOp: false);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
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
        var (context, sql) = Services(database);
        var aggregate = DuckDBTierAggregate.Resolve(context.Model, typeof(TRoot)) ?? throw NotConfigured(typeof(TRoot));
        var cutoff = DuckDBTierControl.AlignCutoff(olderThan, aggregate.Granularity);
        var deleted = aggregate.Nodes.Sum(node => PurgePartitions(node.ArchiveSubPath, aggregate.Granularity, cutoff));

        if (deleted > 0)
        {
            // Regenerate the views so a table whose archive is now completely empty falls back to a hot-only
            // view instead of leaving a cold read_parquet() branch over a glob that matches no files (which
            // would throw on every query).
            var connection = (DuckDBConnection)database.GetDbConnection();
            var openedHere = Open(connection);
            try
            {
                RegenerateViews(connection, sql, aggregate);
            }
            finally
            {
                Close(connection, openedHere);
            }
        }

        return deleted;
    }

    private static string CopySql(ISqlGenerationHelper sql, DuckDBTierAggregate aggregate, DuckDBTierNode node, DateTime from, DateTime cutoff)
        => node.IsRoot
            ? DuckDBTierControl.ArchiveCopySql(sql, node.Table, node.Columns, aggregate.RootTimestampColumn, node.ArchiveSubPath, aggregate.Granularity, from, cutoff)
            : DuckDBTierControl.ArchiveChildCopySql(sql, node.Table, node.Columns, node.ChainToRoot, aggregate.RootTimestampColumn, node.ArchiveSubPath, aggregate.Granularity, from, cutoff);

    private static void RegenerateViews(DuckDBConnection connection, ISqlGenerationHelper sql, DuckDBTierAggregate aggregate)
    {
        foreach (var node in aggregate.Nodes)
        {
            if (node.ViewName is null)
            {
                continue;
            }

            var includeCold = HasArchiveFiles(connection, node.ArchiveSubPath);
            var viewSql = node.IsRoot
                ? DuckDBTierControl.ViewSql(sql, node.ViewName, node.Table, node.Columns, aggregate.RootTimestampColumn, aggregate.ControlKey, node.ArchiveSubPath, aggregate.Granularity, includeCold)
                : DuckDBTierControl.ChildViewSql(sql, node.ViewName, node.Table, node.Columns, node.ChainToRoot, aggregate.RootTimestampColumn, aggregate.ControlKey, node.ArchiveSubPath, aggregate.Granularity, includeCold, aggregate.IncludeHotChildFilter);
            ExecuteNonQuery(connection, viewSql);
        }
    }

    private static async Task DeleteAggregateAsync(DuckDBConnection connection, ISqlGenerationHelper sql, DuckDBTierAggregate aggregate, DateTime cutoff, bool ownTransaction, CancellationToken cancellationToken)
    {
        if (ownTransaction)
        {
            await ExecuteNonQueryAsync(connection, "BEGIN TRANSACTION;", cancellationToken).ConfigureAwait(false);
        }

        try
        {
            // Leaf → root so foreign keys stay satisfied.
            for (var i = aggregate.Nodes.Count - 1; i >= 0; i--)
            {
                var node = aggregate.Nodes[i];
                var deleteSql = node.IsRoot
                    ? DuckDBTierControl.DeleteHotSql(sql, node.Table, aggregate.RootTimestampColumn, cutoff)
                    : DuckDBTierControl.DeleteChildSql(sql, node.Table, node.ChainToRoot, aggregate.RootTimestampColumn, cutoff);
                await ExecuteNonQueryAsync(connection, deleteSql, cancellationToken).ConfigureAwait(false);
            }

            if (ownTransaction)
            {
                await ExecuteNonQueryAsync(connection, "COMMIT;", cancellationToken).ConfigureAwait(false);
            }
        }
        catch when (ownTransaction)
        {
            await ExecuteNonQueryAsync(connection, "ROLLBACK;", cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static string CountRootWindowSql(ISqlGenerationHelper sql, DuckDBTierAggregate aggregate, DateTime from, DateTime cutoff)
    {
        var ts = sql.DelimitIdentifier(aggregate.RootTimestampColumn);
        return $"SELECT count(*) FROM {sql.DelimitIdentifier(aggregate.Root.Table)} "
               + $"WHERE {ts} >= TIMESTAMP '{from.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)}' "
               + $"AND {ts} < TIMESTAMP '{cutoff.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture)}';";
    }

    private static int PurgePartitions(string archivePath, Metadata.TierGranularity granularity, DateTime cutoff)
    {
        var root = archivePath.TrimEnd('/', '\\');
        if (!Directory.Exists(root))
        {
            return 0;
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

                if (granularity == Metadata.TierGranularity.Day)
                {
                    foreach (var dayDir in Directory.GetDirectories(monthDir, "day=*"))
                    {
                        if (TryParsePart(dayDir, "day=", out var day) && new DateTime(year, month, day) < cutoff)
                        {
                            Directory.Delete(dayDir, recursive: true);
                            deleted++;
                        }
                    }
                }
                else if (new DateTime(year, month, 1) < cutoff)
                {
                    Directory.Delete(monthDir, recursive: true);
                    deleted++;
                }
            }
        }

        return deleted;
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

    private static (DbContext Context, ISqlGenerationHelper Sql) Services(DatabaseFacade database)
        => (database.GetService<ICurrentDbContext>().Context, database.GetService<ISqlGenerationHelper>());

    private static DateTime? ReadWatermark(DuckDBConnection connection, ISqlGenerationHelper sql, string controlKey)
        => ExecuteScalar(connection, DuckDBTierControl.ReadWatermarkSql(sql, controlKey)) is DateTime dt ? dt : null;

    private static bool HasArchiveFiles(DuckDBConnection connection, string archivePath)
    {
        var glob = DuckDBTierControl.ReadGlob(archivePath).Replace("'", "''");
        return Convert.ToInt64(ExecuteScalar(connection, $"SELECT count(*) FROM glob('{glob}');"), CultureInfo.InvariantCulture) > 0;
    }

    private static bool Open(DuckDBConnection connection)
    {
        if (connection.State == ConnectionState.Open)
        {
            return false;
        }

        connection.Open();
        return true;
    }

    private static void Close(DuckDBConnection connection, bool openedHere)
    {
        if (openedHere)
        {
            connection.Close();
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
