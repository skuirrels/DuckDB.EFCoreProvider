using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.ValueGeneration.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class HiLoKeyGenerationTests : DuckDBTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChanges_assigns_keys_from_one_shared_counter(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        await using (var context = store.CreateHiLo())
        {
            await context.Database.EnsureCreatedAsync();
            context.AddRange(new Order { Name = "a" }, new Order { Name = "b" }, new Customer { Name = "c" });
            await context.SaveChangesAsync();
        }

        await using var verify = store.CreateHiLo();
        var keys = (await verify.Orders.Select(order => (long)order.Id).ToListAsync())
            .Concat(await verify.Customers.Select(customer => customer.Id).ToListAsync())
            .Order()
            .ToArray();
        Assert.Equal([1L, 2L, 3L], keys);
        Assert.Equal(1L + BlockSize, await store.ReadCounterAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_new_counter_starts_above_keys_already_stored(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        await using (var manual = store.CreateManual())
        {
            await manual.Database.EnsureCreatedAsync();
            manual.AddRange(new Order { Id = 41, Name = "hand" }, new Customer { Id = 7, Name = "hand" });
            await manual.SaveChangesAsync();
        }

        await using var context = store.CreateHiLo();
        var order = new Order { Name = "generated" };
        context.Add(order);
        await context.SaveChangesAsync();

        Assert.Equal(42, order.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rolled_back_transaction_never_returns_its_block(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        int rolledBack;
        await using (var context = store.CreateHiLo())
        {
            await context.Database.EnsureCreatedAsync();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var order = new Order { Name = "discarded" };
            context.Add(order);
            await context.SaveChangesAsync();
            rolledBack = order.Id;
            await transaction.RollbackAsync();
        }

        // The block was reserved outside the caller's transaction, so the counter kept it.
        Assert.Equal(1L + BlockSize, await store.ReadCounterAsync());

        await using var next = store.CreateHiLo();
        var kept = new Order { Name = "kept" };
        next.Add(kept);
        await next.SaveChangesAsync();
        Assert.True(kept.Id > rolledBack);
        Assert.Equal(0, await next.Orders.CountAsync(order => order.Id == rolledBack));
    }

    // A local DuckLake catalog is single-client, so concurrent DuckLake writers are covered by
    // DuckLakeExternalIntegrationTests against a PostgreSQL catalog.
    [Fact]
    public async Task Concurrent_reservations_hand_out_disjoint_blocks()
    {
        await using var store = Store.Create(duckLake: false, DbPath);
        await using (var context = store.CreateHiLo())
        {
            await context.Database.EnsureCreatedAsync();
        }

        const int writers = 6;
        const int reservationsPerWriter = 4;
        const int blockSize = 3;
        var lows = await Task.WhenAll(Enumerable.Range(0, writers).Select(_ => Task.Run(async () =>
        {
            await using var context = store.CreateHiLo();
            var allocator = context.GetService<DuckDBHiLoBlockAllocator>();
            var counter = DuckDBHiLoCounter.For(context.Model.FindEntityType(typeof(Order))!.FindProperty(nameof(Order.Id))!);
            var reserved = new List<long>();
            for (var i = 0; i < reservationsPerWriter; i++)
            {
                reserved.Add(await allocator.AllocateAsync(counter, blockSize, CancellationToken.None));
            }

            return reserved;
        })));

        var values = lows.SelectMany(reserved => reserved)
            .SelectMany(low => Enumerable.Range(0, blockSize).Select(offset => low + offset))
            .ToArray();
        Assert.Equal(writers * reservationsPerWriter * blockSize, values.Distinct().Count());
        Assert.Equal(1L + writers * reservationsPerWriter * blockSize, await store.ReadCounterAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Contexts_on_the_same_database_share_a_block(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        var keys = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            await using var context = store.CreateHiLo();
            if (i == 0)
            {
                await context.Database.EnsureCreatedAsync();
            }

            var order = new Order { Name = $"o{i}" };
            context.Add(order);
            await context.SaveChangesAsync();
            keys.Add(order.Id);
        }

        Assert.Equal([1, 2, 3], keys);
        Assert.Equal(1L + BlockSize, await store.ReadCounterAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BulkInsert_assigns_keys_only_to_entities_without_one(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        await using var context = store.CreateHiLo();
        await context.Database.EnsureCreatedAsync();

        var generated = new Order { Name = "generated" };
        var explicitKey = new Order { Id = 500, Name = "explicit" };
        Assert.Equal(2, await context.BulkInsertAsync([generated, explicitKey]));
        Assert.Equal(1, generated.Id);
        Assert.Equal(500, explicitKey.Id);

        Assert.Equal(1, context.BulkInsert([new Customer { Name = "sync" }]));
        Assert.Equal(
            ["explicit", "generated"],
            await context.Orders.OrderBy(order => order.Name).Select(order => order.Name).ToListAsync());
        Assert.Equal(2L, await context.Customers.Select(customer => customer.Id).SingleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upsert_assigns_keys_to_new_rows_and_reads_stored_keys_back_on_an_alternate_target(bool duckLake)
    {
        await using var store = Store.Create(duckLake, DbPath);
        await using var context = store.CreateHiLo();
        await context.Database.EnsureCreatedAsync();

        // A primary-key target with no key is an insert, so it receives a new key.
        var inserted = new Order { Name = "pk target" };
        Assert.Equal(1, await context.UpsertAsync([inserted]));
        Assert.Equal(1, inserted.Id);

        var existing = new Customer { Name = "existing" };
        context.Add(existing);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var matched = new Customer { Name = "existing" };
        var added = new Customer { Name = "added" };
        Assert.Equal(2, await context.UpsertAsync([matched, added], customer => customer.Name));
        Assert.Equal(existing.Id, matched.Id);
        Assert.True(added.Id > existing.Id);

        var stored = await context.Customers.AsNoTracking().ToDictionaryAsync(customer => customer.Name, customer => customer.Id);
        Assert.Equal(existing.Id, stored["existing"]);
        Assert.Equal(added.Id, stored["added"]);
    }

    [Fact]
    public void Model_level_HiLo_replaces_the_default_but_keeps_explicit_configuration()
    {
        using var context = new ExplicitContext(FileOptions<ExplicitContext>());
        var entity = context.Model.FindEntityType(typeof(Explicit))!;

        Assert.Equal(DuckDBValueGenerationStrategy.HiLo, entity.FindProperty(nameof(Explicit.Id))!.GetValueGenerationStrategy());
        Assert.Equal(
            DuckDBValueGenerationStrategy.AutoIncrement,
            entity.FindProperty(nameof(Explicit.Sequence))!.GetValueGenerationStrategy());
        Assert.Equal("custom_counter", entity.FindProperty(nameof(Explicit.Id))!.GetHiLoTableName());
        Assert.Equal(7, entity.FindProperty(nameof(Explicit.Id))!.GetHiLoBlockSize());
    }

    [Fact]
    public void HiLo_on_a_non_integer_property_is_rejected_during_model_validation()
    {
        using var context = new InvalidContext(FileOptions<InvalidContext>());
        var exception = Assert.Throws<InvalidOperationException>(() => context.Model);
        Assert.Contains("integer entity property", exception.Message);
    }

    [Fact]
    public void HiLo_is_rejected_on_Quack_which_generates_keys_with_sequences()
    {
        var options = new DbContextOptionsBuilder<HiLoContext>()
            .UseDuckDB("Data Source=:memory:", duckDb => duckDb.UseQuack("quack:localhost:1", "token"))
            .Options;
        using var context = new HiLoContext(options);
        var exception = Assert.Throws<InvalidOperationException>(() => context.Model);
        Assert.Contains("does not support Hi-Lo", exception.Message);
    }

    [Fact]
    public void A_block_size_set_through_the_metadata_API_is_validated()
    {
        using var context = new InvalidBlockContext(FileOptions<InvalidBlockContext>());
        var exception = Assert.Throws<InvalidOperationException>(() => context.Model);
        Assert.Contains("block size of at least 1", exception.Message);
    }

    [Fact]
    public void UseHiLo_rejects_an_empty_table_name_or_a_block_size_below_one()
    {
        var builder = new ModelBuilder();
        Assert.Throws<ArgumentException>(() => builder.UseHiLo(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.UseHiLo(blockSize: 0));
    }

    private const int BlockSize = 10;

    private sealed class Store : IAsyncDisposable
    {
        private readonly bool _duckLake;
        private readonly string _dbPath;
        private readonly string _root;

        private Store(bool duckLake, string dbPath)
        {
            _duckLake = duckLake;
            _dbPath = dbPath;
            _root = Path.Combine(Path.GetTempPath(), $"ducklake-hilo-{Guid.NewGuid():N}");
            if (duckLake)
            {
                Directory.CreateDirectory(Path.Combine(_root, "metadata"));
                Directory.CreateDirectory(Path.Combine(_root, "data"));
            }
        }

        internal static Store Create(bool duckLake, string dbPath) => new(duckLake, dbPath);

        internal HiLoContext CreateHiLo() => new(Options<HiLoContext>());

        internal ManualContext CreateManual() => new(Options<ManualContext>());

        internal async Task<long> ReadCounterAsync()
        {
            await using var context = CreateManual();
            return await context.Database.SqlQueryRaw<long>("SELECT \"NextValue\" AS \"Value\" FROM \"__EFHiLo\"")
                .SingleAsync();
        }

        private DbContextOptions<TContext> Options<TContext>()
            where TContext : DbContext
            => _duckLake
                ? new DbContextOptionsBuilder<TContext>()
                    .UseDuckLake(
                        Path.Combine(_root, "metadata", "catalog.ducklake"),
                        duckLake => duckLake.DataPath(Path.Combine(_root, "data")))
                    .Options
                : new DbContextOptionsBuilder<TContext>().UseDuckDB($"DataSource={_dbPath}").Options;

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class HiLoContext(DbContextOptions<HiLoContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseHiLo(blockSize: BlockSize);
            modelBuilder.Entity<Order>().ToTable("Orders");
            modelBuilder.Entity<Customer>(entity =>
            {
                entity.ToTable("Customers");
                entity.HasAlternateKey(customer => customer.Name);
            });
        }
    }

    private sealed class ManualContext(DbContextOptions<ManualContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("Orders");
                entity.Property(order => order.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<Customer>(entity =>
            {
                entity.ToTable("Customers");
                entity.HasAlternateKey(customer => customer.Name);
                entity.Property(customer => customer.Id).ValueGeneratedNever();
            });
        }
    }

    private sealed class ExplicitContext(DbContextOptions<ExplicitContext> options) : DbContext(options)
    {
        public DbSet<Explicit> Items => Set<Explicit>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseHiLo("custom_counter", 7);
            modelBuilder.Entity<Explicit>().Property(item => item.Sequence).UseAutoIncrement();
        }
    }

    private sealed class InvalidContext(DbContextOptions<InvalidContext> options) : DbContext(options)
    {
        public DbSet<Named> Items => Set<Named>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Named>().Property(item => item.Code).UseHiLo();
    }

    private sealed class InvalidBlockContext(DbContextOptions<InvalidBlockContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Order>().Property(order => order.Id).UseHiLo().Metadata.SetHiLoBlockSize(0);
    }

    private sealed class Order
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    private sealed class Customer
    {
        public long Id { get; set; }
        public string Name { get; set; } = null!;
    }

    private sealed class Explicit
    {
        public int Id { get; set; }
        public long Sequence { get; set; }
    }

    private sealed class Named
    {
        public int Id { get; set; }
        public string Code { get; set; } = null!;
    }
}