using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Extensions.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class UpsertGeneratedValueReadBackTests : DuckDBTestBase
{
    [ConditionalFact]
    public async Task UpsertAsync_by_natural_key_reads_back_surrogate_keys_for_inserted_and_updated_rows()
    {
        await using var context = new DimensionContext(FileOptions<DimensionContext>());
        await context.Database.EnsureCreatedAsync();

        var first = new[] { new Customer { Code = "A", Name = "Alpha" }, new Customer { Code = "B", Name = "Beta" } };
        Assert.Equal(2, await context.UpsertAsync(first, customer => customer.Code));
        Assert.All(first, customer => Assert.True(customer.Id > 0));
        Assert.NotEqual(first[0].Id, first[1].Id);

        // The supplied Id is ignored for the existing row: the entity receives the stored surrogate key.
        var second = new[]
        {
            new Customer { Id = 9_999, Code = "B", Name = "Beta updated" },
            new Customer { Code = "C", Name = "Gamma" }
        };
        Assert.Equal(2, await context.UpsertAsync(second, customer => customer.Code));

        Assert.Equal(first[1].Id, second[0].Id);
        Assert.True(second[1].Id > first[1].Id);
        var stored = await context.Customers.AsNoTracking().ToDictionaryAsync(customer => customer.Code);
        Assert.Equal(3, stored.Count);
        Assert.All(first.Concat(second), customer => Assert.Equal(stored[customer.Code].Id, customer.Id));
        Assert.Equal("Beta updated", stored["B"].Name);
    }

    [ConditionalFact]
    public async Task UpsertAsync_reads_back_keys_in_input_order_across_batches()
    {
        await using var context = new DimensionContext(FileOptions<DimensionContext>());
        await context.Database.EnsureCreatedAsync();

        await context.UpsertAsync(
            Enumerable.Range(0, 20).Where(i => i % 2 == 0).Select(i => new Customer { Code = $"C{i:D2}", Name = "old" }).ToList(),
            customer => customer.Code);

        // Odd codes are new and even codes already exist, interleaved across batches of seven staged rows.
        var customers = Enumerable.Range(0, 20).Reverse().Select(i => new Customer { Code = $"C{i:D2}", Name = "new" }).ToList();
        Assert.Equal(20, await context.UpsertAsync(customers, customer => customer.Code, batchSize: 7));

        var stored = await context.Customers.AsNoTracking().ToDictionaryAsync(customer => customer.Code, customer => customer.Id);
        Assert.Equal(20, stored.Count);
        Assert.All(customers, customer => Assert.Equal(stored[customer.Code], customer.Id));
    }

    [ConditionalFact]
    public async Task UpsertAsync_gives_duplicate_natural_keys_in_the_input_the_same_surrogate_key()
    {
        await using var context = new DimensionContext(FileOptions<DimensionContext>());
        await context.Database.EnsureCreatedAsync();

        var customers = new[]
        {
            new Customer { Code = "A", Name = "first" },
            new Customer { Code = "B", Name = "other" },
            new Customer { Code = "A", Name = "last" }
        };
        Assert.Equal(3, await context.UpsertAsync(customers, customer => customer.Code));

        Assert.Equal(customers[0].Id, customers[2].Id);
        Assert.NotEqual(customers[0].Id, customers[1].Id);
        Assert.Equal("last", (await context.Customers.AsNoTracking().SingleAsync(customer => customer.Code == "A")).Name);
    }

    [ConditionalFact]
    public async Task UpsertAsync_logical_key_merge_reads_back_keys_and_leaves_rows_with_null_keys_unchanged()
    {
        await using var context = new DimensionContext(FileOptions<DimensionContext>());
        await context.Database.EnsureCreatedAsync();

        var existing = new LooseCustomer { Code = "A", Name = "existing" };
        context.Add(existing);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var customers = new[]
        {
            new LooseCustomer { Code = "A", Name = "updated" },
            new LooseCustomer { Code = "B", Name = "inserted" },
            new LooseCustomer { Id = 0, Code = null, Name = "no key" }
        };
        Assert.Equal(
            3,
            await context.UpsertAsync(customers, customer => customer.Code, DuckDBUpsertMatchMode.LogicalKeyMerge));

        Assert.Equal(existing.Id, customers[0].Id);
        Assert.True(customers[1].Id > existing.Id);

        // NULL never matches under SQL semantics, so the row is inserted but cannot be matched back to its entity.
        Assert.Equal(0, customers[2].Id);
        Assert.Equal(3, await context.LooseCustomers.CountAsync());
    }

    [ConditionalFact]
    public async Task UpsertAsync_reads_back_non_sequence_defaults_through_value_converters()
    {
        await using var context = new DimensionContext(FileOptions<DimensionContext>());
        await context.Database.EnsureCreatedAsync();

        var regions = new[] { new Region { Code = "N", Name = "north" }, new Region { Code = "S", Name = "south" } };
        Assert.Equal(2, await context.UpsertAsync(regions, region => region.Code));

        Assert.All(regions, region => Assert.NotEqual(Guid.Empty, region.Id.Value));
        var stored = await context.Regions.AsNoTracking().ToDictionaryAsync(region => region.Code, region => region.Id);
        Assert.All(regions, region => Assert.Equal(stored[region.Code], region.Id));
    }

    [ConditionalFact]
    public async Task UpsertAsync_dimension_keys_feed_a_fact_BulkInsert_with_an_enforced_foreign_key()
    {
        await using var context = new DimensionContext(FileOptions<DimensionContext>());
        await context.Database.EnsureCreatedAsync();

        var customers = Enumerable.Range(0, 10).Select(i => new Customer { Code = $"C{i}", Name = $"customer-{i}" }).ToList();
        await context.UpsertAsync(customers, customer => customer.Code);

        var orders = Enumerable.Range(0, 1_000)
            .Select(i => new Order { CustomerId = customers[i % customers.Count].Id, Amount = i })
            .ToList();
        Assert.Equal(1_000, await context.BulkInsertAsync(orders));

        var ordersPerCustomer = await context.Orders
            .GroupBy(order => order.Customer.Code)
            .Select(group => new { Code = group.Key, Count = group.Count() })
            .ToListAsync();
        Assert.Equal(customers.Count, ordersPerCustomer.Count);
        Assert.All(ordersPerCustomer, row => Assert.Equal(100, row.Count));
    }

    [ConditionalFact]
    public void Plans_read_back_only_generated_columns_left_out_of_staging()
    {
        using var context = new DimensionContext(FileOptions<DimensionContext>());

        var naturalKeyPlan = DuckDBUpsertPlanner.GetOrCreate(context, typeof(Customer), [nameof(Customer.Code)]);
        Assert.Equal<string>(["Code", "Name"], naturalKeyPlan.InsertColumns);
        Assert.Equal<string>(["Id", "CreatedRank"], naturalKeyPlan.ReadBackColumns);
        Assert.True(naturalKeyPlan.ReadsBackGeneratedValues);

        var primaryKeyPlan = DuckDBUpsertPlanner.GetOrCreate(context, typeof(Customer));
        Assert.Contains("Id", primaryKeyPlan.InsertColumns);
        Assert.Empty(primaryKeyPlan.ReadBackColumns);
        Assert.False(primaryKeyPlan.ReadsBackGeneratedValues);

        var renderer = new DuckDBUpsertSqlRenderer(context.GetService<ISqlGenerationHelper>());
        Assert.Equal(
            "SELECT count(__duckdb_upsert_stored.\"Code\"), any_value(__duckdb_upsert_stored.\"Id\"), "
            + "any_value(__duckdb_upsert_stored.\"CreatedRank\") FROM staged_rows AS __duckdb_upsert_staged "
            + "LEFT JOIN \"Customers\" AS __duckdb_upsert_stored "
            + "ON __duckdb_upsert_stored.\"Code\" = __duckdb_upsert_staged.\"Code\" "
            + "GROUP BY __duckdb_upsert_staged.rowid ORDER BY __duckdb_upsert_staged.rowid;",
            renderer.RenderReadBack(naturalKeyPlan, "staged_rows"));
    }

    [ConditionalFact]
    public async Task DuckLake_UpsertAsync_by_alternate_key_writes_supplied_keys_without_read_back()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake-upsert-read-back-{Guid.NewGuid():N}");
        var dataPath = Path.Combine(root, "data");
        Directory.CreateDirectory(Path.Combine(root, "metadata"));
        Directory.CreateDirectory(dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<DuckLakeDimensionContext>()
                .UseDuckLake(Path.Combine(root, "metadata", "catalog.ducklake"), duckLake => duckLake.DataPath(dataPath))
                .Options;
            await using var context = new DuckLakeDimensionContext(options);
            await context.Database.EnsureCreatedAsync();

            // DuckLake has no store-generated values, so every column is staged and nothing is read back.
            var plan = DuckDBUpsertPlanner.GetOrCreate(context, typeof(DuckLakeCustomer), [nameof(DuckLakeCustomer.Code)]);
            Assert.False(plan.ReadsBackGeneratedValues);

            var first = new DuckLakeCustomer { Id = 10, Code = "A", Name = "first" };
            await context.UpsertAsync([first], customer => customer.Code);
            var second = new DuckLakeCustomer { Id = 10, Code = "A", Name = "updated" };
            await context.UpsertAsync([second], customer => customer.Code);

            Assert.Equal(10, second.Id);
            var stored = await context.Customers.AsNoTracking().SingleAsync();
            Assert.Equal((10, "updated"), (stored.Id, stored.Name));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class DimensionContext(DbContextOptions<DimensionContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();

        public DbSet<Order> Orders => Set<Order>();

        public DbSet<LooseCustomer> LooseCustomers => Set<LooseCustomer>();

        public DbSet<Region> Regions => Set<Region>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(customer =>
            {
                customer.Property(c => c.Id).UseAutoIncrement();
                customer.HasAlternateKey(c => c.Code);
                customer.Property(c => c.CreatedRank).HasDefaultValue(7).ValueGeneratedOnAdd();
            });
            modelBuilder.Entity<Order>().Property(order => order.Id).UseAutoIncrement();
            modelBuilder.Entity<LooseCustomer>().Property(customer => customer.Id).UseAutoIncrement();
            modelBuilder.Entity<Region>(region =>
            {
                region.Property(r => r.Id)
                    .HasConversion(id => id.Value, value => new RegionId(value))
                    .HasDefaultValueSql("uuid()")
                    .ValueGeneratedOnAdd();
                region.HasAlternateKey(r => r.Code);
            });
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    private sealed class Customer
    {
        public int Id { get; set; }

        public string Code { get; set; } = "";

        public string Name { get; set; } = "";

        public int CreatedRank { get; set; }
    }

    private sealed class Order
    {
        public long Id { get; set; }

        public int CustomerId { get; set; }

        public Customer Customer { get; set; } = null!;

        public int Amount { get; set; }
    }

    private sealed class LooseCustomer
    {
        public long Id { get; set; }

        public string? Code { get; set; }

        public string Name { get; set; } = "";
    }

    private readonly record struct RegionId(Guid Value);

    private sealed class Region
    {
        public RegionId Id { get; set; }

        public string Code { get; set; } = "";

        public string Name { get; set; } = "";
    }

    private sealed class DuckLakeDimensionContext(DbContextOptions<DuckLakeDimensionContext> options) : DbContext(options)
    {
        public DbSet<DuckLakeCustomer> Customers => Set<DuckLakeCustomer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DuckLakeCustomer>(customer =>
            {
                customer.Property(c => c.Id).ValueGeneratedNever();
                customer.HasAlternateKey(c => c.Code);
            });
        }
    }

    private sealed class DuckLakeCustomer
    {
        public int Id { get; set; }

        public string Code { get; set; } = "";

        public string Name { get; set; } = "";
    }
}