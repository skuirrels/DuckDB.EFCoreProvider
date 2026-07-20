using DuckDB.EFCoreProvider.Diagnostics.Internal;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Text;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBRelationalConnection : RelationalConnection, IDuckDBRelationalConnection
{
    private const string AccessModeConfigurationKey = "access_mode";
    private const string ReadOnlyAccessMode = "READ_ONLY";

    private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;
    private readonly IDiagnosticsLogger<DbLoggerCategory.Infrastructure> _logger;
    private readonly DbContext _context;
    private readonly bool _loadSpatial;
    private readonly string? _memoryLimit;
    private readonly string? _fileSearchPath;
    private readonly IReadOnlyList<DuckDBExtensionConfiguration> _configuredExtensions;
    private readonly Action<DuckDBConnection>? _connectionInitializer;
    private readonly DuckLakeOptions? _duckLakeOptions;
    private DuckDBConnection? _initializedDuckLakeConnection;
    private DuckDBConnection? _initializingDuckLakeConnection;
    private DuckDBConnection? _observedDuckLakeConnection;

    public DuckDBRelationalConnection(
        RelationalConnectionDependencies dependencies,
        IRawSqlCommandBuilder rawSqlCommandBuilder,
        IDiagnosticsLogger<DbLoggerCategory.Infrastructure> logger)
        : base(dependencies)
    {
        _rawSqlCommandBuilder = rawSqlCommandBuilder;
        _logger = logger;
        _context = dependencies.CurrentContext.Context;

        var optionsExtension = dependencies.ContextOptions.FindExtension<DuckDBOptionsExtension>();
        _loadSpatial = optionsExtension?.LoadSpatialite == true;
        _memoryLimit = optionsExtension?.MemoryLimit;
        _fileSearchPath = optionsExtension?.FileSearchPath;
        _configuredExtensions = optionsExtension?.ConfiguredExtensions ?? [];
        _connectionInitializer = optionsExtension?.ConnectionInitializer;
        _duckLakeOptions = optionsExtension?.DuckLakeOptions;
    }

    // DuckDB.NET only supports IsolationLevel.Unspecified and IsolationLevel.Snapshot.
    // We expose IsolationLevel.Snapshot to callers so that EF Core's interception infrastructure
    // always sees a concrete isolation level instead of Unspecified.
    private const IsolationLevel DuckDBDefaultIsolationLevel = IsolationLevel.Snapshot;

    /// <inheritdoc />
    protected override DbConnection CreateDbConnection()
    {
        var connection = new DuckDBConnection(GetValidatedConnectionString());

        return connection;
    }

    /// <inheritdoc />
    public override bool Open(bool errorsExpected = false)
    {
        if (_duckLakeOptions is not null && DbConnection.State == ConnectionState.Open)
        {
            InitializeOpenConnection((DuckDBConnection)DbConnection);
        }

        return base.Open(errorsExpected);
    }

    /// <inheritdoc />
    public override async Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
    {
        if (_duckLakeOptions is not null && DbConnection.State == ConnectionState.Open)
        {
            await InitializeOpenConnectionAsync((DuckDBConnection)DbConnection, cancellationToken).ConfigureAwait(false);
        }

        return await base.OpenAsync(cancellationToken, errorsExpected).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Overrides the no-arg overload so that EF Core's interception pipeline sees
    ///     <see cref="IsolationLevel.Snapshot" /> (DuckDB's actual isolation level) instead of
    ///     <see cref="IsolationLevel.Unspecified" /> in the event data.
    /// </remarks>
    public override IDbContextTransaction BeginTransaction()
        => BeginTransaction(DuckDBDefaultIsolationLevel);

    /// <inheritdoc />
    public override Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => BeginTransactionAsync(DuckDBDefaultIsolationLevel, cancellationToken);

    /// <inheritdoc />
    protected override DbTransaction ConnectionBeginTransaction(IsolationLevel isolationLevel)
    {
        // DuckDB.NET only accepts Unspecified and Snapshot; map unsupported levels to Unspecified.
        var driverLevel = ToDuckDBIsolationLevel(isolationLevel);
        var transaction = base.ConnectionBeginTransaction(driverLevel);

        return new DuckDBDbTransactionWrapper(transaction, isolationLevel);
    }

    /// <inheritdoc />
    protected override async ValueTask<DbTransaction> ConnectionBeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default)
    {
        // DuckDB.NET only accepts Unspecified and Snapshot; map unsupported levels to Unspecified.
        var driverLevel = ToDuckDBIsolationLevel(isolationLevel);
        var transaction = await base.ConnectionBeginTransactionAsync(driverLevel, cancellationToken);

        return new DuckDBDbTransactionWrapper(transaction, isolationLevel);
    }

    /// <summary>
    ///     Maps any <see cref="IsolationLevel" /> to one that DuckDB.NET accepts
    ///     (<see cref="IsolationLevel.Unspecified" /> or <see cref="IsolationLevel.Snapshot" />).
    ///     Unsupported levels fall back to <see cref="IsolationLevel.Unspecified" />.
    /// </summary>
    private static IsolationLevel ToDuckDBIsolationLevel(IsolationLevel isolationLevel)
        => isolationLevel is IsolationLevel.Unspecified or IsolationLevel.Snapshot
            ? isolationLevel
            : IsolationLevel.Unspecified;

    /// <summary>
    ///     Wraps a <see cref="DbTransaction" /> to expose a concrete <see cref="IsolationLevel" /> because
    ///     DuckDB.NET reports <see cref="IsolationLevel.Unspecified" /> for all transactions.
    /// </summary>
    private sealed class DuckDBDbTransactionWrapper(DbTransaction inner, IsolationLevel isolationLevel) : DbTransaction
    {
        public override IsolationLevel IsolationLevel { get; } = isolationLevel;
        protected override DbConnection DbConnection => inner.Connection!;
        public override void Commit() => inner.Commit();
        public override void Rollback() => inner.Rollback();
        public override Task CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);
        public override Task RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    public virtual IDuckDBRelationalConnection CreateReadOnlyConnection()
    {
        var connectionStringBuilder = new DuckDBConnectionStringBuilder()
        {
            ConnectionString = GetValidatedConnectionString()
        };

        if (_duckLakeOptions is null)
        {
            connectionStringBuilder[AccessModeConfigurationKey] = ReadOnlyAccessMode;
        }

        var contextOptions = new DbContextOptionsBuilder().UseDuckDB(
            connectionStringBuilder.ToString(),
            options =>
            {
                if (_memoryLimit is not null) options.MemoryLimit(_memoryLimit);
                if (_fileSearchPath is not null) options.FileSearchPath(_fileSearchPath);
                foreach (var extension in _configuredExtensions)
                {
                    options.LoadExtension(extension.Name, extension.Mode);
                }
                if (_connectionInitializer is not null) options.ConfigureConnection(_connectionInitializer);
                if (_duckLakeOptions is not null)
                {
                    var readOnlyProfile = _duckLakeOptions.AsReadOnly();
                    options.UseDuckLake(duckLake =>
                    {
                        if (readOnlyProfile.UsesSecret && readOnlyProfile.MetadataSource!.Length == 0)
                        {
                            duckLake.UseDefaultSecret();
                        }
                        else if (readOnlyProfile.UsesSecret)
                        {
                            duckLake.UseNamedSecret(readOnlyProfile.MetadataSource!);
                        }
                        else
                        {
                            duckLake.UseLocalMetadata(readOnlyProfile.MetadataSource!);
                        }

                        duckLake.CatalogName(readOnlyProfile.CatalogName);
                        if (readOnlyProfile.DataPath is not null)
                        {
                            duckLake.DataPath(readOnlyProfile.DataPath, readOnlyProfile.OverrideDataPath);
                        }

                        duckLake.ReadOnly();
                        if (readOnlyProfile.SnapshotVersion is not null)
                        {
                            duckLake.AsOfSnapshot(readOnlyProfile.SnapshotVersion.Value);
                        }
                        else if (readOnlyProfile.SnapshotTime is not null)
                        {
                            duckLake.AsOfTimestamp(readOnlyProfile.SnapshotTime.Value);
                        }

                        foreach (var additionalCatalog in readOnlyProfile.AdditionalCatalogs)
                        {
                            duckLake.AlsoAttach(
                                additionalCatalog.CatalogName,
                                additionalCatalog.MetadataSource!,
                                readOnly: true);
                        }
                    });
                }
            }).Options;

        return new DuckDBRelationalConnection(Dependencies with { ContextOptions = contextOptions }, _rawSqlCommandBuilder, _logger);
    }

    protected override void CloseDbConnection()
    {
        var connection = (DuckDBConnection)DbConnection;

        if (connection.State != ConnectionState.Closed)
        {
            connection.Close();
        }
    }

    protected override async Task CloseDbConnectionAsync()
    {
        var connection = (DuckDBConnection)DbConnection;

        if (connection.State != ConnectionState.Closed)
        {
            await connection.CloseAsync();
        }
    }

    protected override void OpenDbConnection(bool errorsExpected)
    {
        var connection = (DuckDBConnection)DbConnection;

        connection.Open();
        try
        {
            InitializeOpenConnection(connection);
        }
        catch
        {
            connection.Close();
            throw;
        }
    }

    protected override async Task OpenDbConnectionAsync(bool errorsExpected, CancellationToken cancellationToken)
    {
        var connection = (DuckDBConnection)DbConnection;

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeOpenConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void InitializeOpenConnection(DuckDBConnection connection)
    {
        if (_duckLakeOptions is not null)
        {
            ObserveDuckLakeConnection(connection);
            if (ReferenceEquals(_initializedDuckLakeConnection, connection)
                || ReferenceEquals(_initializingDuckLakeConnection, connection))
            {
                return;
            }

            _initializingDuckLakeConnection = connection;
        }

        try
        {
            ApplyConfigurationIfNeeded();
            LoadSpatialExtensionIfNeeded();
            LoadConfiguredExtensions();
            _connectionInitializer?.Invoke(connection);
            AttachOrSelectDuckLakeCatalog();

            if (_duckLakeOptions is not null)
            {
                _initializedDuckLakeConnection = connection;
            }
        }
        finally
        {
            if (ReferenceEquals(_initializingDuckLakeConnection, connection))
            {
                _initializingDuckLakeConnection = null;
            }
        }
    }

    private async Task InitializeOpenConnectionAsync(
        DuckDBConnection connection,
        CancellationToken cancellationToken)
    {
        if (_duckLakeOptions is not null)
        {
            ObserveDuckLakeConnection(connection);
            if (ReferenceEquals(_initializedDuckLakeConnection, connection)
                || ReferenceEquals(_initializingDuckLakeConnection, connection))
            {
                return;
            }

            _initializingDuckLakeConnection = connection;
        }

        try
        {
            await ApplyConfigurationIfNeededAsync(cancellationToken).ConfigureAwait(false);
            await LoadSpatialExtensionIfNeededAsync(cancellationToken).ConfigureAwait(false);
            await LoadConfiguredExtensionsAsync(cancellationToken).ConfigureAwait(false);
            _connectionInitializer?.Invoke(connection);
            await AttachOrSelectDuckLakeCatalogAsync(cancellationToken).ConfigureAwait(false);

            if (_duckLakeOptions is not null)
            {
                _initializedDuckLakeConnection = connection;
            }
        }
        finally
        {
            if (ReferenceEquals(_initializingDuckLakeConnection, connection))
            {
                _initializingDuckLakeConnection = null;
            }
        }
    }

    private void ApplyConfigurationIfNeeded()
    {
        var statements = BuildConfigurationStatements();
        if (statements.Count == 0)
        {
            return;
        }

        var paramObj = new RelationalCommandParameterObject(this, null, null, null, null);
        foreach (var sql in statements)
        {
            _rawSqlCommandBuilder.Build(sql).ExecuteNonQuery(paramObj);
        }
    }

    private async Task ApplyConfigurationIfNeededAsync(CancellationToken cancellationToken)
    {
        var statements = BuildConfigurationStatements();
        if (statements.Count == 0)
        {
            return;
        }

        var paramObj = new RelationalCommandParameterObject(this, null, null, null, null);
        foreach (var sql in statements)
        {
            await _rawSqlCommandBuilder.Build(sql).ExecuteNonQueryAsync(paramObj, cancellationToken);
        }
    }

    // DuckDB settings applied on connection open. They are developer-supplied configuration, but the string
    // literals are escaped defensively. Values are global DuckDB settings, so applying them on open configures
    // the database instance.
    private List<string> BuildConfigurationStatements()
    {
        var statements = new List<string>();

        if (!string.IsNullOrWhiteSpace(_memoryLimit))
        {
            statements.Add($"SET memory_limit = '{_memoryLimit.Replace("'", "''")}'");
        }

        if (!string.IsNullOrWhiteSpace(_fileSearchPath))
        {
            statements.Add($"SET file_search_path = '{_fileSearchPath.Replace("'", "''")}'");
        }

        return statements;
    }

    private void LoadSpatialExtensionIfNeeded()
    {
        if (!_loadSpatial)
        {
            return;
        }

        var paramObj = new RelationalCommandParameterObject(this, null, null, null, null);
        _rawSqlCommandBuilder.Build("INSTALL spatial").ExecuteNonQuery(paramObj);
        _rawSqlCommandBuilder.Build("LOAD spatial").ExecuteNonQuery(paramObj);
    }

    private async Task LoadSpatialExtensionIfNeededAsync(CancellationToken cancellationToken)
    {
        if (!_loadSpatial)
        {
            return;
        }

        var paramObj = new RelationalCommandParameterObject(this, null, null, null, null);
        await _rawSqlCommandBuilder.Build("INSTALL spatial").ExecuteNonQueryAsync(paramObj, cancellationToken);
        await _rawSqlCommandBuilder.Build("LOAD spatial").ExecuteNonQueryAsync(paramObj, cancellationToken);
    }

    private void LoadConfiguredExtensions()
    {
        foreach (var extension in _configuredExtensions)
        {
            if (extension.Mode == DuckDBExtensionLoadMode.CallerManaged)
            {
                continue;
            }

            var operation = DuckDBOperationScope<DbLoggerCategory.Infrastructure>.Start(
                _logger,
                _context,
                DuckDBProviderOperation.ExtensionLoad,
                "ExtensionLoad",
                extension.Name);

            try
            {
                using var command = DbConnection.CreateCommand();
                command.CommandText = extension.Mode == DuckDBExtensionLoadMode.LoadOnly
                    ? $"LOAD {extension.Name};"
                    : $"INSTALL {extension.Name}; LOAD {extension.Name};";
                command.ExecuteNonQuery();
            }
            catch (Exception exception)
            {
                operation.Fail(exception);
                throw;
            }

            operation.Complete();
        }
    }

    private async Task LoadConfiguredExtensionsAsync(CancellationToken cancellationToken)
    {
        foreach (var extension in _configuredExtensions)
        {
            if (extension.Mode == DuckDBExtensionLoadMode.CallerManaged)
            {
                continue;
            }

            var operation = DuckDBOperationScope<DbLoggerCategory.Infrastructure>.Start(
                _logger,
                _context,
                DuckDBProviderOperation.ExtensionLoad,
                "ExtensionLoad",
                extension.Name);

            try
            {
                await using var command = DbConnection.CreateCommand();
                command.CommandText = extension.Mode == DuckDBExtensionLoadMode.LoadOnly
                    ? $"LOAD {extension.Name};"
                    : $"INSTALL {extension.Name}; LOAD {extension.Name};";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                operation.Fail(exception);
                throw;
            }

            operation.Complete();
        }
    }

    private void AttachOrSelectDuckLakeCatalog()
    {
        if (_duckLakeOptions is null)
        {
            return;
        }

        var operation = DuckDBOperationScope<DbLoggerCategory.Infrastructure>.Start(
            _logger,
            _context,
            DuckDBProviderOperation.DuckLakeAttachment,
            "DuckLakeAttachment",
            _duckLakeOptions.CatalogName);

        try
        {
            using var command = DbConnection.CreateCommand();
            var commandText = new StringBuilder();
            foreach (var profile in _duckLakeOptions.AdditionalCatalogs.Prepend(_duckLakeOptions))
            {
                var attachedDatabase = GetAttachedDatabase(profile.CatalogName);
                EnsureCompatibleAttachedDatabase(profile, attachedDatabase);
                if (attachedDatabase is null)
                {
                    commandText.Append(DuckLakeAttachCommandBuilder.BuildAttachment(profile)).Append(' ');
                }
            }

            commandText.Append(DuckLakeAttachCommandBuilder.BuildUse(_duckLakeOptions));
            command.CommandText = commandText.ToString();
            command.ExecuteNonQuery();
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }

        operation.Complete();
    }

    private async Task AttachOrSelectDuckLakeCatalogAsync(CancellationToken cancellationToken)
    {
        if (_duckLakeOptions is null)
        {
            return;
        }

        var operation = DuckDBOperationScope<DbLoggerCategory.Infrastructure>.Start(
            _logger,
            _context,
            DuckDBProviderOperation.DuckLakeAttachment,
            "DuckLakeAttachment",
            _duckLakeOptions.CatalogName);

        try
        {
            await using var command = DbConnection.CreateCommand();
            var commandText = new StringBuilder();
            foreach (var profile in _duckLakeOptions.AdditionalCatalogs.Prepend(_duckLakeOptions))
            {
                var attachedDatabase = await GetAttachedDatabaseAsync(profile.CatalogName, cancellationToken)
                    .ConfigureAwait(false);
                EnsureCompatibleAttachedDatabase(profile, attachedDatabase);
                if (attachedDatabase is null)
                {
                    commandText.Append(DuckLakeAttachCommandBuilder.BuildAttachment(profile)).Append(' ');
                }
            }

            commandText.Append(DuckLakeAttachCommandBuilder.BuildUse(_duckLakeOptions));
            command.CommandText = commandText.ToString();
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }

        operation.Complete();
    }

    private AttachedDatabase? GetAttachedDatabase(string catalogName)
    {
        using var command = DbConnection.CreateCommand();
        command.CommandText =
            "SELECT type, path, readonly FROM duckdb_databases() WHERE database_name = $catalog_name LIMIT 1;";
        command.Parameters.Add(new DuckDBParameter("catalog_name", catalogName));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new AttachedDatabase(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetBoolean(2))
            : null;
    }

    private async Task<AttachedDatabase?> GetAttachedDatabaseAsync(
        string catalogName,
        CancellationToken cancellationToken)
    {
        await using var command = DbConnection.CreateCommand();
        command.CommandText =
            "SELECT type, path, readonly FROM duckdb_databases() WHERE database_name = $catalog_name LIMIT 1;";
        command.Parameters.Add(new DuckDBParameter("catalog_name", catalogName));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new AttachedDatabase(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetBoolean(2))
            : null;
    }

    private static void EnsureCompatibleAttachedDatabase(DuckLakeOptions profile, AttachedDatabase? attachedDatabase)
    {
        if (attachedDatabase is null)
        {
            return;
        }

        if (!attachedDatabase.Type.Equals("ducklake", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Database alias '{profile.CatalogName}' is already attached as type "
                + $"'{attachedDatabase.Type}' and cannot be used for the DuckLake profile.");
        }

        if (profile.UsesSecret)
        {
            throw new InvalidOperationException(
                $"Database alias '{profile.CatalogName}' is already attached, but its metadata source cannot be "
                + "verified against a DuckLake named-secret profile. Use a fresh connection so the provider can "
                + "attach the configured catalog.");
        }

        if (attachedDatabase.Path is null || profile.MetadataSource is null
            || !PathsEqual(attachedDatabase.Path, profile.MetadataSource))
        {
            throw new InvalidOperationException(
                $"Database alias '{profile.CatalogName}' is already attached to a different DuckLake metadata source.");
        }

        if (attachedDatabase.IsReadOnly != profile.IsReadOnly)
        {
            var configuredMode = profile.IsReadOnly ? "read-only" : "writable";
            var attachedMode = attachedDatabase.IsReadOnly ? "read-only" : "writable";
            throw new InvalidOperationException(
                $"Database alias '{profile.CatalogName}' is already attached as {attachedMode}, but the DuckLake "
                + $"profile requires a {configuredMode} attachment.");
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record AttachedDatabase(string Type, string? Path, bool IsReadOnly);

    private void ObserveDuckLakeConnection(DuckDBConnection connection)
    {
        if (ReferenceEquals(_observedDuckLakeConnection, connection))
        {
            return;
        }

        StopObservingDuckLakeConnection();
        _observedDuckLakeConnection = connection;
        _observedDuckLakeConnection.StateChange += DuckLakeConnectionStateChanged;
    }

    private void DuckLakeConnectionStateChanged(object? sender, StateChangeEventArgs eventArgs)
    {
        if (eventArgs.CurrentState != ConnectionState.Open
            && ReferenceEquals(sender, _initializedDuckLakeConnection))
        {
            _initializedDuckLakeConnection = null;
        }
    }

    private void StopObservingDuckLakeConnection()
    {
        if (_observedDuckLakeConnection is not null)
        {
            _observedDuckLakeConnection.StateChange -= DuckLakeConnectionStateChanged;
        }

        _observedDuckLakeConnection = null;
        _initializedDuckLakeConnection = null;
        _initializingDuckLakeConnection = null;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        try
        {
            base.Dispose();
        }
        finally
        {
            StopObservingDuckLakeConnection();
        }
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            StopObservingDuckLakeConnection();
        }
    }
}