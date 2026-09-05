using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.NET.Data;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>A local DuckDB client connection that replays each ADO command in one attached Quack session.</summary>
internal sealed class QuackDbConnection : DbConnection
{
    private readonly DuckDBConnection _innerConnection;
    private readonly QuackOptions _options;
    private bool _initialized;
    private QuackDbTransaction? _transaction;
    private int _disposed;

    internal QuackDbConnection(
        string connectionString,
        QuackOptions options,
        IDuckDBEngineCapabilities engineCapabilities)
    {
        _innerConnection = new DuckDBConnection(connectionString);
        _options = options;
        EngineCapabilities = engineCapabilities;
    }

    internal DuckDBConnection InnerConnection => _innerConnection;

    internal string RemoteCatalogName => _options.CatalogName;

    internal IDuckDBEngineCapabilities EngineCapabilities { get; }

    [AllowNull]
    public override string ConnectionString
    {
        get => _innerConnection.ConnectionString;
        set => _innerConnection.ConnectionString = value;
    }

    public override string Database => _options.CatalogName;

    public override string DataSource => _options.Endpoint;

    public override string ServerVersion => _innerConnection.ServerVersion;

    public override ConnectionState State => _innerConnection.State;

    public override int ConnectionTimeout => _innerConnection.ConnectionTimeout;

    public override void Open()
    {
        var originalState = State;
        _innerConnection.Open();
        try
        {
            Initialize();
        }
        catch
        {
            try
            {
                _innerConnection.Close();
            }
            catch
            {
                // Preserve the attachment or initialization failure.
            }

            throw;
        }

        if (originalState != ConnectionState.Open)
        {
            OnStateChange(new StateChangeEventArgs(originalState, ConnectionState.Open));
        }
    }

    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        var originalState = State;
        await _innerConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await _innerConnection.CloseAsync().ConfigureAwait(false);
            }
            catch
            {
                // Preserve the attachment or initialization failure.
            }

            throw;
        }

        if (originalState != ConnectionState.Open)
        {
            OnStateChange(new StateChangeEventArgs(originalState, ConnectionState.Open));
        }
    }

    public override void Close()
    {
        var originalState = State;
        if (originalState != ConnectionState.Closed)
        {
            _innerConnection.Close();
        }

        _transaction = null;
        _initialized = false;
        if (originalState != ConnectionState.Closed)
        {
            OnStateChange(new StateChangeEventArgs(originalState, ConnectionState.Closed));
        }
    }

    public override async Task CloseAsync()
    {
        var originalState = State;
        if (originalState != ConnectionState.Closed)
        {
            await _innerConnection.CloseAsync().ConfigureAwait(false);
        }

        _transaction = null;
        _initialized = false;
        if (originalState != ConnectionState.Closed)
        {
            OnStateChange(new StateChangeEventArgs(originalState, ConnectionState.Closed));
        }
    }

    public override void ChangeDatabase(string databaseName)
        => throw new NotSupportedException("A Quack DbContext has one remote database selected by UseQuack.");

    protected override DbCommand CreateDbCommand()
        => new QuackDbCommand(this);

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already active on this Quack connection.");
        }

        ExecuteRemoteControl("BEGIN TRANSACTION;");
        return _transaction = new QuackDbTransaction(this, isolationLevel);
    }

    protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already active on this Quack connection.");
        }

        await ExecuteRemoteControlAsync("BEGIN TRANSACTION;", cancellationToken).ConfigureAwait(false);
        return _transaction = new QuackDbTransaction(this, isolationLevel);
    }

    internal DuckDBCommand CreateRemoteCommand(string commandText, DbParameterCollection parameters)
    {
        EnsureOpen();
        var expanded = QuackSqlTextBuilder.ExpandParameters(commandText, parameters);
        var command = _innerConnection.CreateCommand();
        command.CommandText = "FROM quack_query_by_name($quack_catalog, $quack_sql);";
        command.AddParameter("quack_catalog", _options.CatalogName);
        command.AddParameter("quack_sql", expanded);
        return command;
    }

    internal void CompleteTransaction(QuackDbTransaction transaction, bool commit)
    {
        EnsureActiveTransaction(transaction);
        ExecuteRemoteControl(commit ? "COMMIT;" : "ROLLBACK;");
        _transaction = null;
    }

    internal async Task CompleteTransactionAsync(
        QuackDbTransaction transaction,
        bool commit,
        CancellationToken cancellationToken)
    {
        EnsureActiveTransaction(transaction);
        await ExecuteRemoteControlAsync(commit ? "COMMIT;" : "ROLLBACK;", cancellationToken).ConfigureAwait(false);
        _transaction = null;
    }

    internal void ReleaseTransaction(QuackDbTransaction transaction)
    {
        if (ReferenceEquals(_transaction, transaction))
        {
            try
            {
                ExecuteRemoteControl("ROLLBACK;");
            }
            finally
            {
                _transaction = null;
            }
        }
    }

    internal async Task ReleaseTransactionAsync(QuackDbTransaction transaction)
    {
        if (ReferenceEquals(_transaction, transaction))
        {
            try
            {
                await ExecuteRemoteControlAsync("ROLLBACK;", CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _transaction = null;
            }
        }
    }

    internal void ValidateCommandTransaction(DbTransaction? transaction)
    {
        if (transaction is not null && _transaction is null)
        {
            throw new InvalidOperationException("The transaction assigned to the Quack command is no longer active.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try
            {
                Close();
            }
            finally
            {
                _innerConnection.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try
            {
                await CloseAsync().ConfigureAwait(false);
            }
            finally
            {
                await _innerConnection.DisposeAsync().ConfigureAwait(false);
            }
        }

        Dispose(disposing: false);
        GC.SuppressFinalize(this);
    }

    private void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        ExecuteLocal(GetExtensionCommand());
        if (_options.EnableHttpConnectionCaching)
        {
            ExecuteLocal("SET httpfs_connection_caching = true;");
        }

        QuackCatalogBootstrapper.Prepare(_innerConnection, _options);
        ExecuteLocal(BuildAttachCommand());
        _initialized = true;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await ExecuteLocalAsync(GetExtensionCommand(), cancellationToken).ConfigureAwait(false);
        if (_options.EnableHttpConnectionCaching)
        {
            await ExecuteLocalAsync("SET httpfs_connection_caching = true;", cancellationToken).ConfigureAwait(false);
        }

        await QuackCatalogBootstrapper
            .PrepareAsync(_innerConnection, _options, cancellationToken)
            .ConfigureAwait(false);
        await ExecuteLocalAsync(BuildAttachCommand(), cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    private string GetExtensionCommand()
        => _options.ExtensionPath is not null
            ? $"LOAD {QuackSqlTextBuilder.Quote(_options.ExtensionPath)};"
            : _options.ExtensionLoadMode switch
            {
                DuckDBExtensionLoadMode.InstallAndLoad => "INSTALL quack; LOAD quack;",
                DuckDBExtensionLoadMode.LoadOnly => "LOAD quack;",
                DuckDBExtensionLoadMode.CallerManaged => string.Empty,
                _ => throw new ArgumentOutOfRangeException(nameof(_options.ExtensionLoadMode))
            };

    private string BuildAttachCommand()
    {
        var endpoint = QuackSqlTextBuilder.Quote(_options.Endpoint);
        var token = QuackSqlTextBuilder.Quote(_options.Token);
        return $"ATTACH OR REPLACE {endpoint} AS {QuackSqlTextBuilder.QuoteIdentifier(_options.CatalogName)} "
            + $"(TYPE quack, TOKEN {token}, DISABLE_SSL {_options.DisableSsl.ToString().ToLowerInvariant()});";
    }

    private void ExecuteLocal(string commandText)
    {
        if (commandText.Length == 0)
        {
            return;
        }

        using var command = _innerConnection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private async Task ExecuteLocalAsync(string commandText, CancellationToken cancellationToken)
    {
        if (commandText.Length == 0)
        {
            return;
        }

        await using var command = _innerConnection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ExecuteRemoteControl(string commandText)
    {
        using var emptyCommand = new DuckDBCommand();
        using var command = CreateRemoteCommand(commandText, emptyCommand.Parameters);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
        }
    }

    private async Task ExecuteRemoteControlAsync(string commandText, CancellationToken cancellationToken)
    {
        await using var emptyCommand = new DuckDBCommand();
        await using var command = CreateRemoteCommand(commandText, emptyCommand.Parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
        }
    }

    private void EnsureOpen()
    {
        if (State != ConnectionState.Open || !_initialized)
        {
            throw new InvalidOperationException("The Quack connection is not open.");
        }
    }

    private void EnsureActiveTransaction(QuackDbTransaction transaction)
    {
        if (!ReferenceEquals(_transaction, transaction))
        {
            throw new InvalidOperationException("The Quack transaction is no longer active.");
        }
    }
}