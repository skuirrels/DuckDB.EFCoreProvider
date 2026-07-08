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
        // Only manage our own delete transaction when the caller has not already opened one on this connection
        // (a raw BEGIN inside an existing transaction would fail).
        var ownTransaction = database.CurrentTransaction is null;

        var openedHere = await OpenTrackedAsync(database, cancellationToken).ConfigureAwait(false);
        var connection = (DuckDBConnection)database.GetDbConnection();

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

            await ExecuteNonQueryAsync(connection, DuckDBTierControl.UpsertWatermarkSql(sql, aggregate.ControlKey, aligned, archivePath, aggregate.Granularity), cancellationToken).ConfigureAwait(false);
            RegenerateViews(connection, sql, archiveFileProbe, aggregate);
            await DeleteAggregateAsync(connection, sql, aggregate, aligned, ownTransaction, cancellationToken).ConfigureAwait(false);
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
        var deleted = aggregate.Nodes.Sum(node => PurgePartitions(node.ArchiveSubPath, aggregate.Granularity, cutoff));

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
            ? DuckDBTierControl.ArchiveCopySql(sql, node.Table, node.Schema, node.Columns, aggregate.RootTimestampColumn, node.ArchiveSubPath, aggregate.Granularity, from, cutoff)
            : DuckDBTierControl.ArchiveChildCopySql(sql, node.Table, node.Schema, node.Columns, node.ChainToRoot, aggregate.RootTimestampColumn, node.ArchiveSubPath, aggregate.Granularity, from, cutoff);

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
                ? DuckDBTierControl.ViewSql(sql, node.ViewName, node.Table, node.Schema, node.Columns, node.KeyColumns, aggregate.RootTimestampColumn, aggregate.ControlKey, node.ArchiveSubPath, aggregate.Granularity, includeCold)
                : DuckDBTierControl.ChildViewSql(sql, node.ViewName, node.Table, node.Schema, node.Columns, node.KeyColumns, node.ChainToRoot, aggregate.RootTimestampColumn, aggregate.ControlKey, node.ArchiveSubPath, aggregate.Granularity, includeCold, aggregate.IncludeHotChildFilter);
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
                    ? DuckDBTierControl.DeleteHotSql(sql, node.Table, node.Schema, node.KeyColumns, aggregate.RootTimestampColumn, node.ArchiveSubPath, cutoff)
                    : DuckDBTierControl.DeleteChildSql(sql, node.Table, node.Schema, node.KeyColumns, node.ChainToRoot, aggregate.RootTimestampColumn, node.ArchiveSubPath, cutoff);
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

    /// <summary>
    ///     Returns <see langword="true" /> if the archive path targets a remote object store (has a URL scheme
    ///     such as <c>s3://</c>, <c>gcs://</c>, <c>r2://</c>, <c>azure://</c>, <c>http(s)://</c>) rather than a
    ///     local or mounted filesystem path.
    /// </summary>
    private static bool IsRemoteArchive(string archivePath)
        => System.Text.RegularExpressions.Regex.IsMatch(archivePath, "^[A-Za-z][A-Za-z0-9+.-]*://");

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
