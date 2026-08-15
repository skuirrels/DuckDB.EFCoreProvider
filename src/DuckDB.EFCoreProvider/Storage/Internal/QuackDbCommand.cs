using DuckDB.NET.Data;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>ADO command adapter that sends one fully-bound SQL command to the remote Quack session.</summary>
internal sealed class QuackDbCommand : DbCommand
{
    private readonly QuackDbConnection _connection;
    private readonly DuckDBCommand _parameterCommand;
    private DuckDBCommand? _executionCommand;
    private DbTransaction? _transaction;
    private int _disposed;

    internal QuackDbCommand(QuackDbConnection connection)
    {
        _connection = connection;
        _parameterCommand = connection.InnerConnection.CreateCommand();
    }

    [AllowNull]
    public override string CommandText
    {
        get => _parameterCommand.CommandText;
        set => _parameterCommand.CommandText = value;
    }

    public override int CommandTimeout
    {
        get => _parameterCommand.CommandTimeout;
        set => _parameterCommand.CommandTimeout = value;
    }

    public override CommandType CommandType
    {
        get => _parameterCommand.CommandType;
        set
        {
            if (value != CommandType.Text)
            {
                throw new NotSupportedException("Quack commands support CommandType.Text only.");
            }

            _parameterCommand.CommandType = value;
        }
    }

    public override bool DesignTimeVisible
    {
        get => _parameterCommand.DesignTimeVisible;
        set => _parameterCommand.DesignTimeVisible = value;
    }

    public override UpdateRowSource UpdatedRowSource
    {
        get => _parameterCommand.UpdatedRowSource;
        set => _parameterCommand.UpdatedRowSource = value;
    }

    [AllowNull]
    protected override DbConnection DbConnection
    {
        get => _connection;
        set
        {
            if (value is not null && !ReferenceEquals(value, _connection))
            {
                throw new InvalidOperationException("A Quack command cannot be moved to another connection.");
            }
        }
    }

    protected override DbParameterCollection DbParameterCollection => _parameterCommand.Parameters;

    protected override DbTransaction? DbTransaction
    {
        get => _transaction;
        set
        {
            if (value is not null && value.Connection is null)
            {
                throw new InvalidOperationException("The transaction is no longer active.");
            }

            if (value is not null && !ReferenceEquals(value.Connection, _connection))
            {
                throw new InvalidOperationException("The transaction belongs to another connection.");
            }

            _transaction = value;
        }
    }

    public override void Cancel()
        => _executionCommand?.Cancel();

    public override void Prepare()
    {
        // Quack prepares remotely as part of the request; there is no independent wire-level bind step.
    }

    public override Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    protected override DbParameter CreateDbParameter()
        => _parameterCommand.CreateParameter();

    public override int ExecuteNonQuery()
    {
        using var reader = ExecuteDbDataReader(CommandBehavior.Default);
        return ReadAffectedRows(reader);
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        await using var reader = await ExecuteDbDataReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
        return await ReadAffectedRowsAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    public override object? ExecuteScalar()
    {
        using var reader = ExecuteDbDataReader(CommandBehavior.SingleRow);
        return reader.Read() && reader.FieldCount > 0 ? reader.GetValue(0) : null;
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        await using var reader = await ExecuteDbDataReaderAsync(CommandBehavior.SingleRow, cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && reader.FieldCount > 0
            ? reader.GetValue(0)
            : null;
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        ReplaceExecutionCommand();
        var closeConnection = behavior.HasFlag(CommandBehavior.CloseConnection);
        var reader = _executionCommand!.ExecuteReader(behavior & ~CommandBehavior.CloseConnection);
        return closeConnection ? new QuackDbDataReader(reader, _connection) : reader;
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior,
        CancellationToken cancellationToken)
    {
        ReplaceExecutionCommand();
        var closeConnection = behavior.HasFlag(CommandBehavior.CloseConnection);
        var reader = await _executionCommand!
            .ExecuteReaderAsync(behavior & ~CommandBehavior.CloseConnection, cancellationToken)
            .ConfigureAwait(false);
        return closeConnection ? new QuackDbDataReader(reader, _connection) : reader;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try
            {
                _executionCommand?.Dispose();
            }
            finally
            {
                _parameterCommand.Dispose();
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
                if (_executionCommand is not null)
                {
                    await _executionCommand.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await _parameterCommand.DisposeAsync().ConfigureAwait(false);
            }
        }

        Dispose(disposing: false);
        GC.SuppressFinalize(this);
    }

    private void ReplaceExecutionCommand()
    {
        _connection.ValidateCommandTransaction(_transaction);
        _executionCommand?.Dispose();
        _executionCommand = _connection.CreateRemoteCommand(CommandText, Parameters);
        _executionCommand.CommandTimeout = CommandTimeout;
    }

    private static int ReadAffectedRows(DbDataReader reader)
    {
        if (!reader.Read() || reader.FieldCount == 0 || reader.IsDBNull(0))
        {
            return 0;
        }

        return Convert.ToInt32(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> ReadAffectedRowsAsync(DbDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            || reader.FieldCount == 0
            || await reader.IsDBNullAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        return Convert.ToInt32(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture);
    }
}