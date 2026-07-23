using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace DuckDB.EFCoreProvider.Migrations.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBHistoryRepository : HistoryRepository
{
    private readonly IDuckDBEngineCapabilities _capabilities;

    private const string IdempotentScriptsNotSupportedMessage =
        "Generating idempotent scripts for migrations is not supported by DuckDB, which has no procedural"
        + " IF blocks to guard each migration. Generate a plain script ('dotnet ef migrations script'), or"
        + " apply migrations with 'dotnet ef database update' or 'Database.Migrate()', which consult the"
        + " migrations history table themselves.";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     The default maximum time to wait for the migrations lock before failing with a
    ///     <see cref="TimeoutException" />. Configurable with
    ///     <c>UseDuckDB(o => o.MigrationLockTimeout(...))</c>.
    /// </summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromMinutes(5);

    public DuckDBHistoryRepository(HistoryRepositoryDependencies dependencies)
        : this(
            dependencies,
            dependencies.CurrentContext.Context.GetService<IDuckDBEngineCapabilities>())
    {
    }

    public DuckDBHistoryRepository(
        HistoryRepositoryDependencies dependencies,
        IDuckDBEngineCapabilities capabilities)
        : base(dependencies)
    {
        _capabilities = capabilities;
    }

    protected override bool InterpretExistsResult(object? value)
    {
        return value is true;
    }

    public override IMigrationsDatabaseLock AcquireDatabaseLock()
    {
        EnsureMigrationsSupported();
        Dependencies.MigrationsLogger.AcquiringMigrationLock();

        if (!InterpretExistsResult(
                Dependencies.RawSqlCommandBuilder.Build(CreateExistsSql(LockTableName))
                    .ExecuteScalar(CreateRelationalCommandParameters())))
        {
            CreateLockTableCommand().ExecuteNonQuery(CreateRelationalCommandParameters());
        }

        var timeout = GetLockTimeout();
        var deadline = GetDeadline(timeout);
        var retryDelay = RetryDelay;
        while (true)
        {
            var dbLock = CreateMigrationDatabaseLock();
            var insertedId = CreateInsertLockCommand(DateTimeOffset.UtcNow)
                .ExecuteScalar(CreateRelationalCommandParameters());
            if (insertedId is not null)
            {
                return dbLock;
            }

            var now = DateTimeOffset.UtcNow;
            if (now >= deadline)
            {
                throw CreateLockTimeoutException(timeout);
            }

            Thread.Sleep(ClampToDeadline(retryDelay, now, deadline));
            if (retryDelay < MaxRetryDelay)
            {
                retryDelay = retryDelay.Add(retryDelay);
            }
        }
    }

    public override async Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = new())
    {
        EnsureMigrationsSupported();
        Dependencies.MigrationsLogger.AcquiringMigrationLock();

        if (!InterpretExistsResult(
                await Dependencies.RawSqlCommandBuilder.Build(CreateExistsSql(LockTableName))
                    .ExecuteScalarAsync(CreateRelationalCommandParameters(), cancellationToken).ConfigureAwait(false)))
        {
            await CreateLockTableCommand().ExecuteNonQueryAsync(CreateRelationalCommandParameters(), cancellationToken)
                .ConfigureAwait(false);
        }

        var timeout = GetLockTimeout();
        var deadline = GetDeadline(timeout);
        var retryDelay = RetryDelay;
        while (true)
        {
            var dbLock = CreateMigrationDatabaseLock();
            var insertedId = await CreateInsertLockCommand(DateTimeOffset.UtcNow)
                .ExecuteScalarAsync(CreateRelationalCommandParameters(), cancellationToken)
                .ConfigureAwait(false);
            if (insertedId is not null)
            {
                return dbLock;
            }

            var now = DateTimeOffset.UtcNow;
            if (now >= deadline)
            {
                throw CreateLockTimeoutException(timeout);
            }

            await Task.Delay(ClampToDeadline(retryDelay, now, deadline), cancellationToken).ConfigureAwait(false);
            if (retryDelay < MaxRetryDelay)
            {
                retryDelay = retryDelay.Add(retryDelay);
            }
        }
    }

    public override string GetCreateIfNotExistsScript()
    {
        EnsureMigrationsSupported();
        var script = GetCreateScript();
        return script.Insert(script.IndexOf("CREATE TABLE", StringComparison.Ordinal) + 12, " IF NOT EXISTS");
    }

    public override string GetBeginIfNotExistsScript(string migrationId)
    {
        throw new NotSupportedException(IdempotentScriptsNotSupportedMessage);
    }

    public override string GetBeginIfExistsScript(string migrationId)
    {
        throw new NotSupportedException(IdempotentScriptsNotSupportedMessage);
    }

    public override string GetEndIfScript()
    {
        throw new NotSupportedException(IdempotentScriptsNotSupportedMessage);
    }

    public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Explicit;

    protected override string ExistsSql
    {
        get
        {
            EnsureMigrationsSupported();
            return CreateExistsSql(TableName);
        }
    }

    private void EnsureMigrationsSupported()
    {
        if (!_capabilities.SupportsEfMigrations)
        {
            throw new NotSupportedException(DuckDBCapabilityErrorMessages.MigrationsNotSupported);
        }
    }

    /// <summary>
    ///     The name of the table that will serve as a database-wide lock for migrations.
    /// </summary>
    protected virtual string LockTableName { get; } = "__EFMigrationsLock";

    private TimeSpan GetLockTimeout()
        => Dependencies.CurrentContext.Context
               .GetService<IDbContextOptions>()
               .FindExtension<DuckDBOptionsExtension>()?.MigrationLockTimeout
           ?? DefaultLockTimeout;

    private static DateTimeOffset GetDeadline(TimeSpan timeout)
        => timeout == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow + timeout;

    private static TimeSpan ClampToDeadline(TimeSpan delay, DateTimeOffset now, DateTimeOffset deadline)
        => deadline == DateTimeOffset.MaxValue || now + delay <= deadline ? delay : deadline - now;

    private TimeoutException CreateLockTimeoutException(TimeSpan timeout)
    {
        string holder;
        try
        {
            var heldSince = Dependencies.RawSqlCommandBuilder
                .Build($"""SELECT "Timestamp" FROM "{LockTableName}" WHERE "Id" = 1""")
                .ExecuteScalar(CreateRelationalCommandParameters());
            holder = heldSince is null
                ? "The lock row no longer exists, so the holder may have just released it; retrying may succeed."
                : $"The lock has been held since {heldSince} (UTC).";
        }
        catch (Exception)
        {
            holder = "The current holder could not be determined.";
        }

        return new TimeoutException(
            $"Could not acquire the migrations lock within {timeout}. Another migration appears to be in"
            + $" progress: migrators coordinate through a row in the '{LockTableName}' table. {holder}"
            + " If the process that created the lock is no longer running (for example, it crashed or was"
            + $""" killed mid-migration), remove the stale lock with: DELETE FROM "{LockTableName}"."""
            + " The wait time can be configured with 'UseDuckDB(o => o.MigrationLockTimeout(...))'.");
    }

    private string CreateExistsSql(string tableName)
    {
        var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

        return $"""
                SELECT coalesce(any_value(true), false) FROM duckdb_tables() WHERE "table_name" = {stringTypeMapping.GenerateSqlLiteral(tableName)};
                """;
    }

    private IRelationalCommand CreateLockTableCommand()
        => Dependencies.RawSqlCommandBuilder.Build(
            $"""
             CREATE TABLE IF NOT EXISTS "{LockTableName}" (
                 "Id" INTEGER NOT NULL CONSTRAINT "PK_{LockTableName}" PRIMARY KEY,
                 "Timestamp" TEXT NOT NULL
             );
             """);

    private IRelationalCommand CreateInsertLockCommand(DateTimeOffset timestamp)
    {
        var timestampLiteral = Dependencies.TypeMappingSource.GetMapping(typeof(DateTimeOffset)).GenerateSqlLiteral(timestamp);

        return Dependencies.RawSqlCommandBuilder.Build(
            $"""
             INSERT OR IGNORE INTO "{LockTableName}"("Id", "Timestamp")
             VALUES(1, {timestampLiteral})
             RETURNING "Id"
             """);
    }

    private IRelationalCommand CreateDeleteLockCommand(int? id = null)
    {
        var sql = $"""
                   DELETE FROM "{LockTableName}"
                   """;
        if (id != null)
        {
            sql += $""" WHERE "Id" = {id}""";
        }

        sql += ";";
        return Dependencies.RawSqlCommandBuilder.Build(sql);
    }

    private DuckDBMigrationDatabaseLock CreateMigrationDatabaseLock()
        => new(CreateDeleteLockCommand(), CreateRelationalCommandParameters(), this);

    private RelationalCommandParameterObject CreateRelationalCommandParameters()
        => new(
            Dependencies.Connection,
            null,
            null,
            Dependencies.CurrentContext.Context,
            Dependencies.CommandLogger, CommandSource.Migrations);
}
