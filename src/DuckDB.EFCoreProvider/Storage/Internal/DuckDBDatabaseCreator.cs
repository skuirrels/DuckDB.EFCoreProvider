using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBDatabaseCreator : RelationalDatabaseCreator
{
    private readonly IDuckDBRelationalConnection _connection;
    private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;

    public DuckDBDatabaseCreator(
        RelationalDatabaseCreatorDependencies dependencies,
        IDuckDBRelationalConnection connection,
        IRawSqlCommandBuilder rawSqlCommandBuilder)
        : base(dependencies)
    {
        _connection = connection;
        _rawSqlCommandBuilder = rawSqlCommandBuilder;
    }

    /// <inheritdoc />
    public override bool Exists()
    {
        var connectionOptions = new DuckDBConnectionStringBuilder
        {
            ConnectionString = _connection.ConnectionString
        };

        var dataSource = connectionOptions.DataSource;

        if (dataSource.Equals(DuckDBConnectionStringBuilder.InMemoryDataSource, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // For a local file database, existence is simply whether the file is present. Checking the file
        // directly is robust even when another process holds the single-writer lock on it — opening a
        // read-only probe in that situation throws and would otherwise be misreported as "does not exist".
        if (File.Exists(dataSource))
        {
            return true;
        }

        // Fall back to a read-only open probe for non-file data sources (the file path may be resolved by
        // DuckDB rather than the filesystem). A failure to open is treated as "does not exist", which is the
        // best signal available: DuckDB.NET surfaces a generic DuckDBException without a stable error code to
        // distinguish "missing" from other open failures.
        using var readOnlyConnection = _connection.CreateReadOnlyConnection();

        try
        {
            readOnlyConnection.Open(errorsExpected: true);
        }
        catch (DuckDBException)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override bool HasTables()
    {
        return (bool)_rawSqlCommandBuilder
            .Build(
                """
                SELECT coalesce(any_value(true), false)
                  FROM duckdb_tables
                 WHERE table_name != $default_table_name
                """,
                [
                    new DuckDBParameter("default_table_name", HistoryRepository.DefaultTableName)
                ]).RelationalCommand.ExecuteScalar(new RelationalCommandParameterObject(
                Dependencies.Connection,
                null,
                null,
                null,
                Dependencies.CommandLogger, CommandSource.Migrations))!;
    }

    /// <inheritdoc />
    public override void Create()
    {
        Dependencies.Connection.Open();
        Dependencies.Connection.Close();
    }

    /// <inheritdoc />
    public override void CreateTables()
    {
        base.CreateTables();

        // Create the tiered-storage control table and union views alongside the hot tables, so a fresh
        // EnsureCreated() yields a queryable tiered store without a separate EnsureTieredStoresCreated() call.
        // No-op when the model has no tiered entities.
        Dependencies.CurrentContext.Context.Database.EnsureTieredStoresCreated();
    }

    /// <inheritdoc />
    public override async Task CreateTablesAsync(CancellationToken cancellationToken = default)
    {
        await base.CreateTablesAsync(cancellationToken).ConfigureAwait(false);

        Dependencies.CurrentContext.Context.Database.EnsureTieredStoresCreated();
    }

    /// <inheritdoc />
    public override void Delete()
    {
        string? path = null;

        Dependencies.Connection.Open();
        var dbConnection = Dependencies.Connection.DbConnection;
        try
        {
            path = dbConnection.DataSource;
        }
        catch
        {
            // any exceptions here can be ignored
        }
        finally
        {
            Dependencies.Connection.Close();
        }

        if (!string.IsNullOrEmpty(path))
        {
            File.Delete(path);
        }
        else if (dbConnection.State == ConnectionState.Open)
        {
            dbConnection.Close();
            dbConnection.Open();
        }
    }
}
