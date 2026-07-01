using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace DuckDB.EFCoreProvider.Migrations.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBMigrationDatabaseLock : IMigrationsDatabaseLock
{
    private readonly IRelationalCommand _releaseLockCommand;
    private readonly RelationalCommandParameterObject _relationalCommandParameters;
    private readonly CancellationToken _cancellationToken;

    public DuckDBMigrationDatabaseLock(
        IRelationalCommand releaseLockCommand,
        RelationalCommandParameterObject relationalCommandParameters,
        IHistoryRepository historyRepository,
        CancellationToken cancellationToken = default)
    {
        _releaseLockCommand = releaseLockCommand;
        _relationalCommandParameters = relationalCommandParameters;
        _cancellationToken = cancellationToken;
        HistoryRepository = historyRepository;
    }

    public void Dispose()
    {
        _releaseLockCommand.ExecuteScalar(_relationalCommandParameters);
    }

    public async ValueTask DisposeAsync()
    {
        await _releaseLockCommand.ExecuteScalarAsync(_relationalCommandParameters, _cancellationToken);
    }

    public IHistoryRepository HistoryRepository { get; }
}
