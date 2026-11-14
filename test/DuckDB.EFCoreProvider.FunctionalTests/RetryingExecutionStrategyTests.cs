using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Storage;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class RetryingExecutionStrategyTests : DuckDBTestBase
{
    [Fact]
    public async Task SaveChanges_retries_after_a_write_write_conflict()
    {
        await SeedAsync();
        var conflict = new ConflictingWriter(DbPath, "UPDATE \"Accounts\"");
        await using var context = CreateContext(conflict, retry: true);

        var account = await context.Accounts.SingleAsync(a => a.Id == 1);
        account.Balance = 100;
        await context.SaveChangesAsync();

        Assert.Equal(2, conflict.Attempts);
        Assert.Equal(100, await ReadBalanceAsync(1));
    }

    [Fact]
    public async Task ExecuteUpdate_retries_after_a_write_write_conflict()
    {
        await SeedAsync();
        var conflict = new ConflictingWriter(DbPath, "UPDATE \"Accounts\"");
        await using var context = CreateContext(conflict, retry: true);

        Assert.Equal(1, await context.Accounts.Where(a => a.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, 200)));

        Assert.Equal(2, conflict.Attempts);
        Assert.Equal(200, await ReadBalanceAsync(1));
    }

    [Fact]
    public async Task Explicit_transactions_are_replayed_through_the_strategy()
    {
        await SeedAsync();
        var conflict = new ConflictingWriter(DbPath, "UPDATE \"Accounts\"");
        await using var context = CreateContext(conflict, retry: true);

        var strategy = context.Database.CreateExecutionStrategy();
        Assert.IsType<DuckDBRetryingExecutionStrategy>(strategy);
        await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var account = await context.Accounts.SingleAsync(a => a.Id == 2);
            account.Balance += 5;
            context.Add(new Account { Id = 3, Balance = 7 });
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        // The replay re-reads the row the competing transaction committed (-1) before adding to it.
        Assert.Equal(2, conflict.Attempts);
        Assert.Equal(4, await ReadBalanceAsync(2));
        Assert.Equal(7, await ReadBalanceAsync(3));
    }

    [Fact]
    public async Task Explicit_transactions_outside_the_strategy_are_rejected_when_retries_are_enabled()
    {
        await SeedAsync();
        await using var context = CreateContext(conflict: null, retry: true);

        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Add(new Account { Id = 3, Balance = 1 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Constraint_violations_are_not_retried()
    {
        await SeedAsync();
        var counter = new ConflictingWriter(DbPath, "INSERT INTO \"Accounts\"", conflictOnFirstAttempt: false);
        await using var context = CreateContext(counter, retry: true);
        await context.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX "IX_Accounts_Id" ON "Accounts" ("Id");""");

        context.Add(new Account { Id = 1, Balance = 1 });
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.IsNotType<RetryLimitExceededException>(exception);
        Assert.Equal(1, counter.Attempts);
    }

    [Fact]
    public async Task Without_retries_a_write_write_conflict_fails_the_operation()
    {
        await SeedAsync();
        var conflict = new ConflictingWriter(DbPath, "UPDATE \"Accounts\"");
        await using var context = CreateContext(conflict, retry: false);

        var account = await context.Accounts.SingleAsync(a => a.Id == 1);
        account.Balance = 100;
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.True(DuckDBTransientExceptionDetector.ShouldRetryOn(exception));
        Assert.Equal(1, conflict.Attempts);
    }

    [Fact]
    public async Task Retries_stop_at_the_configured_limit()
    {
        await SeedAsync();
        var conflict = new ConflictingWriter(DbPath, "UPDATE \"Accounts\"", conflictOnEveryAttempt: true);
        await using var context = CreateContext(conflict, retry: true, maxRetryCount: 2);

        var account = await context.Accounts.SingleAsync(a => a.Id == 1);
        account.Balance = 100;
        var exception = await Assert.ThrowsAsync<RetryLimitExceededException>(() => context.SaveChangesAsync());

        Assert.True(DuckDBTransientExceptionDetector.ShouldRetryOn(exception.InnerException));
        Assert.Equal(3, conflict.Attempts);
    }

    [Theory]
    [InlineData("TransactionContext Error: Conflict on update!", true)]
    [InlineData("TransactionContext Error: Conflict on tuple deletion!", true)]
    [InlineData("TransactionContext Error: Catalog write-write conflict on create with \"u\"", true)]
    [InlineData("Transaction conflict - attempting to insert into table with index \"1\" - but another transaction has altered it", true)]
    [InlineData("Attempting to modify table t but another transaction has dropped this table", true)]
    [InlineData("Exceeded the maximum retry count of 10 set by the ducklake_max_retry_count setting.", true)]
    [InlineData("TransactionContext Error: Failed to commit: PRIMARY KEY or UNIQUE constraint violation: duplicate key \"5\"", false)]
    [InlineData("TransactionContext Error: Current transaction is aborted (please ROLLBACK)", false)]
    [InlineData("Constraint Error: Duplicate key \"id: 1\" violates primary key constraint.", false)]
    public void Detector_classifies_DuckDB_and_DuckLake_conflicts(string message, bool transient)
    {
        var exception = RaiseDuckDBException(message);

        Assert.Equal(transient, DuckDBTransientExceptionDetector.ShouldRetryOn(exception));
        Assert.Equal(transient, DuckDBTransientExceptionDetector.ShouldRetryOn(new DbUpdateException("wrapped", exception)));
        Assert.False(DuckDBTransientExceptionDetector.ShouldRetryOn(new InvalidOperationException(message)));
    }

    [Fact]
    public async Task DuckLake_profile_runs_units_of_work_through_the_retrying_strategy()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake-retry-{Guid.NewGuid():N}");
        var dataPath = Path.Combine(root, "data");
        Directory.CreateDirectory(Path.Combine(root, "metadata"));
        Directory.CreateDirectory(dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<AccountContext>()
                .UseDuckLake(
                    Path.Combine(root, "metadata", "catalog.ducklake"),
                    duckLake => duckLake.DataPath(dataPath),
                    duckDb => duckDb.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(10)))
                .Options;
            await using var context = new AccountContext(options);
            await context.Database.EnsureCreatedAsync();

            var strategy = context.Database.CreateExecutionStrategy();
            Assert.IsType<DuckDBRetryingExecutionStrategy>(strategy);
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                context.Add(new Account { Id = 1, Balance = 10 });
                await context.SaveChangesAsync();
                await context.Database.DuckLake().SetCommitMessageAsync("retry-test", "seed");
                await transaction.CommitAsync();
            });

            Assert.Equal(1, await context.Accounts.Where(a => a.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, 11)));
            Assert.Equal(11, await context.Accounts.AsNoTracking().Select(a => a.Balance).SingleAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static DuckDBException RaiseDuckDBException(string message)
    {
        using var connection = new DuckDBConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT error($message)";
        command.Parameters.Add(new DuckDBParameter("message", message));
        return Assert.Throws<DuckDBException>(() => command.ExecuteScalar());
    }

    private AccountContext CreateContext(ConflictingWriter? conflict, bool retry, int maxRetryCount = 5)
    {
        var builder = new DbContextOptionsBuilder<AccountContext>()
            .UseDuckDB(
                $"Data Source={DbPath}",
                duckDb =>
                {
                    if (retry)
                    {
                        duckDb.EnableRetryOnFailure(maxRetryCount, TimeSpan.FromMilliseconds(10));
                    }
                });
        if (conflict is not null)
        {
            builder.AddInterceptors(conflict);
        }

        return new AccountContext(builder.Options);
    }

    private async Task SeedAsync()
    {
        await using var context = CreateContext(conflict: null, retry: false);

        // The table deliberately has no physical primary key. DuckDB 1.5.6 does not report a write-write conflict
        // for UPDATE ... RETURNING on a table with a primary key, and EF's update batches use RETURNING, so a keyed
        // table would hide the conflict SaveChanges is meant to retry here.
        await context.Database.ExecuteSqlRawAsync("""CREATE TABLE "Accounts" ("Id" INTEGER NOT NULL, "Balance" INTEGER NOT NULL);""");
        context.AddRange(new Account { Id = 1, Balance = 0 }, new Account { Id = 2, Balance = 0 });
        await context.SaveChangesAsync();
    }

    private async Task<int> ReadBalanceAsync(int id)
    {
        await using var context = CreateContext(conflict: null, retry: false);
        return await context.Accounts.AsNoTracking().Where(a => a.Id == id).Select(a => a.Balance).SingleAsync();
    }

    /// <summary>
    ///     Counts attempts of commands containing <paramref name="commandText" />. Before a conflicting attempt runs,
    ///     a second connection to the same database updates every account inside a transaction it keeps open, so
    ///     the command fails with a write-write conflict. The competing transaction commits once the command has
    ///     failed, so the strategy's next attempt starts from a fresh snapshot and can succeed.
    /// </summary>
    private sealed class ConflictingWriter(
        string dbPath,
        string commandText,
        bool conflictOnFirstAttempt = true,
        bool conflictOnEveryAttempt = false) : DbCommandInterceptor
    {
        private DuckDBConnection? _competitor;

        internal int Attempts { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Prepare(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Prepare(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Prepare(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Prepare(command);
            return ValueTask.FromResult(result);
        }

        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
            => ReleaseCompetitor();

        public override Task CommandFailedAsync(
            DbCommand command,
            CommandErrorEventData eventData,
            CancellationToken cancellationToken = default)
        {
            ReleaseCompetitor();
            return Task.CompletedTask;
        }

        private void Prepare(DbCommand command)
        {
            if (!command.CommandText.Contains(commandText, StringComparison.Ordinal))
            {
                return;
            }

            Attempts++;
            if (conflictOnEveryAttempt || (conflictOnFirstAttempt && Attempts == 1))
            {
                _competitor = new DuckDBConnection($"Data Source={dbPath}");
                _competitor.Open();
                Execute(_competitor, "BEGIN TRANSACTION; UPDATE \"Accounts\" SET \"Balance\" = \"Balance\" - 1;");
            }
        }

        private void ReleaseCompetitor()
        {
            if (_competitor is null)
            {
                return;
            }

            Execute(_competitor, "COMMIT;");
            _competitor.Dispose();
            _competitor = null;
        }

        private static void Execute(DuckDBConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    private sealed class AccountContext(DbContextOptions<AccountContext> options) : DbContext(options)
    {
        public DbSet<Account> Accounts => Set<Account>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Account>().Property(a => a.Id).ValueGeneratedNever();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    private sealed class Account
    {
        public int Id { get; set; }

        public int Balance { get; set; }
    }
}