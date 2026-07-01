using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class MigrationsLockTests : DuckDBTestBase
{
    private LockContext CreateContext(TimeSpan? lockTimeout = null)
    {
        var context = new LockContext(FileOptions<LockContext>(duckdb =>
        {
            if (lockTimeout is { } timeout)
            {
                duckdb.MigrationLockTimeout(timeout);
            }
        }));
        context.Database.EnsureCreated();
        return context;
    }

    private static IHistoryRepository HistoryRepository(DbContext context)
        => context.GetService<IHistoryRepository>();

    [ConditionalFact]
    public void Lock_can_be_acquired_released_and_reacquired()
    {
        using var context = CreateContext();
        var repository = HistoryRepository(context);

        var dbLock = repository.AcquireDatabaseLock();
        dbLock.Dispose();

        using var reacquired = repository.AcquireDatabaseLock();
        Assert.NotNull(reacquired);
    }

    [ConditionalFact]
    public void Acquisition_times_out_with_actionable_message_when_lock_is_held()
    {
        using var holder = CreateContext();
        using var heldLock = HistoryRepository(holder).AcquireDatabaseLock();

        using var waiter = CreateContext(TimeSpan.FromSeconds(2));
        var exception = Assert.Throws<TimeoutException>(() => HistoryRepository(waiter).AcquireDatabaseLock());

        Assert.Contains("__EFMigrationsLock", exception.Message);
        Assert.Contains("DELETE FROM \"__EFMigrationsLock\"", exception.Message);
        Assert.Contains("MigrationLockTimeout", exception.Message);
        Assert.Contains("held since", exception.Message);
    }

    [ConditionalFact]
    public async Task Async_acquisition_times_out_when_lock_is_held()
    {
        using var holder = CreateContext();
        await using var heldLock = await HistoryRepository(holder).AcquireDatabaseLockAsync();

        using var waiter = CreateContext(TimeSpan.FromSeconds(2));
        var exception = await Assert.ThrowsAsync<TimeoutException>(
            () => HistoryRepository(waiter).AcquireDatabaseLockAsync());

        Assert.Contains("__EFMigrationsLock", exception.Message);
    }

    [ConditionalFact]
    public async Task Waiter_succeeds_when_lock_is_released_within_the_timeout()
    {
        using var holder = CreateContext();
        var heldLock = HistoryRepository(holder).AcquireDatabaseLock();

        var release = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            heldLock.Dispose();
        });

        using var waiter = CreateContext(TimeSpan.FromSeconds(30));
        using var acquired = await HistoryRepository(waiter).AcquireDatabaseLockAsync();

        Assert.NotNull(acquired);
        await release;
    }

    [ConditionalFact]
    public void MigrationLockTimeout_rejects_zero_and_negative_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateContext(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateContext(TimeSpan.FromSeconds(-1)));
    }

    [ConditionalFact]
    public void MigrationLockTimeout_accepts_InfiniteTimeSpan()
    {
        using var context = CreateContext(Timeout.InfiniteTimeSpan);
        var extension = context.GetService<IDbContextOptions>().FindExtension<DuckDBOptionsExtension>();

        Assert.Equal(Timeout.InfiniteTimeSpan, extension!.MigrationLockTimeout);
    }

    [ConditionalFact]
    public void MigrationLockTimeout_sets_the_option_and_defaults_to_null()
    {
        using var configured = CreateContext(TimeSpan.FromSeconds(42));
        var extension = configured.GetService<IDbContextOptions>().FindExtension<DuckDBOptionsExtension>();
        Assert.Equal(TimeSpan.FromSeconds(42), extension!.MigrationLockTimeout);

        using var unconfigured = CreateContext();
        var defaultExtension = unconfigured.GetService<IDbContextOptions>().FindExtension<DuckDBOptionsExtension>();
        Assert.Null(defaultExtension!.MigrationLockTimeout);
    }

    private sealed class LockContext(DbContextOptions<LockContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Row>().Property(e => e.Id).ValueGeneratedNever();
    }

    private sealed class Row
    {
        public int Id { get; set; }
    }
}
