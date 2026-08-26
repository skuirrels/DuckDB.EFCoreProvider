using DuckDB.EFCoreProvider.Diagnostics.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>Options for a Quack server whose lifetime is owned by the provider.</summary>
public sealed class DuckDBQuackServerOptions
{
    /// <summary>The listen URI. Defaults to Quack's loopback endpoint.</summary>
    public string Uri { get; set; } = "quack:localhost";

    /// <summary>An optional authentication token. Quack generates one when omitted.</summary>
    public string? Token { get; set; }

    /// <summary>Allows a non-loopback listen hostname. Disabled by default.</summary>
    public bool AllowOtherHostname { get; set; }

    /// <summary>Disables TLS at the Quack listener. Use a TLS-terminating reverse proxy outside loopback.</summary>
    public bool DisableSsl { get; set; }

    /// <summary>Controls how the Quack extension is provisioned before starting.</summary>
    public DuckDBExtensionLoadMode ExtensionLoadMode { get; set; } = DuckDBExtensionLoadMode.InstallAndLoad;

    /// <summary>An optional explicit Quack extension file for pinned or offline deployments.</summary>
    public string? ExtensionPath { get; set; }
}

/// <summary>One future-tolerant row returned by Quack's diagnostic table functions.</summary>
public sealed record DuckDBQuackDiagnosticRow(IReadOnlyDictionary<string, object?> Values);

/// <summary>A point-in-time Quack health and protocol diagnostic snapshot.</summary>
public sealed record DuckDBQuackDiagnosticsSnapshot(
    TimeSpan RoundTripLatency,
    DuckDBQuackDiagnosticRow? Identity,
    IReadOnlyList<DuckDBQuackDiagnosticRow> Servers,
    IReadOnlyList<DuckDBQuackDiagnosticRow> ActiveConnections,
    IReadOnlyList<DuckDBQuackDiagnosticRow> RecentProtocolEvents,
    string? ProtocolLogError);

/// <summary>A running provider-managed Quack server.</summary>
public sealed class DuckDBQuackServer : IDisposable, IAsyncDisposable
{
    private readonly DbContext _context;
    private readonly DuckDBConnection _connection;
    private readonly bool _closeConnection;
    private int _stopped;

    internal DuckDBQuackServer(
        DbContext context,
        DuckDBConnection connection,
        string uri,
        string? url,
        string authenticationToken,
        bool closeConnection)
    {
        _context = context;
        _connection = connection;
        Uri = uri;
        Url = url;
        AuthenticationToken = authenticationToken;
        _closeConnection = closeConnection;
    }

    /// <summary>The URI accepted by Quack.</summary>
    public string Uri { get; }

    /// <summary>The resolved HTTP URL reported by Quack.</summary>
    public string? Url { get; }

    /// <summary>The authentication token. Treat this value as a secret.</summary>
    public string AuthenticationToken { get; }

    /// <summary>Stops the listener and releases the provider-owned connection lease.</summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        var stopFailed = false;
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "CALL quack_stop($uri);";
            AddParameter(command, "uri", Uri);
            command.ExecuteNonQuery();
        }
        catch
        {
            stopFailed = true;
            if (!_closeConnection)
            {
                Volatile.Write(ref _stopped, 0);
            }

            throw;
        }
        finally
        {
            if (_closeConnection)
            {
                try
                {
                    _context.Database.CloseConnection();
                }
                catch when (stopFailed)
                {
                    // Preserve the primary stop failure.
                }
            }
        }
    }

    /// <summary>Stops the listener and releases the provider-owned connection lease.</summary>
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        var stopFailed = false;
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "CALL quack_stop($uri);";
            AddParameter(command, "uri", Uri);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            stopFailed = true;
            if (!_closeConnection)
            {
                Volatile.Write(ref _stopped, 0);
            }

            throw;
        }
        finally
        {
            if (_closeConnection)
            {
                try
                {
                    await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
                }
                catch when (stopFailed)
                {
                    // Preserve the primary stop failure.
                }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => StopAsync();

    /// <inheritdoc />
    public override string ToString() => $"Quack server {Uri}";

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

/// <summary>Provider-managed Quack server lifecycle and diagnostics.</summary>
public static class DuckDBQuackExtensions
{
    /// <summary>Starts a Quack listener on the context's in-process DuckDB connection.</summary>
    public static async Task<DuckDBQuackServer> StartQuackServerAsync(
        this DatabaseFacade database,
        DuckDBQuackServerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        options ??= new DuckDBQuackServerOptions();
        var uri = options.Uri;
        var token = options.Token;
        var allowOtherHostname = options.AllowOtherHostname;
        var disableSsl = options.DisableSsl;
        var extensionLoadMode = options.ExtensionLoadMode;
        var extensionPath = options.ExtensionPath;

        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        if (!uri.StartsWith("quack:", StringComparison.OrdinalIgnoreCase)
            || uri.Length == "quack:".Length)
        {
            throw new ArgumentException("A Quack listen URI must use the quack: scheme and include a host.", nameof(options));
        }

        if (token is { Length: < 4 })
        {
            throw new ArgumentException("A Quack authentication token must contain at least four characters.", nameof(options));
        }

        if (!Enum.IsDefined(extensionLoadMode))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The Quack extension load mode is invalid.");
        }

        var context = database.GetService<ICurrentDbContext>().Context;
        var operation = DuckDBOperationDiagnostics.StartInfrastructure(
            context,
            DuckDBProviderOperation.QuackServer,
            nameof(StartQuackServerAsync),
            uri);

        var dbConnection = database.GetDbConnection();
        if (dbConnection is not DuckDBConnection connection)
        {
            var exception = new NotSupportedException(
                "A provider-managed Quack server requires an in-process UseDuckDB context; a UseQuack client cannot host another server.");
            operation.Fail(exception);
            throw exception;
        }

        var openedHere = connection.State != ConnectionState.Open;
        var serverStarted = false;
        try
        {
            if (openedHere)
            {
                await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            await LoadQuackAsync(connection, extensionLoadMode, extensionPath, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = token is null
                ? "CALL quack_serve($uri, allow_other_hostname => $allow_other, disable_ssl => $disable_ssl);"
                : "CALL quack_serve($uri, token => $token, allow_other_hostname => $allow_other, disable_ssl => $disable_ssl);";
            AddParameter(command, "uri", uri);
            if (token is not null)
            {
                AddParameter(command, "token", token);
            }
            AddParameter(command, "allow_other", allowOtherHostname);
            AddParameter(command, "disable_ssl", disableSsl);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            serverStarted = true;
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("quack_serve did not return its server identity.");
            }

            var listenUri = ReadString(reader, "listen_uri") ?? ReadString(reader, "uri") ?? uri;
            var url = ReadString(reader, "url");
            var authenticationToken = ReadString(reader, "auth_token") ?? token
                ?? throw new InvalidOperationException("quack_serve did not return an authentication token.");
            var server = new DuckDBQuackServer(context, connection, listenUri, url, authenticationToken, openedHere);
            operation.Complete();
            return server;
        }
        catch (Exception exception)
        {
            if (serverStarted && connection.State == ConnectionState.Open)
            {
                try
                {
                    await StopQuackServerAsync(connection, uri).ConfigureAwait(false);
                }
                catch
                {
                    // Preserve and report the server-start failure rather than a secondary stop failure.
                }
            }

            if (openedHere && connection.State == ConnectionState.Open)
            {
                try
                {
                    await database.CloseConnectionAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Preserve and report the server-start failure rather than a secondary close failure.
                }
            }

            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>Collects identity, server/session state, latency, and recent correlated Quack protocol events.</summary>
    public static async Task<DuckDBQuackDiagnosticsSnapshot> GetQuackDiagnosticsAsync(
        this DatabaseFacade database,
        int protocolEventLimit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentOutOfRangeException.ThrowIfNegative(protocolEventLimit);

        var context = database.GetService<ICurrentDbContext>().Context;
        var connection = database.GetDbConnection();
        var operation = DuckDBOperationDiagnostics.StartCommand(
            context,
            DuckDBProviderOperation.QuackDiagnostics,
            nameof(GetQuackDiagnosticsAsync),
            connection.DataSource);
        var openedHere = connection.State != ConnectionState.Open;
        var failed = false;

        try
        {
            if (openedHere)
            {
                await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            if (connection is DuckDBConnection duckDbConnection)
            {
                await LoadQuackAsync(
                    duckDbConnection,
                    DuckDBExtensionLoadMode.LoadOnly,
                    extensionPath: null,
                    cancellationToken).ConfigureAwait(false);
            }

            var startedAt = Stopwatch.GetTimestamp();
            var identityRows = await ReadRowsAsync(connection, "FROM whoami();", cancellationToken).ConfigureAwait(false);
            var latency = Stopwatch.GetElapsedTime(startedAt);
            var servers = await ReadRowsAsync(connection, "FROM quack_server_list();", cancellationToken).ConfigureAwait(false);
            var activeConnections = await ReadRowsAsync(connection, "FROM quack_active_connections();", cancellationToken).ConfigureAwait(false);

            IReadOnlyList<DuckDBQuackDiagnosticRow> protocolEvents = [];
            string? protocolLogError = null;
            if (protocolEventLimit > 0)
            {
                try
                {
                    protocolEvents = await ReadRowsAsync(
                        connection,
                        $"SELECT * FROM duckdb_logs_parsed('Quack') ORDER BY timestamp DESC LIMIT {protocolEventLimit};",
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException
                                                  || !cancellationToken.IsCancellationRequested)
                {
                    protocolLogError = exception.Message;
                }
            }

            var snapshot = new DuckDBQuackDiagnosticsSnapshot(
                latency,
                identityRows.FirstOrDefault(),
                servers,
                activeConnections,
                protocolEvents,
                protocolLogError);
            operation.Complete(activeConnections.Count);
            return snapshot;
        }
        catch (Exception exception)
        {
            failed = true;
            operation.Fail(exception);
            throw;
        }
        finally
        {
            if (openedHere && connection.State == ConnectionState.Open)
            {
                try
                {
                    await database.CloseConnectionAsync().ConfigureAwait(false);
                }
                catch when (failed)
                {
                    // Preserve the primary diagnostics failure.
                }
            }
        }
    }

    private static async Task LoadQuackAsync(
        DuckDBConnection connection,
        DuckDBExtensionLoadMode mode,
        string? extensionPath,
        CancellationToken cancellationToken)
    {
        if (extensionPath is null && mode == DuckDBExtensionLoadMode.CallerManaged)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = extensionPath is not null
            ? $"LOAD {QuackSqlTextBuilder.Quote(extensionPath)};"
            : mode == DuckDBExtensionLoadMode.LoadOnly
                ? "LOAD quack;"
                : "INSTALL quack; LOAD quack;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task StopQuackServerAsync(DuckDBConnection connection, string uri)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "CALL quack_stop($uri);";
        AddParameter(command, "uri", uri);
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<DuckDBQuackDiagnosticRow>> ReadRowsAsync(
        DbConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<DuckDBQuackDiagnosticRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var values = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                values[reader.GetName(index)] = await reader.IsDBNullAsync(index, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetValue(index);
            }

            rows.Add(new DuckDBQuackDiagnosticRow(new ReadOnlyDictionary<string, object?>(values)));
        }

        return rows;
    }

    private static string? ReadString(DbDataReader reader, string name)
    {
        for (var index = 0; index < reader.FieldCount; index++)
        {
            if (string.Equals(reader.GetName(index), name, StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}