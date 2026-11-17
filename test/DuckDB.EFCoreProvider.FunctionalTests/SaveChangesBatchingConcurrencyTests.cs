using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     End-to-end coverage for the affected-row guarantees of merged <c>SaveChanges</c> runs on native DuckDB and
///     DuckLake: a merged update or delete must report a missing row, and on DuckLake, where logical keys are not
///     physically enforced, a key that matches more than one row, as <see cref="DbUpdateConcurrencyException" />
///     before any row is changed.
/// </summary>
public sealed class SaveChangesBatchingConcurrencyTests : DuckDBTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batched_inserts_updates_and_deletes_round_trip(bool duckLake)
    {
        await using var store = new Store(duckLake, DbPath);
        await using (var context = store.Create())
        {
            await context.Database.EnsureCreatedAsync();
            context.Items.AddRange(Enumerable.Range(1, 5).Select(id => new Item { Id = id, Name = $"item-{id}" }));
            context.Composites.AddRange(
                new CompositeItem { KeyA = 1, KeyB = 1, Name = "a" },
                new CompositeItem { KeyA = 1, KeyB = 2, Name = "b" });
            Assert.Equal(7, await context.SaveChangesAsync());
        }

        var interceptor = new CommandCaptureInterceptor();
        await using (var context = store.Create(interceptor))
        {
            foreach (var item in await context.Items.Where(item => item.Id <= 3).ToListAsync())
            {
                item.Name += "-updated";
            }

            foreach (var composite in await context.Composites.ToListAsync())
            {
                composite.Name += "-updated";
            }

            context.Items.RemoveRange(await context.Items.Where(item => item.Id > 3).ToListAsync());
            context.Items.Add(new Item { Id = 6, Name = "item-6" });
            Assert.Equal(8, await context.SaveChangesAsync());
        }

        var sql = string.Join(Environment.NewLine, interceptor.CommandTexts);
        Assert.Contains("FROM (VALUES", sql);
        Assert.Contains("DELETE FROM \"Items\" WHERE \"Id\" IN (", sql);
        Assert.Contains("HAVING COUNT(*) = 1", sql);
        Assert.Contains(
            "INNER JOIN (VALUES ($p0, $p1), ($p3, $p4)) AS k(\"KeyA\", \"KeyB\") ON \"Composites\".\"KeyA\" = k.\"KeyA\""
            + " AND \"Composites\".\"KeyB\" = k.\"KeyB\" GROUP BY k.\"KeyA\", k.\"KeyB\" HAVING COUNT(*) = 1);",
            sql);

        await using var verification = store.Create();
        Assert.Equal(
            ["item-1-updated", "item-2-updated", "item-3-updated", "item-6"],
            await verification.Items.OrderBy(item => item.Id).Select(item => item.Name).ToListAsync());
        Assert.All(await verification.Composites.ToListAsync(), composite => Assert.EndsWith("-updated", composite.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batched_update_of_a_deleted_row_throws_and_changes_nothing(bool duckLake)
    {
        await using var store = new Store(duckLake, DbPath);
        await SeedAsync(store);

        await using var stale = store.Create();
        var items = await stale.Items.OrderBy(item => item.Id).ToListAsync();
        await DeleteItemAsync(store, 2);

        foreach (var item in items)
        {
            item.Name += "-updated";
        }

        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        Assert.Equal(2, Assert.IsType<Item>(Assert.Single(exception.Entries).Entity).Id);

        await using var verification = store.Create();
        Assert.Equal(["one", "three"], await verification.Items.OrderBy(item => item.Id).Select(item => item.Name).ToListAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Synchronous_batched_write_of_a_deleted_row_throws_and_changes_nothing(bool duckLake, bool delete)
    {
        using var store = new Store(duckLake, DbPath);
        SeedAsync(store).GetAwaiter().GetResult();

        using var stale = store.Create();
        var items = stale.Items.OrderBy(item => item.Id).ToList();
        DeleteItemAsync(store, 2).GetAwaiter().GetResult();

        foreach (var item in items)
        {
            if (delete)
            {
                stale.Items.Remove(item);
            }
            else
            {
                item.Name += "-updated";
            }
        }

        var exception = Assert.Throws<DbUpdateConcurrencyException>(() => stale.SaveChanges());
        Assert.Equal(2, Assert.IsType<Item>(Assert.Single(exception.Entries).Entity).Id);

        using var verification = store.Create();
        Assert.Equal(["one", "three"], verification.Items.OrderBy(item => item.Id).Select(item => item.Name).ToList());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batched_writes_without_an_EF_transaction_still_run_in_one(bool duckLake)
    {
        await using var store = new Store(duckLake, DbPath);
        await SeedAsync(store);

        var transactions = new TransactionCounter();
        await using (var context = store.Create(transactionInterceptor: transactions))
        {
            context.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
            foreach (var item in await context.Items.ToListAsync())
            {
                item.Name += "-updated";
            }

            Assert.Equal(3, await context.SaveChangesAsync());
        }

        Assert.Equal(1, transactions.Started);
        Assert.Equal(1, transactions.Committed);

        await using var stale = store.Create(transactionInterceptor: transactions);
        stale.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
        var items = await stale.Items.ToListAsync();
        await DeleteItemAsync(store, 2);
        stale.Items.RemoveRange(items);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        Assert.Equal(2, transactions.Started);
        Assert.Equal(1, transactions.Committed);

        await using var verification = store.Create();
        Assert.Equal([1, 3], await verification.Items.OrderBy(item => item.Id).Select(item => item.Id).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batched_composite_key_update_of_a_deleted_row_throws(bool duckLake)
    {
        await using var store = new Store(duckLake, DbPath);
        await using (var context = store.Create())
        {
            await context.Database.EnsureCreatedAsync();
            context.Composites.AddRange(
                new CompositeItem { KeyA = 1, KeyB = 1, Name = "a" },
                new CompositeItem { KeyA = 1, KeyB = 2, Name = "b" });
            await context.SaveChangesAsync();
        }

        await using var stale = store.Create();
        var composites = await stale.Composites.ToListAsync();
        await using (var other = store.Create())
        {
            other.Composites.Remove(await other.Composites.SingleAsync(composite => composite.KeyB == 2));
            await other.SaveChangesAsync();
        }

        foreach (var composite in composites)
        {
            composite.Name += "-updated";
        }

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());

        await using var verification = store.Create();
        Assert.Equal("a", (await verification.Composites.SingleAsync()).Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batched_delete_of_a_deleted_row_throws_and_deletes_nothing(bool duckLake)
    {
        await using var store = new Store(duckLake, DbPath);
        await SeedAsync(store);

        await using var stale = store.Create();
        var items = await stale.Items.ToListAsync();
        await DeleteItemAsync(store, 2);

        stale.Items.RemoveRange(items);

        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        Assert.Equal(2, Assert.IsType<Item>(Assert.Single(exception.Entries).Entity).Id);

        await using var verification = store.Create();
        Assert.Equal([1, 3], await verification.Items.OrderBy(item => item.Id).Select(item => item.Id).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batched_update_with_a_stale_concurrency_token_throws(bool duckLake)
    {
        await using var store = new Store(duckLake, DbPath);
        await using (var context = store.Create())
        {
            await context.Database.EnsureCreatedAsync();
            context.Versioned.AddRange(
                new VersionedItem { Id = 1, Name = "one", Version = 1 },
                new VersionedItem { Id = 2, Name = "two", Version = 1 });
            await context.SaveChangesAsync();
        }

        await using var stale = store.Create();
        var items = await stale.Versioned.OrderBy(item => item.Id).ToListAsync();
        await using (var other = store.Create())
        {
            var current = await other.Versioned.SingleAsync(item => item.Id == 2);
            current.Name = "changed";
            current.Version = 2;
            await other.SaveChangesAsync();
        }

        foreach (var item in items)
        {
            item.Name += "-updated";
            item.Version++;
        }

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());

        await using var verification = store.Create();
        Assert.Equal(
            ["one", "changed"],
            await verification.Versioned.OrderBy(item => item.Id).Select(item => item.Name).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batched_delete_with_a_stale_concurrency_token_throws(bool duckLake)
    {
        await using var store = new Store(duckLake, DbPath);
        await using (var context = store.Create())
        {
            await context.Database.EnsureCreatedAsync();
            context.Versioned.Add(new VersionedItem { Id = 1, Name = "one", Version = 1 });
            await context.SaveChangesAsync();
        }

        await using var stale = store.Create();
        var item = await stale.Versioned.SingleAsync();
        await using (var other = store.Create())
        {
            var current = await other.Versioned.SingleAsync();
            current.Version = 2;
            await other.SaveChangesAsync();
        }

        stale.Versioned.Remove(item);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());

        await using var verification = store.Create();
        Assert.Equal(1, await verification.Versioned.CountAsync());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task DuckLake_batched_write_to_a_duplicated_logical_key_throws_before_mutation(
        bool delete,
        bool hideMissingRow)
    {
        await using var store = new Store(duckLake: true, DbPath);
        await using (var context = store.Create())
        {
            await context.Database.EnsureCreatedAsync();
            // DuckLake cannot enforce the primary key, so two physical rows can share logical key 1.
            await context.Database.ExecuteSqlRawAsync(
                """INSERT INTO "Items" ("Id", "Name") VALUES (1, 'one'), (1, 'one-copy'), (2, 'two');""");
        }

        await using var stale = store.Create();
        // With key 3 absent, the duplicate of key 1 would make a plain affected-row total match the expected 2.
        var tracked = hideMissingRow
            ? new[] { new Item { Id = 1, Name = "one" }, new Item { Id = 3, Name = "three" } }
            : new[] { new Item { Id = 1, Name = "one" }, new Item { Id = 2, Name = "two" } };
        foreach (var item in tracked)
        {
            stale.Attach(item);
            if (delete)
            {
                stale.Remove(item);
            }
            else
            {
                item.Name += "-updated";
            }
        }

        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        if (!hideMissingRow)
        {
            // Only key 1 is duplicated, so only its entity is reported.
            Assert.Equal(1, Assert.IsType<Item>(Assert.Single(exception.Entries).Entity).Id);
        }

        await using var verification = store.Create();
        Assert.Equal(
            ["one", "one-copy", "two"],
            await verification.Items.OrderBy(item => item.Id).ThenBy(item => item.Name).Select(item => item.Name)
                .ToListAsync());
    }

    private static async Task SeedAsync(Store store)
    {
        await using var context = store.Create();
        await context.Database.EnsureCreatedAsync();
        context.Items.AddRange(
            new Item { Id = 1, Name = "one" },
            new Item { Id = 2, Name = "two" },
            new Item { Id = 3, Name = "three" });
        await context.SaveChangesAsync();
    }

    private static async Task DeleteItemAsync(Store store, int id)
    {
        await using var other = store.Create();
        other.Items.Remove(await other.Items.SingleAsync(item => item.Id == id));
        await other.SaveChangesAsync();
    }

    private sealed class Store(bool duckLake, string databasePath) : IAsyncDisposable, IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"ducklake-batching-{Guid.NewGuid():N}");

        public BatchingContext Create(
            DbCommandInterceptor? interceptor = null,
            DbTransactionInterceptor? transactionInterceptor = null)
        {
            var builder = new DbContextOptionsBuilder<BatchingContext>()
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            if (duckLake)
            {
                var dataPath = Path.Combine(_root, "data");
                Directory.CreateDirectory(dataPath);
                builder.UseDuckLake(
                    Path.Combine(_root, "catalog.ducklake"),
                    lake => lake.DataPath(dataPath),
                    ConfigureBatching);
            }
            else
            {
                builder.UseDuckDB($"DataSource={databasePath}", ConfigureBatching);
            }

            if (interceptor is not null)
            {
                builder.AddInterceptors(interceptor);
            }

            if (transactionInterceptor is not null)
            {
                builder.AddInterceptors(transactionInterceptor);
            }

            return new BatchingContext(builder.Options);
        }

        private static void ConfigureBatching(DuckDB.EFCoreProvider.Infrastructure.DuckDBDbContextOptionsBuilder duckDB)
            => duckDB.EnableBulkInsertBatching().EnableBulkUpdateBatching().EnableBulkDeleteBatching();

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TransactionCounter : DbTransactionInterceptor
    {
        public int Started { get; private set; }

        public int Committed { get; private set; }

        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            Started++;
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Committed++;
            return Task.CompletedTask;
        }
    }

    private sealed class BatchingContext(DbContextOptions<BatchingContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
        public DbSet<CompositeItem> Composites => Set<CompositeItem>();
        public DbSet<VersionedItem> Versioned => Set<VersionedItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>().Property(item => item.Id).ValueGeneratedNever();
            modelBuilder.Entity<CompositeItem>().HasKey(item => new { item.KeyA, item.KeyB });
            modelBuilder.Entity<VersionedItem>().Property(item => item.Id).ValueGeneratedNever();
        }
    }

    private sealed class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class CompositeItem
    {
        public int KeyA { get; set; }
        public int KeyB { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class VersionedItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        [ConcurrencyCheck]
        public int Version { get; set; }
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> CommandTexts { get; } = [];

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            CommandTexts.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CommandTexts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandTexts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}