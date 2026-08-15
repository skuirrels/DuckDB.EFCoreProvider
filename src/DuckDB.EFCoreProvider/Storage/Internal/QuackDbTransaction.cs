using System.Data;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>Transaction adapter that keeps BEGIN/COMMIT/ROLLBACK inside the remote Quack session.</summary>
internal sealed class QuackDbTransaction : DbTransaction
{
    private readonly QuackDbConnection _connection;
    private bool _completed;

    internal QuackDbTransaction(QuackDbConnection connection, IsolationLevel isolationLevel)
    {
        _connection = connection;
        IsolationLevel = isolationLevel is IsolationLevel.Unspecified
            ? IsolationLevel.Snapshot
            : isolationLevel;
    }

    public override IsolationLevel IsolationLevel { get; }

    protected override DbConnection? DbConnection => _completed ? null : _connection;

    public override void Commit()
    {
        EnsureActive();
        _connection.CompleteTransaction(this, commit: true);
        _completed = true;
    }

    public override void Rollback()
    {
        EnsureActive();
        _connection.CompleteTransaction(this, commit: false);
        _completed = true;
    }

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await _connection.CompleteTransactionAsync(this, commit: true, cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public override async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        EnsureActive();
        await _connection.CompleteTransactionAsync(this, commit: false, cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_completed)
        {
            _connection.ReleaseTransaction(this);
            _completed = true;
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            await _connection.ReleaseTransactionAsync(this).ConfigureAwait(false);
            _completed = true;
        }

        GC.SuppressFinalize(this);
    }

    private void EnsureActive()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The Quack transaction has already completed.");
        }
    }
}