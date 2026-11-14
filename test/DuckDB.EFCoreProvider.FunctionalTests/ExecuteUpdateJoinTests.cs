using System.Data.Common;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     <c>ExecuteUpdate</c> sources that join more than one table render their inner joins as cross joins with the
///     join predicates in <c>WHERE</c>, because DuckDB cannot bind a later <c>JOIN ... ON</c> that references the
///     update target.
/// </summary>
public sealed class ExecuteUpdateJoinTests : DuckDBTestBase
{
    [ConditionalFact]
    public async Task Update_filtered_through_two_navigations_moves_join_predicates_to_where()
    {
        var interceptor = new CommandCaptureInterceptor();
        await using var context = new ShopContext(
            new DbContextOptionsBuilder<ShopContext>(FileOptions<ShopContext>()).AddInterceptors(interceptor).Options);

        await AssertUpdateThroughTwoNavigationsAsync(context, interceptor);
    }

    [ConditionalFact]
    public async Task Update_filtered_through_two_navigations_on_DuckLake()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake_update_joins_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "data"));

        try
        {
            var interceptor = new CommandCaptureInterceptor();
            var options = new DbContextOptionsBuilder<ShopContext>()
                .UseDuckLake(Path.Combine(root, "metadata.ducklake"), duckLake => duckLake.DataPath(Path.Combine(root, "data")))
                .AddInterceptors(interceptor)
                .Options;

            await using var context = new ShopContext(options);

            await AssertUpdateThroughTwoNavigationsAsync(context, interceptor);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [ConditionalFact]
    public async Task Update_filtered_through_one_navigation_keeps_the_existing_shape()
    {
        var interceptor = new CommandCaptureInterceptor();
        await using var context = new ShopContext(
            new DbContextOptionsBuilder<ShopContext>(FileOptions<ShopContext>()).AddInterceptors(interceptor).Options);
        await SeedAsync(context, interceptor);

        var updated = await context.Set<LineItem>()
            .Where(item => item.Product!.Discontinued)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Quantity, 0));

        Assert.Equal(2, updated);
        Assert.Equal(
            """
            UPDATE "LineItem" AS l
            SET "Quantity" = $p
            FROM "Product" AS p
            WHERE l."ProductId" = p."Id" AND p."Discontinued"
            """,
            SingleUpdate(interceptor));
    }

    private static async Task AssertUpdateThroughTwoNavigationsAsync(ShopContext context, CommandCaptureInterceptor interceptor)
    {
        await SeedAsync(context, interceptor);

        var updated = await context.Set<LineItem>()
            .Where(item => item.Product!.Discontinued && item.Order!.Region == "North")
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Quantity, 0));

        Assert.Equal(1, updated);
        Assert.Equal(
            """
            UPDATE "LineItem" AS l
            SET "Quantity" = $p
            FROM "Product" AS p
            CROSS JOIN "Order" AS o
            WHERE l."ProductId" = p."Id" AND l."OrderId" = o."Id" AND p."Discontinued" AND o."Region" = 'North'
            """,
            SingleUpdate(interceptor));

        context.ChangeTracker.Clear();
        var quantities = await context.Set<LineItem>().OrderBy(item => item.Id).Select(item => item.Quantity).ToListAsync();

        // Only the line for a discontinued product on a northern order is cleared.
        Assert.Equal([0, 5, 7, 9], quantities);
    }

    private static async Task SeedAsync(ShopContext context, CommandCaptureInterceptor interceptor)
    {
        await context.Database.EnsureCreatedAsync();

        var north = new Order { Id = 1, Region = "North" };
        var south = new Order { Id = 2, Region = "South" };
        var retired = new Product { Id = 1, Discontinued = true };
        var current = new Product { Id = 2, Discontinued = false };
        context.AddRange(
            new LineItem { Id = 1, Order = north, Product = retired, Quantity = 3 },
            new LineItem { Id = 2, Order = north, Product = current, Quantity = 5 },
            new LineItem { Id = 3, Order = south, Product = retired, Quantity = 7 },
            new LineItem { Id = 4, Order = south, Product = current, Quantity = 9 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        interceptor.CommandTexts.Clear();
    }

    private static string SingleUpdate(CommandCaptureInterceptor interceptor)
        => Assert.Single(interceptor.CommandTexts, text => text.StartsWith("UPDATE ", StringComparison.Ordinal))
            .ReplaceLineEndings("\n")
            .Trim();

    private sealed class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().Property(order => order.Id).ValueGeneratedNever();
            modelBuilder.Entity<Product>().Property(product => product.Id).ValueGeneratedNever();
            modelBuilder.Entity<LineItem>().Property(item => item.Id).ValueGeneratedNever();
            // Non-nullable foreign keys make both relationships required, so the navigations become inner joins.
            modelBuilder.Entity<LineItem>().HasOne(item => item.Order).WithMany().HasForeignKey(item => item.OrderId);
            modelBuilder.Entity<LineItem>().HasOne(item => item.Product).WithMany().HasForeignKey(item => item.ProductId);
        }
    }

    private sealed class Order
    {
        public int Id { get; set; }
        public required string Region { get; set; }
    }

    private sealed class Product
    {
        public int Id { get; set; }
        public bool Discontinued { get; set; }
    }

    private sealed class LineItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public Order? Order { get; set; }
        public Product? Product { get; set; }
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> CommandTexts { get; } = [];

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CommandTexts.Add(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}