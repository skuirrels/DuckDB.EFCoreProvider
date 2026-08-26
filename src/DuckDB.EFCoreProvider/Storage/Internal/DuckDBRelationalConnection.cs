using DuckDB.EFCoreProvider.Diagnostics.Internal;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
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

    /// <summary>Bounds the symbolic-link hops taken while comparing attached database paths.</summary>
    private const int MaximumLinkDepth = 64;

    /// <summary>
    ///     Fingerprints of the keys the provider attached encrypted databases with, by canonical file path.
    ///     DuckDB cannot check a key against a live attachment — the file handle is unique per instance, so a
    ///     probing re-attach is impossible — and without this a context whose key is wrong or rotated away
    ///     would silently inherit full access from whichever context attached the database first.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte[]> AttachedKeyFingerprints =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private const string ReadOnlyAccessMode = "READ_ONLY";

    private readonly IRawSqlCommandBuilder _rawSqlCommandBuilder;
    private readonly IDiagnosticsLogger<DbLoggerCategory.Infrastructure> _logger;
    private readonly DbContext _context;
    private readonly bool _loadSpatial;
    private readonly string? _memoryLimit;
    private readonly int? _threads;
    private readonly string? _checkpointThreshold;
    private readonly string? _fileSearchPath;
    private readonly IReadOnlyList<DuckDBExtensionConfiguration> _configuredExtensions;
    private readonly Action<DuckDBConnection>? _connectionInitializer;
    private readonly DuckDBEncryptedDatabaseOptions? _encryptedDatabase;
    private readonly DuckLakeOptions? _duckLakeOptions;
    private readonly QuackOptions? _quackOptions;
    private readonly IDuckDBEngineCapabilities _engineCapabilities;
    private readonly DbProviderFactory _providerFactory;
    private DuckDBConnection? _initializedCatalogConnection;
    private DuckDBConnection? _initializingCatalogConnection;
    private DuckDBConnection? _observedCatalogConnection;

    public DuckDBRelationalConnection(
        RelationalConnectionDependencies dependencies,
        IRawSqlCommandBuilder rawSqlCommandBuilder,
        IDiagnosticsLogger<DbLoggerCategory.Infrastructure> logger,
        DbProviderFactory providerFactory)
        : this(
            dependencies,
            rawSqlCommandBuilder,
            logger,
            DuckDBEngineCapabilities.FromOptions(dependencies.ContextOptions),
            providerFactory)
    {
    }

    public DuckDBRelationalConnection(
        RelationalConnectionDependencies dependencies,
        IRawSqlCommandBuilder rawSqlCommandBuilder,
        IDiagnosticsLogger<DbLoggerCategory.Infrastructure> logger,
        IDuckDBEngineCapabilities engineCapabilities,
        DbProviderFactory providerFactory)
        : base(dependencies)
    {
        _rawSqlCommandBuilder = rawSqlCommandBuilder;
        _logger = logger;
        _context = dependencies.CurrentContext.Context;
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));

        var optionsExtension = dependencies.ContextOptions.FindExtension<DuckDBOptionsExtension>();
        _loadSpatial = optionsExtension?.LoadSpatialite == true;
        _memoryLimit = optionsExtension?.MemoryLimit;
        _threads = optionsExtension?.Threads;
        _checkpointThreshold = optionsExtension?.CheckpointThreshold;
        _fileSearchPath = optionsExtension?.FileSearchPath;
        _configuredExtensions = optionsExtension?.ConfiguredExtensions ?? [];
        _connectionInitializer = optionsExtension?.ConnectionInitializer;
        _encryptedDatabase = optionsExtension?.EncryptedDatabase;
        _duckLakeOptions = optionsExtension?.DuckLakeOptions;
        _quackOptions = optionsExtension?.QuackOptions;
        _engineCapabilities = engineCapabilities ?? throw new ArgumentNullException(nameof(engineCapabilities));
    }

    /// <summary>
    ///     <see langword="true" /> for the profiles whose data lives in a catalog attached to the connection
    ///     rather than in the connection's own data source. They need initialization to run for a connection the
    ///     caller opened, and to run only once per open connection because it invokes the caller's initializer.
    /// </summary>
    private bool UsesAttachedCatalog => _duckLakeOptions is not null || _encryptedDatabase is not null;

    // DuckDB.NET only supports IsolationLevel.Unspecified and IsolationLevel.Snapshot.
    // We expose IsolationLevel.Snapshot to callers so that EF Core's interception infrastructure
    // always sees a concrete isolation level instead of Unspecified.
    private const IsolationLevel DuckDBDefaultIsolationLevel = IsolationLevel.Snapshot;

    /// <inheritdoc />
    protected override DbConnection CreateDbConnection()
    {
        if (_quackOptions is not null)
        {
            return new QuackDbConnection(GetValidatedConnectionString(), _quackOptions, _engineCapabilities);
        }

        var connection = _providerFactory.CreateConnection()
            ?? throw new InvalidOperationException(
                $"{_providerFactory.GetType().Name}.CreateConnection() returned null.");
        connection.ConnectionString = GetValidatedConnectionString();
        return connection;
    }

    /// <inheritdoc />
    public override bool Open(bool errorsExpected = false)
    {
        // A caller that opened the underlying connection itself never reaches OpenDbConnection, so the catalog
        // would otherwise stay unattached and the context would silently run against the empty host database.
        if (UsesAttachedCatalog && DbConnection.State == ConnectionState.Open)
        {
            InitializeOpenConnection((DuckDBConnection)DbConnection);
        }

        return base.Open(errorsExpected);
    }

    /// <inheritdoc />
    public override async Task<bool> OpenAsync(CancellationToken cancellationToken, bool errorsExpected = false)
    {
        if (UsesAttachedCatalog && DbConnection.State == ConnectionState.Open)
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
        if (_encryptedDatabase is not null)
        {
            throw new NotSupportedException(
                "An encrypted database cannot back an independently enforced read-only connection. Its access "
                + "mode belongs to the attachment, which every connection on the shared DuckDB host instance "
                + "sees, so a clone can neither re-attach it read-only while it is attached writable nor stop "
                + "the writable context from using it. Configure a separate read-only context with "
                + "UseEncryptedDatabase(..., encrypted => encrypted.ReadOnly()) instead.");
        }

        if (_quackOptions is not null)
        {
            throw new NotSupportedException(
                "A Quack profile cannot create an independently enforced read-only connection. "
                + "Use a server-side read-only authorization policy and a separate UseQuack context.");
        }

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
                if (_threads is not null) options.Threads(_threads.Value);
                if (_checkpointThreshold is not null) options.CheckpointThreshold(_checkpointThreshold);
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
                            if (additionalCatalog.UsesSecret)
                            {
                                duckLake.AlsoAttachNamedSecret(
                                    additionalCatalog.CatalogName,
                                    additionalCatalog.MetadataSource!,
                                    readOnly: true);
                            }
                            else
                            {
                                duckLake.AlsoAttach(
                                    additionalCatalog.CatalogName,
                                    additionalCatalog.MetadataSource!,
                                    readOnly: true);
                            }
                        }
                    });
                }
            }).Options;

        return new DuckDBRelationalConnection(Dependencies with { ContextOptions = contextOptions }, _rawSqlCommandBuilder, _logger, _providerFactory);
    }

    protected override void CloseDbConnection()
    {
        var connection = DbConnection;

        if (connection.State != ConnectionState.Closed)
        {
            connection.Close();
        }
    }

    protected override async Task CloseDbConnectionAsync()
    {
        var connection = DbConnection;

        if (connection.State != ConnectionState.Closed)
        {
            await connection.CloseAsync();
        }
    }

    protected override void OpenDbConnection(bool errorsExpected)
    {
        var connection = DbConnection;

        connection.Open();
        try
        {
            if (connection is DuckDBConnection duckDbConnection)
            {
                InitializeOpenConnection(duckDbConnection);
            }
            else
            {
                InitializeOpenQuackConnection();
            }
        }
        catch
        {
            connection.Close();
            throw;
        }
    }

    protected override async Task OpenDbConnectionAsync(bool errorsExpected, CancellationToken cancellationToken)
    {
        var connection = DbConnection;

        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (connection is DuckDBConnection duckDbConnection)
            {
                await InitializeOpenConnectionAsync(duckDbConnection, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await InitializeOpenQuackConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await connection.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void InitializeOpenQuackConnection()
    {
        ApplyConfigurationIfNeeded();
        LoadSpatialExtensionIfNeeded();
        LoadConfiguredExtensions();
    }

    private async Task InitializeOpenQuackConnectionAsync(CancellationToken cancellationToken)
    {
        await ApplyConfigurationIfNeededAsync(cancellationToken).ConfigureAwait(false);
        await LoadSpatialExtensionIfNeededAsync(cancellationToken).ConfigureAwait(false);
        await LoadConfiguredExtensionsAsync(cancellationToken).ConfigureAwait(false);
    }

    private void InitializeOpenConnection(DuckDBConnection connection)
    {
        if (UsesAttachedCatalog)
        {
            ObserveCatalogConnection(connection);
            if (ReferenceEquals(_initializedCatalogConnection, connection)
                || ReferenceEquals(_initializingCatalogConnection, connection))
            {
                return;
            }

            _initializingCatalogConnection = connection;
        }

        try
        {
            ApplyConfigurationIfNeeded();
            LoadSpatialExtensionIfNeeded();
            LoadConfiguredExtensions();
            AttachOrSelectEncryptedDatabase();
            _connectionInitializer?.Invoke(connection);
            AttachOrSelectDuckLakeCatalog();

            if (UsesAttachedCatalog)
            {
                _initializedCatalogConnection = connection;
            }
        }
        finally
        {
            if (ReferenceEquals(_initializingCatalogConnection, connection))
            {
                _initializingCatalogConnection = null;
            }
        }
    }

    private async Task InitializeOpenConnectionAsync(
        DuckDBConnection connection,
        CancellationToken cancellationToken)
    {
        if (UsesAttachedCatalog)
        {
            ObserveCatalogConnection(connection);
            if (ReferenceEquals(_initializedCatalogConnection, connection)
                || ReferenceEquals(_initializingCatalogConnection, connection))
            {
                return;
            }

            _initializingCatalogConnection = connection;
        }

        try
        {
            await ApplyConfigurationIfNeededAsync(cancellationToken).ConfigureAwait(false);
            await LoadSpatialExtensionIfNeededAsync(cancellationToken).ConfigureAwait(false);
            await LoadConfiguredExtensionsAsync(cancellationToken).ConfigureAwait(false);
            await AttachOrSelectEncryptedDatabaseAsync(cancellationToken).ConfigureAwait(false);
            _connectionInitializer?.Invoke(connection);
            await AttachOrSelectDuckLakeCatalogAsync(cancellationToken).ConfigureAwait(false);

            if (UsesAttachedCatalog)
            {
                _initializedCatalogConnection = connection;
            }
        }
        finally
        {
            if (ReferenceEquals(_initializingCatalogConnection, connection))
            {
                _initializingCatalogConnection = null;
            }
        }
    }

    private void ApplyConfigurationIfNeeded()
    {
        var commandText = BuildConfigurationCommandText(_memoryLimit, _threads, _fileSearchPath, _checkpointThreshold);
        if (commandText is null)
        {
            return;
        }

        var paramObj = new RelationalCommandParameterObject(this, null, null, null, null);
        _rawSqlCommandBuilder.Build(commandText).ExecuteNonQuery(paramObj);
    }

    private async Task ApplyConfigurationIfNeededAsync(CancellationToken cancellationToken)
    {
        var commandText = BuildConfigurationCommandText(_memoryLimit, _threads, _fileSearchPath, _checkpointThreshold);
        if (commandText is null)
        {
            return;
        }

        var paramObj = new RelationalCommandParameterObject(this, null, null, null, null);
        await _rawSqlCommandBuilder
            .Build(commandText)
            .ExecuteNonQueryAsync(paramObj, cancellationToken)
            .ConfigureAwait(false);
    }

    // DuckDB settings applied on connection open. They are developer-supplied configuration, but the string
    // literals are escaped defensively. Values are global DuckDB settings, so applying them on open configures
    // the database instance.
    internal static string? BuildConfigurationCommandText(
        string? memoryLimit,
        int? threads,
        string? fileSearchPath,
        string? checkpointThreshold = null)
    {
        if (string.IsNullOrWhiteSpace(memoryLimit)
            && threads is null
            && string.IsNullOrWhiteSpace(fileSearchPath)
            && string.IsNullOrWhiteSpace(checkpointThreshold))
        {
            return null;
        }

        var statements = new List<string>();

        if (!string.IsNullOrWhiteSpace(memoryLimit))
        {
            statements.Add($"SET memory_limit = '{memoryLimit.Replace("'", "''")}'");
        }

        if (threads is not null)
        {
            statements.Add($"SET threads = {threads.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(fileSearchPath))
        {
            statements.Add($"SET file_search_path = '{fileSearchPath.Replace("'", "''")}'");
        }

        if (!string.IsNullOrWhiteSpace(checkpointThreshold))
        {
            statements.Add($"SET checkpoint_threshold = '{checkpointThreshold.Replace("'", "''")}'");
        }

        return statements.Count == 0
            ? null
            : string.Join("; ", statements) + ";";
    }

    private void LoadSpatialExtensionIfNeeded()
    {
        if (!_loadSpatial)
        {
            return;
        }

        var paramObj = new RelationalCommandParameterObject(this, null, null, null, null);
        _rawSqlCommandBuilder.Build("INSTALL spatial; LOAD spatial;").ExecuteNonQuery(paramObj);
    }

    private async Task LoadSpatialExtensionIfNeededAsync(CancellationToken cancellationToken)
    {
        if (!_loadSpatial)
        {
            return;
        }

        var paramObj = new RelationalCommandParameterObject(this, null, null, null, null);
        await _rawSqlCommandBuilder
            .Build("INSTALL spatial; LOAD spatial;")
            .ExecuteNonQueryAsync(paramObj, cancellationToken)
            .ConfigureAwait(false);
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

    /// <inheritdoc />
    /// <remarks>
    ///     Detaching mutates catalog state that this connection caches: the once-per-open-connection guard and,
    ///     when the caller holds an outer open scope, the connection's current catalog. Both must be reset here —
    ///     the <see cref="DbConnection.StateChange" /> reset only fires when the connection physically closes, so
    ///     an <c>EnsureDeleted</c> under an open connection would otherwise leave the guard set and the next
    ///     open would silently run against the unencrypted in-memory host instead of re-attaching.
    /// </remarks>
    public virtual void DetachEncryptedDatabase()
    {
        if (_encryptedDatabase is null)
        {
            throw new InvalidOperationException("No encrypted database is configured for this connection.");
        }

        using (var command = DbConnection.CreateCommand())
        {
            command.CommandText = DuckDBEncryptedAttachCommandBuilder.BuildDetach(_encryptedDatabase);
            command.ExecuteNonQuery();
        }

        _initializedCatalogConnection = null;
        AttachedKeyFingerprints.TryRemove(ResolvePath(Path.GetFullPath(_encryptedDatabase.Path)), out _);
    }

    private void AttachOrSelectEncryptedDatabase()
    {
        if (_encryptedDatabase is null)
        {
            return;
        }

        var operation = DuckDBOperationScope<DbLoggerCategory.Infrastructure>.Start(
            _logger,
            _context,
            DuckDBProviderOperation.EncryptedDatabaseAttachment,
            "EncryptedDatabaseAttachment",
            _encryptedDatabase.CatalogName);

        var key = string.Empty;

        try
        {
            var attachedDatabase = GetAttachedDatabase(_encryptedDatabase.CatalogName);
            EnsureCompatibleEncryptedDatabase(_encryptedDatabase, attachedDatabase);

            key = _encryptedDatabase.ResolveKey();
            var canonicalPath = ResolvePath(Path.GetFullPath(_encryptedDatabase.Path));

            if (attachedDatabase is not null)
            {
                // The database is already attached, so this context's ATTACH becomes a no-op and its key would
                // never be checked. Prove the key against the fingerprint the attaching context recorded.
                VerifyKeyMatchesAttachment(canonicalPath, key);
            }
            else
            {
                // No attachment exists, so any recorded fingerprint is stale — e.g. the host instance died and
                // the file was re-encrypted with a rotated key before this attach.
                AttachedKeyFingerprints.TryRemove(canonicalPath, out _);
            }

            using (var command = DbConnection.CreateCommand())
            {
                command.CommandText = BuildEncryptedDatabaseCommandText(attachedDatabase is null, key);
                command.ExecuteNonQuery();
            }

            if (attachedDatabase is null)
            {
                // Nothing serializes the check above with the attachment itself, so a connection that raced
                // this one may have attached a different database under the alias. ATTACH IF NOT EXISTS
                // matches on the alias and would have silently kept theirs.
                EnsureCompatibleEncryptedDatabase(
                    _encryptedDatabase,
                    GetAttachedDatabase(_encryptedDatabase.CatalogName));
                RecordKeyFingerprint(canonicalPath, key);
            }
        }
        catch (Exception exception)
        {
            var sanitized = SanitizeEncryptedDatabaseFailure(exception, key);
            operation.Fail(sanitized);
            throw sanitized;
        }

        operation.Complete();
    }

    private async Task AttachOrSelectEncryptedDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_encryptedDatabase is null)
        {
            return;
        }

        var operation = DuckDBOperationScope<DbLoggerCategory.Infrastructure>.Start(
            _logger,
            _context,
            DuckDBProviderOperation.EncryptedDatabaseAttachment,
            "EncryptedDatabaseAttachment",
            _encryptedDatabase.CatalogName);

        var key = string.Empty;

        try
        {
            var attachedDatabase = await GetAttachedDatabaseAsync(_encryptedDatabase.CatalogName, cancellationToken)
                .ConfigureAwait(false);
            EnsureCompatibleEncryptedDatabase(_encryptedDatabase, attachedDatabase);

            key = _encryptedDatabase.ResolveKey();
            var canonicalPath = ResolvePath(Path.GetFullPath(_encryptedDatabase.Path));

            if (attachedDatabase is not null)
            {
                VerifyKeyMatchesAttachment(canonicalPath, key);
            }
            else
            {
                AttachedKeyFingerprints.TryRemove(canonicalPath, out _);
            }

            await using (var command = DbConnection.CreateCommand())
            {
                command.CommandText = BuildEncryptedDatabaseCommandText(attachedDatabase is null, key);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            if (attachedDatabase is null)
            {
                EnsureCompatibleEncryptedDatabase(
                    _encryptedDatabase,
                    await GetAttachedDatabaseAsync(_encryptedDatabase.CatalogName, cancellationToken)
                        .ConfigureAwait(false));
                RecordKeyFingerprint(canonicalPath, key);
            }
        }
        catch (Exception exception)
        {
            var sanitized = SanitizeEncryptedDatabaseFailure(exception, key);
            operation.Fail(sanitized);
            throw sanitized;
        }

        operation.Complete();
    }

    /// <summary>
    ///     Builds the statements that make the encrypted database usable on this connection: the temporary-file
    ///     setting, the attachment when the host instance does not already hold it, and the catalog selection.
    ///     The attachment is only needed once per DuckDB instance, but <c>USE</c> is per connection.
    /// </summary>
    private string BuildEncryptedDatabaseCommandText(bool attach, string key)
    {
        var commandText = new StringBuilder();

        if (_encryptedDatabase!.EncryptTemporaryFiles)
        {
            commandText.Append(DuckDBEncryptedAttachCommandBuilder.BuildTemporaryFileEncryption()).Append(' ');
        }

        if (attach)
        {
            commandText
                .Append(DuckDBEncryptedAttachCommandBuilder.BuildAttachment(_encryptedDatabase, key))
                .Append(' ');
        }

        return commandText.Append(DuckDBEncryptedAttachCommandBuilder.BuildUse(_encryptedDatabase)).ToString();
    }

    /// <summary>
    ///     Proves this context's key against the fingerprint recorded when the database was attached. A missing
    ///     entry means the attachment was made outside the provider (caller-issued SQL); there is nothing to
    ///     verify against, and rejecting it would break deployments that pre-attach with their own tooling.
    /// </summary>
    private void VerifyKeyMatchesAttachment(string canonicalPath, string key)
    {
        if (AttachedKeyFingerprints.TryGetValue(canonicalPath, out var recorded)
            && !CryptographicOperations.FixedTimeEquals(ComputeKeyFingerprint(key), recorded))
        {
            throw new InvalidOperationException(
                $"The encrypted database '{_encryptedDatabase!.CatalogName}' is attached with a different "
                + "encryption key than this context resolved. The attachment is shared by every context on the "
                + "DuckDB host instance, so a context whose key no longer matches must not inherit it. Align "
                + "the key providers, or rotate the database file to the new key.");
        }
    }

    /// <summary>
    ///     Records the attaching key's fingerprint. When a racing connection recorded first, this context's key
    ///     is verified against that record instead — failing closed if the two keys differ.
    /// </summary>
    private void RecordKeyFingerprint(string canonicalPath, string key)
    {
        var fingerprint = ComputeKeyFingerprint(key);
        if (!AttachedKeyFingerprints.TryAdd(canonicalPath, fingerprint))
        {
            VerifyKeyMatchesAttachment(canonicalPath, key);
        }
    }

    private static byte[] ComputeKeyFingerprint(string key)
        => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    /// <summary>
    ///     Returns the failure to report for an attachment. DuckDB quotes the failing statement in some parse and
    ///     binder errors, so the key literal the attachment wrote is redacted before the message is logged or
    ///     propagated. Only that literal is rewritten: replacing every occurrence of the key's characters would
    ///     corrupt unrelated text, since a short key also matches paths, aliases, and ordinary words. A message
    ///     that still contains the key outside the literal is dropped entirely rather than leaked, as is the
    ///     original exception whenever anything was redacted, because chaining it would carry the key along.
    /// </summary>
    internal static Exception SanitizeEncryptedDatabaseFailure(Exception exception, string key)
    {
        if (key.Length == 0)
        {
            return exception;
        }

        var redacted = exception.Message.Replace(
            DuckDBEncryptedAttachCommandBuilder.KeyLiteral(key),
            DuckDBEncryptedAttachCommandBuilder.KeyLiteral("***"),
            StringComparison.Ordinal);

        if (redacted.Contains(key, StringComparison.Ordinal))
        {
            return new InvalidOperationException(
                $"Attaching the encrypted database failed with {exception.GetType().Name}. Its message was "
                + "suppressed because it contains the encryption key outside the redacted attachment literal. "
                + "A longer, higher-entropy key avoids the incidental matches that cause this.");
        }

        return string.Equals(redacted, exception.Message, StringComparison.Ordinal)
            ? exception
            : new InvalidOperationException(
                $"Attaching the encrypted database failed with {exception.GetType().Name}: {redacted}");
    }

    private static void EnsureCompatibleEncryptedDatabase(
        DuckDBEncryptedDatabaseOptions options,
        AttachedDatabase? attachedDatabase)
    {
        if (attachedDatabase is null)
        {
            return;
        }

        if (attachedDatabase.Path is null || !PathsEqual(attachedDatabase.Path, options.Path))
        {
            throw new InvalidOperationException(
                $"Catalog alias '{options.CatalogName}' is already attached to a different database file. Contexts "
                + "in one process share a single DuckDB host instance, so give each encrypted database its own "
                + "alias with UseEncryptedDatabase(..., encrypted => encrypted.CatalogName(...)).");
        }

        if (!attachedDatabase.IsEncrypted)
        {
            throw new InvalidOperationException(
                $"The database attached as '{options.CatalogName}' is not encrypted. Attach an encrypted database "
                + "file, or create an encrypted copy of the existing one before configuring UseEncryptedDatabase.");
        }

        if (attachedDatabase.IsReadOnly != options.IsReadOnly)
        {
            var configuredMode = options.IsReadOnly ? "read-only" : "writable";
            var attachedMode = attachedDatabase.IsReadOnly ? "read-only" : "writable";
            throw new InvalidOperationException(
                $"The encrypted database attached as '{options.CatalogName}' is {attachedMode}, but this context "
                + $"requires a {configuredMode} attachment. The access mode belongs to the attachment, which is "
                + "shared by every context using the same DuckDB host instance and catalog alias.");
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
            "SELECT type, path, readonly, encrypted FROM duckdb_databases() WHERE database_name = $catalog_name LIMIT 1;";
        command.Parameters.Add(new DuckDBParameter("catalog_name", catalogName));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new AttachedDatabase(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3))
            : null;
    }

    private async Task<AttachedDatabase?> GetAttachedDatabaseAsync(
        string catalogName,
        CancellationToken cancellationToken)
    {
        await using var command = DbConnection.CreateCommand();
        command.CommandText =
            "SELECT type, path, readonly, encrypted FROM duckdb_databases() WHERE database_name = $catalog_name LIMIT 1;";
        command.Parameters.Add(new DuckDBParameter("catalog_name", catalogName));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? new AttachedDatabase(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3))
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
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var leftFull = Path.GetFullPath(left);
        var rightFull = Path.GetFullPath(right);

        // The textual comparison decides the common case without touching the filesystem, and keeps two
        // identical strings equal even when link resolution fails for one of them.
        return string.Equals(leftFull, rightFull, comparison)
            || string.Equals(ResolvePath(leftFull), ResolvePath(rightFull), comparison);
    }

    /// <summary>
    ///     Resolves a full path to the form DuckDB reports for an attached database. DuckDB canonicalizes
    ///     symbolic links in every segment, including the database file itself, which
    ///     <see cref="Path.GetFullPath(string)" /> does not — so a configured path through a linked directory
    ///     (macOS reaches its temporary directories through <c>/var</c>) or a linked file (a blue/green
    ///     <c>current.duckdb</c>) would otherwise compare unequal to the same file already attached.
    /// </summary>
    private static string ResolvePath(string fullPath)
    {
        var remainingLinkHops = MaximumLinkDepth;

        try
        {
            if (File.Exists(fullPath)
                && new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true) is { } fileTarget)
            {
                fullPath = fileTarget.FullName;
                remainingLinkHops--;
            }

            var directory = Path.GetDirectoryName(fullPath);
            return string.IsNullOrEmpty(directory)
                ? fullPath
                : Path.Combine(
                    ResolveDirectory(new DirectoryInfo(directory), ref remainingLinkHops),
                    Path.GetFileName(fullPath));
        }
        catch (IOException)
        {
            return fullPath;
        }
        catch (UnauthorizedAccessException)
        {
            return fullPath;
        }
    }

    /// <summary>
    ///     Resolves a directory and each of its parents, because <see cref="FileSystemInfo.ResolveLinkTarget" />
    ///     only follows a link in the final path segment. The budget is spent only on link hops — never on the
    ///     parent walk, which is bounded by the path's own depth — so a deep but link-free path always resolves
    ///     completely instead of returning a half-resolved prefix that would fail the comparison.
    /// </summary>
    private static string ResolveDirectory(DirectoryInfo directory, ref int remainingLinkHops)
    {
        if (remainingLinkHops > 0
            && directory.Exists
            && directory.ResolveLinkTarget(returnFinalTarget: true) is { } target)
        {
            remainingLinkHops--;
            return ResolveDirectory(new DirectoryInfo(target.FullName), ref remainingLinkHops);
        }

        return directory.Parent is { } parent
            ? Path.Combine(ResolveDirectory(parent, ref remainingLinkHops), directory.Name)
            : directory.FullName;
    }

    private sealed record AttachedDatabase(string Type, string? Path, bool IsReadOnly, bool IsEncrypted);

    private void ObserveCatalogConnection(DuckDBConnection connection)
    {
        if (ReferenceEquals(_observedCatalogConnection, connection))
        {
            return;
        }

        StopObservingCatalogConnection();
        _observedCatalogConnection = connection;
        _observedCatalogConnection.StateChange += CatalogConnectionStateChanged;
    }

    private void CatalogConnectionStateChanged(object? sender, StateChangeEventArgs eventArgs)
    {
        if (eventArgs.CurrentState != ConnectionState.Open
            && ReferenceEquals(sender, _initializedCatalogConnection))
        {
            _initializedCatalogConnection = null;
        }
    }

    private void StopObservingCatalogConnection()
    {
        if (_observedCatalogConnection is not null)
        {
            _observedCatalogConnection.StateChange -= CatalogConnectionStateChanged;
        }

        _observedCatalogConnection = null;
        _initializedCatalogConnection = null;
        _initializingCatalogConnection = null;
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
            StopObservingCatalogConnection();
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
            StopObservingCatalogConnection();
        }
    }
}