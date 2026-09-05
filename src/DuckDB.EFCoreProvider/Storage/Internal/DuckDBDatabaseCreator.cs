using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;

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
    private readonly DuckLakeOptions? _duckLakeOptions;
    private readonly DuckDBEncryptedDatabaseOptions? _encryptedDatabase;
    private readonly bool _supportsSchemaManagement;
    private readonly bool _supportsDatabaseDeletion;
    private bool _duckLakeCatalogReadyForEnsureCreated;

    public DuckDBDatabaseCreator(
        RelationalDatabaseCreatorDependencies dependencies,
        IDuckDBRelationalConnection connection,
        IRawSqlCommandBuilder rawSqlCommandBuilder)
        : this(
            dependencies,
            connection,
            rawSqlCommandBuilder,
            DuckDBEngineCapabilities.FromOptions(
                dependencies.CurrentContext.Context.GetService<IDbContextOptions>()))
    {
    }

    public DuckDBDatabaseCreator(
        RelationalDatabaseCreatorDependencies dependencies,
        IDuckDBRelationalConnection connection,
        IRawSqlCommandBuilder rawSqlCommandBuilder,
        IDuckDBEngineCapabilities engineCapabilities)
        : base(dependencies)
    {
        _connection = connection;
        _rawSqlCommandBuilder = rawSqlCommandBuilder;
        var contextOptions = dependencies.CurrentContext.Context.GetService<IDbContextOptions>();
        var providerOptions = contextOptions.FindExtension<DuckDBOptionsExtension>();
        _duckLakeOptions = providerOptions?.DuckLakeOptions;
        _encryptedDatabase = providerOptions?.EncryptedDatabase;
        var capabilities = engineCapabilities ?? throw new ArgumentNullException(nameof(engineCapabilities));
        _supportsSchemaManagement = capabilities.SupportsSchemaManagement;
        _supportsDatabaseDeletion = capabilities.SupportsDatabaseDeletion;
    }

    /// <inheritdoc />
    public override bool EnsureCreated()
    {
        ThrowIfSchemaProvisioningUnsupported();
        if (_duckLakeOptions is null)
        {
            return base.EnsureCreated();
        }

        // A DuckLake read-only probe cannot migrate an older catalog. Open the configured profile first so
        // CREATE_IF_NOT_EXISTS and AUTOMATIC_MIGRATION run, then let EF inspect the catalog's existing tables.
        Create();
        _duckLakeCatalogReadyForEnsureCreated = true;

        try
        {
            return base.EnsureCreated();
        }
        finally
        {
            _duckLakeCatalogReadyForEnsureCreated = false;
        }
    }

    /// <inheritdoc />
    public override async Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfSchemaProvisioningUnsupported();
        if (_duckLakeOptions is null)
        {
            return await base.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        }

        await CreateAsync(cancellationToken).ConfigureAwait(false);
        _duckLakeCatalogReadyForEnsureCreated = true;

        try
        {
            return await base.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _duckLakeCatalogReadyForEnsureCreated = false;
        }
    }

    /// <inheritdoc />
    public override bool Exists()
    {
        // An encrypted database is a local file that ATTACH creates on demand, so its presence is the whole
        // answer. Probing by attaching would create the file the caller is asking about.
        if (_encryptedDatabase is not null)
        {
            return File.Exists(_encryptedDatabase.Path);
        }

        if (_duckLakeOptions is not null)
        {
            if (_duckLakeCatalogReadyForEnsureCreated)
            {
                return true;
            }

            using var probeConnection = _connection.CreateReadOnlyConnection();

            try
            {
                probeConnection.Open(errorsExpected: true);
            }
            catch (DuckDBException)
            {
                return false;
            }

            return true;
        }

        if (_connection.DbConnection is not DuckDBConnection)
        {
            return ExistsUsingConnection();
        }

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
    public override async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        if (_encryptedDatabase is not null)
        {
            return File.Exists(_encryptedDatabase.Path);
        }

        if (_duckLakeOptions is null)
        {
            return await base.ExistsAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_duckLakeCatalogReadyForEnsureCreated)
        {
            return true;
        }

        await using var probeConnection = _connection.CreateReadOnlyConnection();

        try
        {
            await probeConnection.OpenAsync(cancellationToken, errorsExpected: true).ConfigureAwait(false);
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
        // Attached-catalog profiles share the connection with the in-memory host and, for an encrypted
        // database, with any other context in the same DuckDB instance, so the search is scoped to the catalog
        // this context maps entities to.
        var catalogName = _duckLakeOptions?.CatalogName ?? _encryptedDatabase?.CatalogName;
        var databasePredicate = catalogName is null
            ? string.Empty
            : " AND database_name = $database_name";
        using var parameterFactoryCommand = Dependencies.Connection.DbConnection.CreateCommand();
        var parameters = new List<DbParameter>
        {
            parameterFactoryCommand.CreateParameter("default_table_name", HistoryRepository.DefaultTableName)
        };

        if (catalogName is not null)
        {
            parameters.Add(parameterFactoryCommand.CreateParameter("database_name", catalogName));
        }

        return (bool)_rawSqlCommandBuilder
            .Build(
                $$"""
                SELECT coalesce(any_value(true), false)
                  FROM duckdb_tables()
                 WHERE table_name != $default_table_name
                {{databasePredicate}}
                """,
                parameters).RelationalCommand.ExecuteScalar(new RelationalCommandParameterObject(
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
    public override async Task CreateAsync(CancellationToken cancellationToken = default)
    {
        await Dependencies.Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await Dependencies.Connection.CloseAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override bool EnsureDeleted()
    {
        ThrowIfDatabaseDeletionUnsupported();
        return base.EnsureDeleted();
    }

    /// <inheritdoc />
    public override async Task<bool> EnsureDeletedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDatabaseDeletionUnsupported();
        return await base.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void CreateTables()
    {
        base.CreateTables();

        // Create the tiered-storage control table and union views alongside the hot tables, so a fresh
        // EnsureCreated() yields a queryable tiered store without a separate EnsureTieredStoresCreated() call.
        // No-op when the model has no tiered entities.
        if (_duckLakeOptions is null)
        {
            Dependencies.CurrentContext.Context.Database.EnsureTieredStoresCreated();
        }
    }

    /// <inheritdoc />
    public override async Task CreateTablesAsync(CancellationToken cancellationToken = default)
    {
        await base.CreateTablesAsync(cancellationToken).ConfigureAwait(false);

        if (_duckLakeOptions is null)
        {
            Dependencies.CurrentContext.Context.Database.EnsureTieredStoresCreated();
        }
    }

    /// <inheritdoc />
    public override void Delete()
    {
        ThrowIfDatabaseDeletionUnsupported();

        if (_encryptedDatabase is not null)
        {
            DeleteEncryptedDatabase();
            return;
        }

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

    /// <summary>
    ///     Detaches the encrypted database from the host instance before deleting it. The instance is shared,
    ///     so it can outlive this context's connections whenever another context holds one open, and the
    ///     attached file would otherwise stay open and, on Windows, locked. The detach goes through the
    ///     provider connection so its attachment cache is invalidated with it.
    /// </summary>
    private void DeleteEncryptedDatabase()
    {
        Dependencies.Connection.Open();
        try
        {
            _connection.DetachEncryptedDatabase();
        }
        finally
        {
            Dependencies.Connection.Close();
        }

        File.Delete(_encryptedDatabase!.Path);

        // DuckDB checkpoints into the database file and removes the write-ahead log on detach, but a crashed
        // process can leave one behind; it holds the same data and must not outlive the database.
        var writeAheadLog = _encryptedDatabase.Path + ".wal";
        if (File.Exists(writeAheadLog))
        {
            File.Delete(writeAheadLog);
        }
    }

    private void ThrowIfSchemaProvisioningUnsupported()
    {
        if (!_supportsSchemaManagement)
        {
            throw new NotSupportedException(
                "Database.EnsureCreated is not supported by the configured DuckDB engine capabilities.");
        }
    }

    private void ThrowIfDatabaseDeletionUnsupported()
    {
        if (_duckLakeOptions is not null)
        {
            throw new NotSupportedException(
                "Database.EnsureDeleted() is intentionally disabled for DuckLake because a catalog may reference shared or "
                + "remote metadata and object storage. Delete the catalog and data path explicitly with storage-specific tooling.");
        }

        if (!_supportsDatabaseDeletion)
        {
            throw new NotSupportedException(
                "Database.EnsureDeleted() is intentionally disabled for the configured remote profile because the database "
                + "is owned by the server. Delete it explicitly on the server.");
        }
    }

    private bool ExistsUsingConnection()
    {
        var opened = false;
        try
        {
            opened = _connection.Open(errorsExpected: true);
            return true;
        }
        catch (DbException)
        {
            return false;
        }
        finally
        {
            if (opened)
            {
                _connection.Close();
            }
        }
    }
}
