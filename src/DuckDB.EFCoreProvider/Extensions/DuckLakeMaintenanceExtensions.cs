using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Globalization;
using System.Numerics;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>A committed DuckLake snapshot.</summary>
public sealed record DuckLakeSnapshot(
    long SnapshotId,
    DateTimeOffset SnapshotTime,
    long SchemaVersion,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Changes,
    string? Author,
    string? CommitMessage,
    string? CommitExtraInfo);

/// <summary>The result of a DuckLake file merge or rewrite operation.</summary>
public sealed record DuckLakeFileRewriteResult(
    string SchemaName,
    string TableName,
    long FilesProcessed,
    long FilesCreated);

/// <summary>The result of flushing one table's inlined rows to data files.</summary>
public sealed record DuckLakeFlushResult(string SchemaName, string TableName, BigInteger RowsFlushed);

/// <summary>Options for merging adjacent DuckLake files.</summary>
public sealed class DuckLakeMergeOptions
{
    /// <summary>Limits the operation to one table. When omitted, DuckLake processes eligible tables.</summary>
    public string? TableName { get; init; }

    /// <summary>The table schema when <see cref="TableName" /> is supplied.</summary>
    public string SchemaName { get; init; } = "main";

    /// <summary>Excludes files smaller than this size.</summary>
    public ulong? MinimumFileSizeBytes { get; init; }

    /// <summary>Excludes files at or above this size.</summary>
    public ulong? MaximumFileSizeBytes { get; init; }

    /// <summary>Limits the number of compaction outputs per table.</summary>
    public ulong? MaximumCompactedFiles { get; init; }
}

/// <summary>Options for rewriting heavily deleted DuckLake files.</summary>
public sealed class DuckLakeRewriteOptions
{
    /// <summary>Limits the operation to one table. When omitted, DuckLake processes eligible tables.</summary>
    public string? TableName { get; init; }

    /// <summary>The table schema when <see cref="TableName" /> is supplied.</summary>
    public string SchemaName { get; init; } = "main";

    /// <summary>The deleted-row fraction at which a file is rewritten.</summary>
    public double? DeleteThreshold { get; init; }
}

/// <summary>Options for flushing inlined DuckLake data.</summary>
public sealed class DuckLakeFlushOptions
{
    /// <summary>Limits the operation to one table. When omitted, DuckLake processes eligible tables.</summary>
    public string? TableName { get; init; }

    /// <summary>Limits the operation to one schema. May be used with or without <see cref="TableName" />.</summary>
    public string? SchemaName { get; init; }
}

/// <summary>Provides typed operations for the DuckLake catalog selected by the current context.</summary>
public sealed class DuckLakeMaintenanceFacade
{
    private readonly DatabaseFacade _database;
    private readonly DuckLakeOptions _options;

    internal DuckLakeMaintenanceFacade(DatabaseFacade database, DuckLakeOptions options)
    {
        _database = database;
        _options = options;
    }

    /// <summary>Gets all retained snapshots in snapshot order.</summary>
    public Task<IReadOnlyList<DuckLakeSnapshot>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
        => ReadSnapshotsAsync(
            "SELECT snapshot_id, CAST(snapshot_time AS VARCHAR), schema_version, changes, author, commit_message, "
            + "commit_extra_info FROM ducklake_snapshots({0}) ORDER BY snapshot_id",
            [_options.CatalogName],
            cancellationToken);

    /// <summary>Sets the author, commit message, and optional extra information for the current transaction.</summary>
    /// <remarks>
    ///     <para>
    ///         DuckLake records one snapshot for a committed transaction. Begin an explicit EF transaction, perform
    ///         the writes, call this method before commit, and then commit that transaction. The provider stores no
    ///         ambient author or message and does not derive either value from application state.
    ///     </para>
    ///     <para><paramref name="extraInfo" /> is passed to DuckLake as an opaque string, commonly JSON.</para>
    /// </remarks>
    /// <param name="author">The snapshot author supplied by the caller.</param>
    /// <param name="commitMessage">The snapshot commit message supplied by the caller.</param>
    /// <param name="extraInfo">Optional opaque information associated with the snapshot.</param>
    /// <param name="cancellationToken">A token used to cancel the command.</param>
    /// <exception cref="InvalidOperationException">
    ///     The profile is read-only or the context has no active transaction.
    /// </exception>
    public async Task SetCommitMessageAsync(
        string author,
        string commitMessage,
        string? extraInfo = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitMessage);
        if (_options.IsReadOnly)
        {
            throw new InvalidOperationException("DuckLake commit messages cannot be set through a read-only profile.");
        }

        if (_database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "SetCommitMessageAsync requires an active transaction so the metadata is attached to one DuckLake snapshot.");
        }

        var catalog = DelimitIdentifier(_options.CatalogName);
        var sql = extraInfo is null
            ? $"CALL {catalog}.set_commit_message({{0}}, {{1}})"
            : $"CALL {catalog}.set_commit_message({{0}}, {{1}}, extra_info => {{2}})";
        var parameters = extraInfo is null
            ? new object[] { author, commitMessage }
            : new object[] { author, commitMessage, extraInfo };

        await _database.ExecuteSqlRawAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Expires snapshots older than <paramref name="olderThan" />. Dry-run mode is enabled by default.
    /// </summary>
    public Task<IReadOnlyList<DuckLakeSnapshot>> ExpireSnapshotsAsync(
        DateTimeOffset olderThan,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        return ReadSnapshotsAsync(
            "SELECT snapshot_id, CAST(snapshot_time AS VARCHAR), schema_version, changes, author, commit_message, "
            + "commit_extra_info FROM ducklake_expire_snapshots({0}, dry_run => {1}, older_than => {2}) "
            + "ORDER BY snapshot_id",
            [_options.CatalogName, dryRun, olderThan],
            cancellationToken);
    }

    /// <summary>
    ///     Lists or deletes files scheduled for deletion before <paramref name="olderThan" />. Dry-run mode is
    ///     enabled by default.
    /// </summary>
    public Task<IReadOnlyList<string>> CleanupOldFilesAsync(
        DateTimeOffset olderThan,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        return ReadPathsAsync(
            "SELECT path FROM ducklake_cleanup_old_files({0}, dry_run => {1}, older_than => {2})",
            [_options.CatalogName, dryRun, olderThan],
            cancellationToken);
    }

    /// <summary>
    ///     Lists or deletes untracked files older than <paramref name="olderThan" />. Dry-run mode is enabled by
    ///     default.
    /// </summary>
    public Task<IReadOnlyList<string>> DeleteOrphanedFilesAsync(
        DateTimeOffset olderThan,
        bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        return ReadPathsAsync(
            "SELECT path FROM ducklake_delete_orphaned_files({0}, dry_run => {1}, older_than => {2})",
            [_options.CatalogName, dryRun, olderThan],
            cancellationToken);
    }

    /// <summary>Flushes eligible inlined rows to DuckLake data files.</summary>
    public Task<IReadOnlyList<DuckLakeFlushResult>> FlushInlinedDataAsync(
        DuckLakeFlushOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        options ??= new DuckLakeFlushOptions();

        var parameters = new List<object?> { _options.CatalogName };
        var arguments = new List<string> { "{0}" };
        if (options.SchemaName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.SchemaName);
            arguments.Add($"schema_name => {{{parameters.Count}}}");
            parameters.Add(options.SchemaName);
        }

        if (options.TableName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.TableName);
            arguments.Add($"table_name => {{{parameters.Count}}}");
            parameters.Add(options.TableName);
        }

        return ReadFlushResultsAsync(
            $"SELECT schema_name, table_name, rows_flushed FROM ducklake_flush_inlined_data({string.Join(", ", arguments)})",
            parameters,
            cancellationToken);
    }

    /// <summary>Merges adjacent DuckLake files using the supplied technical thresholds.</summary>
    public Task<IReadOnlyList<DuckLakeFileRewriteResult>> MergeAdjacentFilesAsync(
        DuckLakeMergeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        options ??= new DuckLakeMergeOptions();
        var (arguments, parameters) = BuildTableArguments(
            _options.CatalogName,
            options.TableName,
            options.SchemaName);
        AddNamedArgument(arguments, parameters, "min_file_size", options.MinimumFileSizeBytes);
        AddNamedArgument(arguments, parameters, "max_file_size", options.MaximumFileSizeBytes);
        AddNamedArgument(arguments, parameters, "max_compacted_files", options.MaximumCompactedFiles);

        return ReadFileRewriteResultsAsync(
            $"SELECT schema_name, table_name, files_processed, files_created "
            + $"FROM ducklake_merge_adjacent_files({string.Join(", ", arguments)})",
            parameters,
            cancellationToken);
    }

    /// <summary>Rewrites DuckLake files whose deleted-row fraction exceeds the supplied threshold.</summary>
    public Task<IReadOnlyList<DuckLakeFileRewriteResult>> RewriteDataFilesAsync(
        DuckLakeRewriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        options ??= new DuckLakeRewriteOptions();
        if (options.DeleteThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.DeleteThreshold,
                "The delete threshold must be between 0 and 1.");
        }

        var (arguments, parameters) = BuildTableArguments(
            _options.CatalogName,
            options.TableName,
            options.SchemaName);
        AddNamedArgument(arguments, parameters, "delete_threshold", options.DeleteThreshold);

        return ReadFileRewriteResultsAsync(
            $"SELECT schema_name, table_name, files_processed, files_created "
            + $"FROM ducklake_rewrite_data_files({string.Join(", ", arguments)})",
            parameters,
            cancellationToken);
    }

    private async Task<IReadOnlyList<DuckLakeSnapshot>> ReadSnapshotsAsync(
        string sql,
        IReadOnlyList<object?> parameters,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<DuckLakeSnapshot>();
        await using var result = await _database
            .SqlQueryDynamicRawAsync(sql, parameters, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var row in result.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = row.ToArray();
            snapshots.Add(new DuckLakeSnapshot(
                (long)values[0]!,
                DateTimeOffset.Parse((string)values[1]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                (long)values[2]!,
                ConvertChanges(values[3]),
                values[4] as string,
                values[5] as string,
                values[6] as string));
        }

        return snapshots;
    }

    private async Task<IReadOnlyList<string>> ReadPathsAsync(
        string sql,
        IReadOnlyList<object?> parameters,
        CancellationToken cancellationToken)
    {
        var paths = new List<string>();
        await using var result = await _database
            .SqlQueryDynamicRawAsync(sql, parameters, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var row in result.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
        {
            paths.Add((string)row.Span[0]!);
        }

        return paths;
    }

    private async Task<IReadOnlyList<DuckLakeFlushResult>> ReadFlushResultsAsync(
        string sql,
        IReadOnlyList<object?> parameters,
        CancellationToken cancellationToken)
    {
        var results = new List<DuckLakeFlushResult>();
        await using var result = await _database
            .SqlQueryDynamicRawAsync(sql, parameters, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var row in result.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = row.ToArray();
            results.Add(new DuckLakeFlushResult(
                (string)values[0]!,
                (string)values[1]!,
                (BigInteger)values[2]!));
        }

        return results;
    }

    private async Task<IReadOnlyList<DuckLakeFileRewriteResult>> ReadFileRewriteResultsAsync(
        string sql,
        IReadOnlyList<object?> parameters,
        CancellationToken cancellationToken)
    {
        var results = new List<DuckLakeFileRewriteResult>();
        await using var result = await _database
            .SqlQueryDynamicRawAsync(sql, parameters, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var row in result.ReadRowsAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = row.ToArray();
            results.Add(new DuckLakeFileRewriteResult(
                (string)values[0]!,
                (string)values[1]!,
                (long)values[2]!,
                (long)values[3]!));
        }

        return results;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ConvertChanges(object? value)
    {
        if (value is not Dictionary<string, List<string>> changes)
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }

        return changes.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
    }

    private static (List<string> Arguments, List<object?> Parameters) BuildTableArguments(
        string catalogName,
        string? tableName,
        string schemaName)
    {
        var parameters = new List<object?> { catalogName };
        var arguments = new List<string> { "{0}" };
        if (tableName is null)
        {
            return (arguments, parameters);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        arguments.Add("{1}");
        parameters.Add(tableName);
        arguments.Add("schema => {2}");
        parameters.Add(schemaName);
        return (arguments, parameters);
    }

    private static void AddNamedArgument<T>(
        ICollection<string> arguments,
        ICollection<object?> parameters,
        string name,
        T? value)
        where T : struct
    {
        if (value is null)
        {
            return;
        }

        arguments.Add($"{name} => {{{parameters.Count}}}");
        parameters.Add(value.Value);
    }

    private static string DelimitIdentifier(string identifier)
        => '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    private void EnsureWritable()
    {
        if (_options.IsReadOnly)
        {
            throw new InvalidOperationException("DuckLake maintenance operations cannot run through a read-only profile.");
        }
    }
}

/// <summary>Creates a typed DuckLake maintenance facade for a configured context.</summary>
public static class DuckLakeMaintenanceExtensions
{
    /// <summary>Gets typed operations for the DuckLake catalog selected by this context.</summary>
    /// <exception cref="InvalidOperationException">The context is not configured with a DuckLake profile.</exception>
    public static DuckLakeMaintenanceFacade DuckLake(this DatabaseFacade database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var context = database.GetService<ICurrentDbContext>().Context;
        var options = context.GetService<IDbContextOptions>()
            .FindExtension<DuckDBOptionsExtension>()?
            .DuckLakeOptions;

        return options is null
            ? throw new InvalidOperationException("DuckLake operations require a context configured with UseDuckLake(...).")
            : new DuckLakeMaintenanceFacade(database, options);
    }
}