using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using System.Globalization;

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

        return _capabilities.MigrationLockStrategy == DuckDBMigrationLockStrategy.ConditionalUpdate
            ? AcquireClaimedLock()
            : AcquireKeyedLock();
    }

    public override Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = new())
    {
        EnsureMigrationsSupported();
        Dependencies.MigrationsLogger.AcquiringMigrationLock();

        return _capabilities.MigrationLockStrategy == DuckDBMigrationLockStrategy.ConditionalUpdate
            ? AcquireClaimedLockAsync(cancellationToken)
            : AcquireKeyedLockAsync(cancellationToken);
    }

    private IMigrationsDatabaseLock AcquireKeyedLock()
    {
        if (!LockTableExists())
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
                throw CreateKeyedLockTimeoutException(timeout);
            }

            Thread.Sleep(ClampToDeadline(retryDelay, now, deadline));
            if (retryDelay < MaxRetryDelay)
            {
                retryDelay = retryDelay.Add(retryDelay);
            }
        }
    }

    private async Task<IMigrationsDatabaseLock> AcquireKeyedLockAsync(CancellationToken cancellationToken)
    {
        if (!await LockTableExistsAsync(cancellationToken).ConfigureAwait(false))
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
                throw CreateKeyedLockTimeoutException(timeout);
            }

            await Task.Delay(ClampToDeadline(retryDelay, now, deadline), cancellationToken).ConfigureAwait(false);
            if (retryDelay < MaxRetryDelay)
            {
                retryDelay = retryDelay.Add(retryDelay);
            }
        }
    }

    private IMigrationsDatabaseLock AcquireClaimedLock()
    {
        EnsureClaimLockTable();

        var holder = Guid.NewGuid().ToString("N");
        var timeout = GetLockTimeout();
        var deadline = GetDeadline(timeout);
        var retryDelay = RetryDelay;
        while (true)
        {
            if (TryClaimLock(holder))
            {
                return CreateClaimedMigrationDatabaseLock(holder);
            }

            var now = DateTimeOffset.UtcNow;
            if (now >= deadline)
            {
                throw CreateClaimedLockTimeoutException(timeout);
            }

            Thread.Sleep(ClampToDeadline(retryDelay, now, deadline));
            if (retryDelay < MaxRetryDelay)
            {
                retryDelay = retryDelay.Add(retryDelay);
            }
        }
    }

    private async Task<IMigrationsDatabaseLock> AcquireClaimedLockAsync(CancellationToken cancellationToken)
    {
        await EnsureClaimLockTableAsync(cancellationToken).ConfigureAwait(false);

        var holder = Guid.NewGuid().ToString("N");
        var timeout = GetLockTimeout();
        var deadline = GetDeadline(timeout);
        var retryDelay = RetryDelay;
        while (true)
        {
            if (await TryClaimLockAsync(holder, cancellationToken).ConfigureAwait(false))
            {
                return CreateClaimedMigrationDatabaseLock(holder);
            }

            var now = DateTimeOffset.UtcNow;
            if (now >= deadline)
            {
                throw CreateClaimedLockTimeoutException(timeout);
            }

            await Task.Delay(ClampToDeadline(retryDelay, now, deadline), cancellationToken).ConfigureAwait(false);
            if (retryDelay < MaxRetryDelay)
            {
                retryDelay = retryDelay.Add(retryDelay);
            }
        }
    }

    // The claim is a single autocommit UPDATE, so it commits (or conflicts) before the migrator opens its migration
    // transaction. Zero affected rows means another migrator holds the lock; a commit conflict means another migrator
    // claimed the free row first. Both are treated as busy and retried until the lock timeout.
    private bool TryClaimLock(string holder)
    {
        try
        {
            return CreateClaimLockCommand(holder, DateTimeOffset.UtcNow)
                .ExecuteNonQuery(CreateRelationalCommandParameters()) > 0;
        }
        catch (Exception exception) when (DuckDBTransientExceptionDetector.ShouldRetryOn(exception))
        {
            return false;
        }
    }

    private async Task<bool> TryClaimLockAsync(string holder, CancellationToken cancellationToken)
    {
        try
        {
            return await CreateClaimLockCommand(holder, DateTimeOffset.UtcNow)
                .ExecuteNonQueryAsync(CreateRelationalCommandParameters(), cancellationToken)
                .ConfigureAwait(false) > 0;
        }
        catch (Exception exception) when (DuckDBTransientExceptionDetector.ShouldRetryOn(exception))
        {
            return false;
        }
    }

    // The keyless lock table is created and seeded with its single row in one transaction. Concurrent creators of
    // the same table conflict at commit, so exactly one seeded table survives; the losers find it on the re-check.
    // A plain CREATE TABLE (not IF NOT EXISTS) keeps a creator whose snapshot already contains the table from
    // inserting a second row. The re-check runs in the catch body, after the failed transaction has been disposed
    // (rolled back): an exception filter would run before that and query through the aborted transaction.
    private void EnsureClaimLockTable()
    {
        if (LockTableExists())
        {
            return;
        }

        var connection = Dependencies.Connection;
        connection.Open();
        try
        {
            using var transaction = connection.BeginTransaction();
            CreateClaimLockTableCommand().ExecuteNonQuery(CreateRelationalCommandParameters());
            CreateSeedClaimLockCommand().ExecuteNonQuery(CreateRelationalCommandParameters());
            transaction.Commit();
        }
        catch (Exception)
        {
            if (!LockTableExists())
            {
                throw;
            }

            // Another migrator created and seeded the lock table first.
        }
        finally
        {
            connection.Close();
        }
    }

    private async Task EnsureClaimLockTableAsync(CancellationToken cancellationToken)
    {
        if (await LockTableExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var connection = Dependencies.Connection;
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await CreateClaimLockTableCommand()
                .ExecuteNonQueryAsync(CreateRelationalCommandParameters(), cancellationToken)
                .ConfigureAwait(false);
            await CreateSeedClaimLockCommand()
                .ExecuteNonQueryAsync(CreateRelationalCommandParameters(), cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            if (!await LockTableExistsAsync(cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            // Another migrator created and seeded the lock table first.
        }
        finally
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private bool LockTableExists()
        => InterpretExistsResult(
            Dependencies.RawSqlCommandBuilder.Build(CreateExistsSql(LockTableName))
                .ExecuteScalar(CreateRelationalCommandParameters()));

    private async Task<bool> LockTableExistsAsync(CancellationToken cancellationToken)
        => InterpretExistsResult(
            await Dependencies.RawSqlCommandBuilder.Build(CreateExistsSql(LockTableName))
                .ExecuteScalarAsync(CreateRelationalCommandParameters(), cancellationToken)
                .ConfigureAwait(false));

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

    private TimeoutException CreateKeyedLockTimeoutException(TimeSpan timeout)
    {
        string holder;
        try
        {
            var heldSince = Dependencies.RawSqlCommandBuilder
                .Build($"""SELECT "Timestamp" FROM {DelimitedLockTableName} WHERE "Id" = 1""")
                .ExecuteScalar(CreateRelationalCommandParameters());
            holder = heldSince is null
                ? "The lock row no longer exists, so the holder may have just released it; retrying may succeed."
                : $"The lock has been held since {heldSince} (UTC).";
        }
        catch (Exception)
        {
            holder = "The current holder could not be determined.";
        }

        return CreateLockTimeoutException(
            timeout,
            holder,
            $"DELETE FROM {DelimitedLockTableName}");
    }

    private TimeoutException CreateClaimedLockTimeoutException(TimeSpan timeout)
    {
        string holder;
        try
        {
            long rows;
            object? heldSince;
            using (var reader = Dependencies.RawSqlCommandBuilder
                       .Build($"""SELECT count(*), max("Timestamp") FROM {DelimitedLockTableName} WHERE "Id" = 1""")
                       .ExecuteReader(CreateRelationalCommandParameters()))
            {
                var row = reader.DbDataReader;
                row.Read();
                rows = Convert.ToInt64(row.GetValue(0), CultureInfo.InvariantCulture);
                heldSince = row.IsDBNull(1) ? null : row.GetValue(1);
            }

            holder = rows == 0
                ? "The lock row is missing, so no migrator can claim it. Restore it with: INSERT INTO"
                    + $""" {DelimitedLockTableName} ("Id", "Holder", "Timestamp") VALUES (1, NULL, NULL)."""
                : heldSince is null
                    ? "The lock row is free, so the holder may have just released it; retrying may succeed."
                    : $"The lock has been held since {heldSince} (UTC).";
        }
        catch (Exception)
        {
            holder = "The current holder could not be determined.";
        }

        return CreateLockTimeoutException(
            timeout,
            holder,
            $"""UPDATE {DelimitedLockTableName} SET "Holder" = NULL, "Timestamp" = NULL WHERE "Id" = 1""");
    }

    private TimeoutException CreateLockTimeoutException(TimeSpan timeout, string holder, string releaseSql)
        => new(
            $"Could not acquire the migrations lock within {timeout}. Another migration appears to be in"
            + $" progress: migrators coordinate through a row in the '{LockTableName}' table. {holder}"
            + " If the process that created the lock is no longer running (for example, it crashed or was"
            + $" killed mid-migration), remove the stale lock with: {releaseSql}."
            + " The wait time can be configured with 'UseDuckDB(o => o.MigrationLockTimeout(...))'.");

    private string CreateExistsSql(string tableName)
    {
        var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));

        // duckdb_tables() spans every attached catalog, so the probe is scoped to the one this context maps
        // to. It matters for the attached-catalog profiles (DuckLake, an encrypted database), where the host
        // instance can hold another context's history table under the same name.
        return $"""
                SELECT coalesce(any_value(true), false) FROM duckdb_tables()
                 WHERE "table_name" = {stringTypeMapping.GenerateSqlLiteral(tableName)}
                   AND "database_name" = current_database();
                """;
    }

    private string DelimitedLockTableName
        => Dependencies.SqlGenerationHelper.DelimitIdentifier(LockTableName);

    private IRelationalCommand CreateClaimLockTableCommand()
        => Dependencies.RawSqlCommandBuilder.Build(
            $"""
             CREATE TABLE {DelimitedLockTableName} (
                 "Id" INTEGER NOT NULL,
                 "Holder" TEXT NULL,
                 "Timestamp" TEXT NULL
             );
             """);

    private IRelationalCommand CreateSeedClaimLockCommand()
        => Dependencies.RawSqlCommandBuilder.Build(
            $"""
             INSERT INTO {DelimitedLockTableName} ("Id", "Holder", "Timestamp") VALUES (1, NULL, NULL);
             """);

    private IRelationalCommand CreateClaimLockCommand(string holder, DateTimeOffset timestamp)
    {
        var stringMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));
        var timestampLiteral = stringMapping.GenerateSqlLiteral(
            timestamp.ToString("yyyy-MM-dd HH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture));

        return Dependencies.RawSqlCommandBuilder.Build(
            $"""
             UPDATE {DelimitedLockTableName}
             SET "Holder" = {stringMapping.GenerateSqlLiteral(holder)}, "Timestamp" = {timestampLiteral}
             WHERE "Id" = 1 AND "Holder" IS NULL;
             """);
    }

    private IRelationalCommand CreateReleaseClaimedLockCommand(string holder)
        => Dependencies.RawSqlCommandBuilder.Build(
            $"""
             UPDATE {DelimitedLockTableName}
             SET "Holder" = NULL, "Timestamp" = NULL
             WHERE "Id" = 1 AND "Holder" = {Dependencies.TypeMappingSource.GetMapping(typeof(string)).GenerateSqlLiteral(holder)};
             """);

    // Release is not tied to the acquiring call's cancellation token: a cancelled release would leave a stale lock.
    private DuckDBMigrationDatabaseLock CreateClaimedMigrationDatabaseLock(string holder)
        => new(CreateReleaseClaimedLockCommand(holder), CreateRelationalCommandParameters(), this);

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