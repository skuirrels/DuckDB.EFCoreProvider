using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class BulkInsertKeyReservationTests : DuckDBTestBase
{
    [ConditionalFact]
    public void BulkInsert_reserves_sequence_keys_across_chunks_and_writes_them_back()
    {
        using var context = new StarContext(FileOptions<StarContext>());
        context.Database.EnsureCreated();

        // More rows than one reservation chunk, so keys come from several reservations.
        var products = Enumerable.Range(0, 20_000).Select(i => new Product { Name = $"product-{i}" }).ToList();

        Assert.Equal(20_000, context.BulkInsert(products));

        Assert.All(products, product => Assert.True(product.Id > 0));
        Assert.Equal(products.Count, products.Select(product => product.Id).Distinct().Count());

        var stored = context.Products.AsNoTracking().ToDictionary(product => product.Id, product => product.Name);
        Assert.Equal(products.Count, stored.Count);
        Assert.All(products, product => Assert.Equal(product.Name, stored[product.Id]));
    }

    [ConditionalFact]
    public void BulkInsert_keeps_explicit_keys_and_reserves_only_unset_ones()
    {
        using var context = new StarContext(FileOptions<StarContext>());
        context.Database.EnsureCreated();

        var explicitProduct = new Product { Id = 1_000_000, Name = "explicit" };
        var generatedProducts = new[] { new Product { Name = "first" }, new Product { Name = "second" } };

        Assert.Equal(3, context.BulkInsert([generatedProducts[0], explicitProduct, generatedProducts[1]]));

        Assert.Equal(1_000_000, explicitProduct.Id);
        Assert.Equal([1, 2], generatedProducts.Select(product => product.Id));
        Assert.Equal(
            ["first", "second", "explicit"],
            context.Products.AsNoTracking().OrderBy(product => product.Id).Select(product => product.Name));
    }

    [ConditionalFact]
    public async Task BulkInsert_loads_dimensions_then_facts_with_reserved_keys()
    {
        await using var context = new StarContext(FileOptions<StarContext>());
        await context.Database.EnsureCreatedAsync();

        var products = Enumerable.Range(0, 50).Select(i => new Product { Name = $"product-{i}" }).ToList();
        await context.BulkInsertAsync(products);

        // The facts reference the dimension keys reserved by the first call; the FK is enforced by DuckDB.
        var sales = Enumerable.Range(0, 5_000)
            .Select(i => new Sale { ProductId = products[i % products.Count].Id, Quantity = i })
            .ToList();
        Assert.Equal(5_000, await context.BulkInsertAsync(sales));

        Assert.All(sales, sale => Assert.True(sale.Id > 0));
        var salesPerProduct = await context.Sales
            .GroupBy(sale => sale.Product.Name)
            .Select(group => new { Name = group.Key, Count = group.Count() })
            .ToListAsync();
        Assert.Equal(products.Count, salesPerProduct.Count);
        Assert.All(salesPerProduct, row => Assert.Equal(100, row.Count));
    }

    [ConditionalFact]
    public void BulkInsert_reserved_keys_do_not_collide_with_SaveChanges_or_later_bulk_inserts()
    {
        using var context = new StarContext(FileOptions<StarContext>());
        context.Database.EnsureCreated();

        var first = new[] { new Product { Name = "bulk-1" }, new Product { Name = "bulk-2" } };
        context.BulkInsert(first);

        var tracked = new Product { Name = "tracked" };
        context.Add(tracked);
        context.SaveChanges();

        var second = new[] { new Product { Name = "bulk-3" } };
        context.BulkInsert(second);

        Assert.Equal(
            4,
            first.Select(product => product.Id)
                .Append(tracked.Id)
                .Concat(second.Select(product => product.Id))
                .Distinct()
                .Count());
        Assert.Equal(4, context.Products.Count());
    }

    [ConditionalFact]
    public void BulkInsert_reserves_non_sequence_defaults_through_value_converters()
    {
        using var context = new StarContext(FileOptions<StarContext>());
        context.Database.EnsureCreated();

        var regions = new[] { new Region { Name = "north" }, new Region { Name = "south" } };
        Assert.Equal(2, context.BulkInsert(regions));

        Assert.All(regions, region => Assert.NotEqual(Guid.Empty, region.Id.Value));
        Assert.NotEqual(regions[0].Id, regions[1].Id);
        Assert.Equal(
            regions.OrderBy(region => region.Name).Select(region => region.Id),
            context.Regions.AsNoTracking().OrderBy(region => region.Name).Select(region => region.Id).ToList());
    }

    [ConditionalFact]
    public async Task BulkInsert_rolled_back_with_its_transaction_still_assigns_keys()
    {
        await using var context = new StarContext(FileOptions<StarContext>());
        await context.Database.EnsureCreatedAsync();

        var products = new[] { new Product { Name = "rolled-back" } };
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            Assert.Equal(1, await context.BulkInsertAsync(products));
            await transaction.RollbackAsync();
        }

        Assert.True(products[0].Id > 0);
        Assert.False(await context.Products.AnyAsync());
    }

    [ConditionalFact]
    public void BulkInsert_leaves_keys_without_a_column_default_unchanged()
    {
        using var context = new StarContext(FileOptions<StarContext>());
        context.Database.EnsureCreated();

        var row = new ManualKeyRow { Id = 0, Name = "zero" };
        Assert.Equal(1, context.BulkInsert([row]));

        Assert.Equal(0, row.Id);
        Assert.Equal("zero", context.ManualKeyRows.AsNoTracking().Single(r => r.Id == 0).Name);
    }

    [ConditionalFact]
    public void DuckLake_BulkInsert_writes_supplied_keys_without_reservation()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake-bulk-keys-{Guid.NewGuid():N}");
        var dataPath = Path.Combine(root, "data");
        Directory.CreateDirectory(Path.Combine(root, "metadata"));
        Directory.CreateDirectory(dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<DuckLakeKeyContext>()
                .UseDuckLake(Path.Combine(root, "metadata", "catalog.ducklake"), duckLake => duckLake.DataPath(dataPath))
                .Options;
            using var context = new DuckLakeKeyContext(options);
            context.Database.EnsureCreated();

            var rows = new[]
            {
                new DuckLakeKeyRow { Id = 10, Name = "ten" },
                new DuckLakeKeyRow { Id = 20, Name = "twenty" }
            };
            Assert.Equal(2, context.BulkInsert(rows));

            Assert.Equal([10, 20], rows.Select(row => row.Id));
            Assert.Equal(
                ["ten", "twenty"],
                context.Rows.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Name));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class StarContext(DbContextOptions<StarContext> options) : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();

        public DbSet<Sale> Sales => Set<Sale>();

        public DbSet<Region> Regions => Set<Region>();

        public DbSet<ManualKeyRow> ManualKeyRows => Set<ManualKeyRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>().Property(product => product.Id).UseAutoIncrement();
            modelBuilder.Entity<Sale>().Property(sale => sale.Id).UseAutoIncrement();
            modelBuilder.Entity<Region>(region =>
            {
                region.Property(r => r.Id)
                    .HasConversion(id => id.Value, value => new RegionId(value))
                    .HasDefaultValueSql("uuid()")
                    .ValueGeneratedOnAdd();
            });
            modelBuilder.Entity<ManualKeyRow>().Property(row => row.Id).ValueGeneratedNever();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    private sealed class Product
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    private sealed class Sale
    {
        public long Id { get; set; }

        public int ProductId { get; set; }

        public Product Product { get; set; } = null!;

        public int Quantity { get; set; }
    }

    private readonly record struct RegionId(Guid Value);

    private sealed class Region
    {
        public RegionId Id { get; set; }

        public string Name { get; set; } = "";
    }

    private sealed class ManualKeyRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    private sealed class DuckLakeKeyContext(DbContextOptions<DuckLakeKeyContext> options) : DbContext(options)
    {
        public DbSet<DuckLakeKeyRow> Rows => Set<DuckLakeKeyRow>();
    }

    private sealed class DuckLakeKeyRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }
}