using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.ValueGeneration.Internal;

/// <summary>
///     Reserves blocks of Hi-Lo key values from a one-row counter table.
/// </summary>
/// <remarks>
///     <para>
///         Each block is reserved in its own short transaction on a second connection to the context's database
///         instance, which shares its attached catalogs but not the caller's transaction. A reserved block
///         therefore stays reserved when the caller rolls back, so values are never handed out twice.
///     </para>
///     <para>
///         Two writers that reserve at once update the same counter row, which DuckDB and DuckLake report as a
///         write-write conflict; the loser retries with the new value. The counter table is created on first use
///         in the same transaction as its first row, starting above the largest key already stored in any column
///         that shares it. A concurrent create conflicts in the same way, so only one starting row ever commits.
///     </para>
/// </remarks>
internal sealed class DuckDBHiLoBlockAllocator
{
    private const string CounterColumn = "NextValue";
    private const int MaxAttempts = 20;

    private readonly IRelationalConnection _connection;
    private readonly ISqlGenerationHelper _sqlGenerationHelper;

    public DuckDBHiLoBlockAllocator(IRelationalConnection connection, ISqlGenerationHelper sqlGenerationHelper)
    {
        _connection = connection;
        _sqlGenerationHelper = sqlGenerationHelper;
    }

    /// <summary>
    ///     Reserves <paramref name="blockSize" /> values and returns the first of them.
    /// </summary>
    internal long Allocate(DuckDBHiLoCounter counter, int blockSize)
        => AllocateAsync(counter, blockSize, async: false, CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc cref="Allocate" />
    internal Task<long> AllocateAsync(DuckDBHiLoCounter counter, int blockSize, CancellationToken cancellationToken)
        => AllocateAsync(counter, blockSize, async: true, cancellationToken);

    private async Task<long> AllocateAsync(
        DuckDBHiLoCounter counter,
        int blockSize,
        bool async,
        CancellationToken cancellationToken)
    {
        var openedHere = async
            ? await _connection.OpenAsync(cancellationToken).ConfigureAwait(false)
            : _connection.Open();
        try
        {
            var source = _connection.DbConnection as DuckDBConnection
                ?? throw new NotSupportedException(
                    "Hi-Lo key generation needs a local DuckDB connection; the configured engine does not provide one.");
            var location = await ResolveLocationAsync(source, counter, async, cancellationToken).ConfigureAwait(false);

            for (var attempt = 1; ; attempt++)
            {
                var duplicate = CreateIndependentConnection(source);
                try
                {
                    if (duplicate.State != System.Data.ConnectionState.Open)
                    {
                        if (async)
                        {
                            await duplicate.OpenAsync(cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            duplicate.Open();
                        }
                    }

                    return await ReserveAsync(duplicate, counter, location, blockSize, async, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (attempt < MaxAttempts && IsContention(exception))
                {
                    var delay = TimeSpan.FromMilliseconds(Random.Shared.Next(5, 20) * Math.Min(attempt, 10));
                    if (async)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        Thread.Sleep(delay);
                    }
                }
                finally
                {
                    if (async)
                    {
                        await duplicate.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        duplicate.Dispose();
                    }
                }
            }
        }
        finally
        {
            if (openedHere)
            {
                if (async)
                {
                    await _connection.CloseAsync().ConfigureAwait(false);
                }
                else
                {
                    _connection.Close();
                }
            }
        }
    }

    private async Task<long> ReserveAsync(
        DuckDBConnection connection,
        DuckDBHiLoCounter counter,
        Location location,
        int blockSize,
        bool async,
        CancellationToken cancellationToken)
    {
        var table = Qualify(location.Database, location.Schema, counter.TableName);
        var column = _sqlGenerationHelper.DelimitIdentifier(CounterColumn);
        await ExecuteAsync(connection, "BEGIN TRANSACTION;", [], async, cancellationToken).ConfigureAwait(false);
        var committed = false;
        try
        {
            long low;
            if (await TableExistsAsync(connection, location.Database, location.Schema, counter.TableName, async, cancellationToken)
                    .ConfigureAwait(false))
            {
                var values = await ReadInt64sAsync(connection, $"SELECT {column} FROM {table};", async, cancellationToken)
                    .ConfigureAwait(false);
                if (values.Count != 1 || values[0] is not { } current)
                {
                    throw new InvalidOperationException(
                        $"The Hi-Lo counter table '{counter.TableName}' must hold exactly one non-null row, "
                        + $"but it holds {values.Count}.");
                }

                low = current;
                var updated = await ExecuteAsync(
                        connection,
                        $"UPDATE {table} SET {column} = $next WHERE {column} = $current;",
                        [("next", checked(low + blockSize)), ("current", low)],
                        async,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (updated != 1)
                {
                    throw new CounterMovedException();
                }
            }
            else
            {
                low = await SeedAsync(connection, counter, location, async, cancellationToken).ConfigureAwait(false);
                await ExecuteAsync(connection, $"CREATE TABLE {table} ({column} BIGINT);", [], async, cancellationToken)
                    .ConfigureAwait(false);
                await ExecuteAsync(
                        connection,
                        $"INSERT INTO {table} ({column}) VALUES ($next);",
                        [("next", checked(low + blockSize))],
                        async,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await ExecuteAsync(connection, "COMMIT;", [], async, cancellationToken).ConfigureAwait(false);
            committed = true;
            return low;
        }
        finally
        {
            if (!committed)
            {
                try
                {
                    await ExecuteAsync(connection, "ROLLBACK;", [], async, CancellationToken.None).ConfigureAwait(false);
                }
                catch (DuckDBException)
                {
                    // A failed commit has already ended the transaction; the original failure is what matters.
                }
            }
        }
    }

    // The first value is one above the largest key already stored in any column that shares the counter.
    private async Task<long> SeedAsync(
        DuckDBConnection connection,
        DuckDBHiLoCounter counter,
        Location location,
        bool async,
        CancellationToken cancellationToken)
    {
        long largest = 0;
        foreach (var key in counter.KeyColumns)
        {
            var schema = key.Schema ?? location.Schema;
            if (!await TableExistsAsync(connection, location.Database, schema, key.Table, async, cancellationToken)
                    .ConfigureAwait(false))
            {
                continue;
            }

            var values = await ReadInt64sAsync(
                    connection,
                    $"SELECT CAST(max({_sqlGenerationHelper.DelimitIdentifier(key.Column)}) AS BIGINT) "
                    + $"FROM {Qualify(location.Database, schema, key.Table)};",
                    async,
                    cancellationToken)
                .ConfigureAwait(false);
            if (values is [{ } maximum] && maximum > largest)
            {
                largest = maximum;
            }
        }

        return checked(largest + 1);
    }

    private async Task<Location> ResolveLocationAsync(
        DuckDBConnection connection,
        DuckDBHiLoCounter counter,
        bool async,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_database(), current_schema();";
        await using var reader = async
            ? await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)
            : command.ExecuteReader();
        if (!(async ? await reader.ReadAsync(cancellationToken).ConfigureAwait(false) : reader.Read()))
        {
            throw new InvalidOperationException("DuckDB did not report the current database.");
        }

        return new Location(reader.GetString(0), counter.Schema ?? reader.GetString(1));
    }

    private static async Task<bool> TableExistsAsync(
        DuckDBConnection connection,
        string database,
        string schema,
        string table,
        bool async,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM duckdb_tables() "
            + "WHERE database_name = $d AND schema_name = $s AND lower(table_name) = lower($t);";
        command.AddParameter("d", database);
        command.AddParameter("s", schema);
        command.AddParameter("t", table);
        var count = async
            ? await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            : command.ExecuteScalar();
        return Convert.ToInt64(count, System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<List<long?>> ReadInt64sAsync(
        DuckDBConnection connection,
        string commandText,
        bool async,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await using var reader = async
            ? await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false)
            : command.ExecuteReader();
        var values = new List<long?>();
        while (async ? await reader.ReadAsync(cancellationToken).ConfigureAwait(false) : reader.Read())
        {
            values.Add(reader.IsDBNull(0) ? null : reader.GetInt64(0));
        }

        return values;
    }

    private static async Task<int> ExecuteAsync(
        DuckDBConnection connection,
        string commandText,
        (string Name, long Value)[] parameters,
        bool async,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        foreach (var (name, value) in parameters)
        {
            command.AddParameter(name, value);
        }

        return async
            ? await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false)
            : command.ExecuteNonQuery();
    }

    // An in-memory database is reachable only through a duplicate of its connection. A file database is shared by
    // every connection DuckDB.NET opens with the same connection string, which a duplicate does not support.
    private static DuckDBConnection CreateIndependentConnection(DuckDBConnection source)
    {
        var dataSource = new DuckDBConnectionStringBuilder { ConnectionString = source.ConnectionString }.DataSource;
        return string.IsNullOrWhiteSpace(dataSource)
            || dataSource.StartsWith(":memory:", StringComparison.OrdinalIgnoreCase)
                ? source.Duplicate()
                : new DuckDBConnection(source.ConnectionString);
    }

    private string Qualify(string database, string schema, string table)
        => $"{_sqlGenerationHelper.DelimitIdentifier(database)}.{_sqlGenerationHelper.DelimitIdentifier(table, schema)}";

    // A concurrent reservation or counter creation; retrying sees the winner's committed value.
    private static bool IsContention(Exception exception)
        => exception is CounterMovedException
            || DuckDBTransientExceptionDetector.ShouldRetryOn(exception)
            || (exception is DuckDBException
                && exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase));

    private sealed record Location(string Database, string Schema);

    // Another writer committed a new counter value after this transaction read it.
    private sealed class CounterMovedException : Exception;
}