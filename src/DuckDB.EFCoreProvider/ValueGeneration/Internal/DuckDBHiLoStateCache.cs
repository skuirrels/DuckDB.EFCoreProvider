using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using System.Collections.Concurrent;

namespace DuckDB.EFCoreProvider.ValueGeneration.Internal;

/// <summary>
///     Shares Hi-Lo block state between contexts that use the same persistent database, so each block is used to the
///     end instead of being abandoned when a short-lived context is disposed.
/// </summary>
/// <remarks>
///     State is shared only when the database can be identified: a DuckLake catalog by its metadata source, an
///     encrypted database by its file, and a native database by its file connection string. An in-memory database
///     is private to its connection, so its contexts keep their own state. Deleting a database through the provider
///     discards the state for it, so a recreated database starts again from its new counter.
/// </remarks>
internal sealed class DuckDBHiLoStateCache
{
    private readonly ConcurrentDictionary<StateKey, HiLoValueGeneratorState> _states = new();

    internal HiLoValueGeneratorState GetOrAdd(string databaseIdentity, string? schema, string table, int blockSize)
        => _states.GetOrAdd(
            new StateKey(databaseIdentity, schema, table, blockSize),
            static key => new HiLoValueGeneratorState(key.BlockSize));

    internal void Reset(string? databaseIdentity)
    {
        if (databaseIdentity is null)
        {
            return;
        }

        foreach (var key in _states.Keys)
        {
            if (string.Equals(key.DatabaseIdentity, databaseIdentity, StringComparison.Ordinal)
                && _states.TryRemove(key, out var state))
            {
                state.Dispose();
            }
        }
    }

    /// <summary>
    ///     Identifies the database that holds the Hi-Lo counters, or returns <see langword="null" /> when it is an
    ///     in-memory database that no other connection can see.
    /// </summary>
    internal static string? GetDatabaseIdentity(IDbContextOptions options, string? connectionString)
    {
        var extension = options.FindExtension<DuckDBOptionsExtension>();
        if (extension?.DuckLakeOptions is { } duckLake)
        {
            return $"ducklake\n{duckLake.UsesSecret}\n{duckLake.MetadataSource}\n{duckLake.CatalogName}\n{duckLake.DataPath}";
        }

        if (extension?.EncryptedDatabase is { } encrypted)
        {
            return $"encrypted\n{Path.GetFullPath(encrypted.Path)}";
        }

        if (connectionString is null)
        {
            return null;
        }

        var dataSource = new DuckDBConnectionStringBuilder { ConnectionString = connectionString }.DataSource;
        return string.IsNullOrWhiteSpace(dataSource) || dataSource.StartsWith(":memory:", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"native\n{Path.GetFullPath(dataSource)}";
    }

    private sealed record StateKey(string DatabaseIdentity, string? Schema, string Table, int BlockSize);
}