using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Query.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     End-to-end coverage for the QUALIFY ranking filters and ASOF joins, on both native DuckDB and DuckLake.
/// </summary>
public sealed class QualifyAndAsOfJoinTests : DuckDBTestBase
{
    private static readonly DateTime Day = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Unspecified);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QualifyRowNumber_keeps_the_latest_row_per_partition(bool duckLake)
    {
        await using var store = await Store.CreateSeededAsync(duckLake, DbPath);
        await using var context = store.Create();

        var query = context.Orders
            .OrderByDescending(order => order.PlacedAt)
            .QualifyRowNumber(order => order.CustomerId, n => n == 1);

        var sql = query.ToQueryString();
        Assert.Equal([5, 3, 6], query.Select(order => order.Id).ToList());
        Assert.Contains("QUALIFY row_number() OVER (PARTITION BY o.\"CustomerId\" ORDER BY o.\"PlacedAt\" DESC) = 1", sql);
        Assert.DoesNotContain("WHERE", sql);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QualifyRank_and_QualifyDenseRank_keep_ties(bool duckLake)
    {
        await using var store = await Store.CreateSeededAsync(duckLake, DbPath);
        await using var context = store.Create();

        // Customer 1 has amounts 30, 30, 10 and 20: rank gives 1, 1, 4, 3; dense rank gives 1, 1, 3, 2.
        var ranked = context.Orders
            .Where(order => order.CustomerId == 1)
            .OrderByDescending(order => order.Amount)
            .QualifyRank(order => order.CustomerId, n => n <= 2);
        var denseRanked = context.Orders
            .Where(order => order.CustomerId == 1)
            .OrderByDescending(order => order.Amount)
            .QualifyDenseRank(order => order.CustomerId, n => n == 2);

        Assert.Equal([1, 5], ranked.Select(order => order.Id).OrderBy(id => id).ToList());
        Assert.Equal([4], denseRanked.Select(order => order.Id).ToList());
        Assert.Contains("QUALIFY rank() OVER", ranked.ToQueryString());
        Assert.Contains("QUALIFY dense_rank() OVER", denseRanked.ToQueryString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Qualify_uses_every_ordering_key_and_composite_and_navigation_partitions(bool duckLake)
    {
        await using var store = await Store.CreateSeededAsync(duckLake, DbPath);
        await using var context = store.Create();

        var byRegion = context.Orders
            .OrderByDescending(order => order.Amount)
            .ThenBy(order => order.Id)
            .QualifyRowNumber(order => order.Customer!.Region, n => n == 1);
        var byCustomerAndAmount = context.Orders
            .OrderBy(order => order.PlacedAt)
            .QualifyRowNumber(order => new { order.CustomerId, order.Amount }, n => n == 1);

        var sql = byRegion.ToQueryString();
        Assert.Equal([1, 6], byRegion.Select(order => order.Id).OrderBy(id => id).ToList());
        Assert.Contains("ORDER BY o.\"Amount\" DESC, o.\"Id\")", sql);
        Assert.Contains("PARTITION BY c.\"Region\"", sql);
        Assert.Equal([1, 2, 3, 4, 6], byCustomerAndAmount.Select(order => order.Id).OrderBy(id => id).ToList());
        Assert.Contains("PARTITION BY o.\"CustomerId\", o.\"Amount\"", byCustomerAndAmount.ToQueryString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Operators_composed_after_Qualify_apply_to_the_filtered_rows(bool duckLake)
    {
        await using var store = await Store.CreateSeededAsync(duckLake, DbPath);
        await using var context = store.Create();

        // The latest orders per customer are 5 (amount 30), 3 (15) and 6 (40), so filtering after QUALIFY keeps only
        // order 3. Filtering first ranks the cheaper orders instead, so customer 1's latest becomes order 4.
        var filteredAfter = context.Orders
            .OrderByDescending(order => order.PlacedAt)
            .QualifyRowNumber(order => order.CustomerId, n => n == 1)
            .Where(order => order.Amount < 25m);
        var filteredBefore = context.Orders
            .Where(order => order.Amount < 25m)
            .OrderByDescending(order => order.PlacedAt)
            .QualifyRowNumber(order => order.CustomerId, n => n == 1);
        var counted = context.Orders
            .OrderByDescending(order => order.PlacedAt)
            .QualifyRowNumber(order => order.CustomerId, n => n <= 2)
            .CountAsync();

        Assert.Equal([3], filteredAfter.Select(order => order.Id).ToList());
        Assert.Equal([4, 3], filteredBefore.Select(order => order.Id).ToList());
        Assert.Equal(4, await counted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Qualify_after_Take_ranks_only_the_taken_rows(bool duckLake)
    {
        await using var store = await Store.CreateSeededAsync(duckLake, DbPath);
        await using var context = store.Create();

        // The three earliest orders are 6, 1 and 2; within them the latest per customer are 2 (customer 1) and 6.
        var query = context.Orders
            .OrderBy(order => order.PlacedAt)
            .Take(3)
            .OrderByDescending(order => order.PlacedAt)
            .QualifyRowNumber(order => order.CustomerId, n => n == 1);

        Assert.Equal([2, 6], query.Select(order => order.Id).ToList());
    }

    [Fact]
    public void Qualify_requires_an_OrderBy_source()
    {
        using var context = new Store(false, DbPath).Create();

        var unordered = (IOrderedQueryable<Order>)context.Orders.AsNoTracking();
        var exception = Assert.Throws<InvalidOperationException>(
            () => unordered.QualifyRowNumber(order => order.CustomerId, n => n == 1));

        Assert.Equal(DuckDBQualifyPlanner.OrderingRequired, exception.Message);
    }

    [Fact]
    public void Qualify_rejects_an_entity_partition_key()
    {
        using var context = new Store(false, DbPath).Create();

        var query = context.Orders
            .OrderBy(order => order.PlacedAt)
            .QualifyRowNumber(order => order.Customer, n => n == 1);

        var exception = Assert.Throws<InvalidOperationException>(() => query.ToQueryString());
        Assert.Contains("must be a single scalar value", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsOfJoin_matches_the_latest_inner_row_at_or_before_each_outer_row(bool duckLake)
    {
        await using var store = await Store.CreateSeededAsync(duckLake, DbPath);
        await using var context = store.Create();

        var query = context.Trades
            .AsOfJoin(
                context.Prices,
                (trade, price) => trade.Symbol == price.Symbol && trade.At >= price.At,
                (trade, price) => new { TradeId = trade.Id, PriceId = price.Id, price.Value });

        var sql = query.ToQueryString();
        var rows = query.ToList().OrderBy(row => row.TradeId).Select(row => (row.TradeId, row.PriceId, row.Value)).ToList();

        // Trade 1 precedes every ABC price, so the inner ASOF join drops it.
        Assert.Equal([(2, 1, 10m), (3, 2, 12m), (4, 3, 50m)], rows);
        Assert.Contains("ASOF JOIN \"Prices\" AS p ON t.\"Symbol\" = p.\"Symbol\" AND t.\"At\" >= p.\"At\"", sql);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeftAsOfJoin_keeps_outer_rows_without_a_match(bool duckLake)
    {
        await using var store = await Store.CreateSeededAsync(duckLake, DbPath);
        await using var context = store.Create();

        var query = context.Trades
            .LeftAsOfJoin(
                context.Prices,
                (trade, price) => price.At <= trade.At && price.Symbol == trade.Symbol,
                (trade, price) => new { TradeId = trade.Id, Price = price });

        var sql = query.ToQueryString();
        var rows = query.ToList().OrderBy(row => row.TradeId).Select(row => (row.TradeId, row.Price?.Id)).ToList();

        Assert.Equal([(1, (int?)null), (2, 1), (3, 2), (4, 3)], rows);
        Assert.Contains("ASOF LEFT JOIN \"Prices\" AS p ON t.\"Symbol\" = p.\"Symbol\" AND t.\"At\" >= p.\"At\"", sql);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsOfJoin_supports_strict_and_forward_inequalities_and_composes(bool duckLake)
    {
        await using var store = await Store.CreateSeededAsync(duckLake, DbPath);
        await using var context = store.Create();

        // Trade 2 is at the same instant as price 1, so a strict inequality finds no earlier price and drops it.
        var strict = context.Trades
            .AsOfJoin(context.Prices, (trade, price) => trade.Symbol == price.Symbol && trade.At > price.At, (trade, price) => new { trade.Id, PriceId = price.Id })
            .Where(row => row.Id >= 2)
            .OrderBy(row => row.Id);
        var next = context.Trades
            .AsOfJoin(context.Prices, (trade, price) => trade.Symbol == price.Symbol && trade.At <= price.At, (trade, price) => new { trade.Id, PriceId = price.Id })
            .OrderBy(row => row.Id);

        Assert.Equal([(3, 2), (4, 3)], strict.ToList().Select(row => (row.Id, row.PriceId)).ToList());
        Assert.Equal([(1, 1), (2, 1), (3, 4)], next.ToList().Select(row => (row.Id, row.PriceId)).ToList());
        Assert.Contains("t.\"At\" <= p.\"At\"", next.ToQueryString());
    }

    [Fact]
    public void AsOfJoin_rejects_conditions_DuckDB_cannot_run()
    {
        using var context = new Store(false, DbPath).Create();

        Assert.Throws<InvalidOperationException>(
            () => context.Trades.AsOfJoin(context.Prices, (trade, price) => trade.Symbol == price.Symbol, (trade, price) => trade));
        Assert.Throws<InvalidOperationException>(
            () => context.Trades.AsOfJoin(context.Prices, (trade, price) => trade.At >= price.At && trade.Id >= price.Id, (trade, price) => trade));
        Assert.Throws<InvalidOperationException>(
            () => context.Trades.AsOfJoin(context.Prices, (trade, price) => trade.At >= price.At || trade.Symbol == price.Symbol, (trade, price) => trade));
        Assert.Throws<InvalidOperationException>(
            () => context.Trades.AsOfJoin(context.Prices, (trade, price) => trade.At >= trade.At, (trade, price) => trade));
    }

    private sealed class Store(bool duckLake, string databasePath) : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"ducklake-qualify-{Guid.NewGuid():N}");

        public static async Task<Store> CreateSeededAsync(bool duckLake, string databasePath)
        {
            var store = new Store(duckLake, databasePath);
            await using var context = store.Create();
            await context.Database.EnsureCreatedAsync();
            context.Customers.AddRange(
                new Customer { Id = 1, Region = "North" },
                new Customer { Id = 2, Region = "South" },
                new Customer { Id = 3, Region = "South" });
            context.Orders.AddRange(
                new Order { Id = 1, CustomerId = 1, PlacedAt = Day.AddHours(1), Amount = 30m },
                new Order { Id = 2, CustomerId = 1, PlacedAt = Day.AddHours(2), Amount = 10m },
                new Order { Id = 3, CustomerId = 2, PlacedAt = Day.AddHours(3), Amount = 15m },
                new Order { Id = 4, CustomerId = 1, PlacedAt = Day.AddHours(4), Amount = 20m },
                new Order { Id = 5, CustomerId = 1, PlacedAt = Day.AddHours(5), Amount = 30m },
                new Order { Id = 6, CustomerId = 3, PlacedAt = Day.AddHours(0.5), Amount = 40m });
            context.Prices.AddRange(
                new Price { Id = 1, Symbol = "ABC", At = Day.AddHours(1), Value = 10m },
                new Price { Id = 2, Symbol = "ABC", At = Day.AddHours(3), Value = 12m },
                new Price { Id = 3, Symbol = "XYZ", At = Day.AddHours(1), Value = 50m },
                new Price { Id = 4, Symbol = "ABC", At = Day.AddHours(6), Value = 14m });
            context.Trades.AddRange(
                new Trade { Id = 1, Symbol = "ABC", At = Day.AddHours(0.5) },
                new Trade { Id = 2, Symbol = "ABC", At = Day.AddHours(1) },
                new Trade { Id = 3, Symbol = "ABC", At = Day.AddHours(4) },
                new Trade { Id = 4, Symbol = "XYZ", At = Day.AddHours(2) });
            await context.SaveChangesAsync();
            return store;
        }

        public AnalyticsContext Create()
        {
            var builder = new DbContextOptionsBuilder<AnalyticsContext>()
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            if (duckLake)
            {
                var dataPath = Path.Combine(_root, "data");
                Directory.CreateDirectory(dataPath);
                builder.UseDuckLake(Path.Combine(_root, "catalog.ducklake"), lake => lake.DataPath(dataPath));
            }
            else
            {
                builder.UseDuckDB($"DataSource={databasePath}");
            }

            return new AnalyticsContext(builder.Options);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    public sealed class AnalyticsContext(DbContextOptions<AnalyticsContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers
            => Set<Customer>();

        public DbSet<Order> Orders
            => Set<Order>();

        public DbSet<Trade> Trades
            => Set<Trade>();

        public DbSet<Price> Prices
            => Set<Price>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>().Property(customer => customer.Id).ValueGeneratedNever();
            modelBuilder.Entity<Order>().Property(order => order.Id).ValueGeneratedNever();
            modelBuilder.Entity<Trade>().Property(trade => trade.Id).ValueGeneratedNever();
            modelBuilder.Entity<Price>().Property(price => price.Id).ValueGeneratedNever();
        }
    }

    public sealed class Customer
    {
        public int Id { get; set; }

        public required string Region { get; set; }
    }

    public sealed class Order
    {
        public int Id { get; set; }

        public int CustomerId { get; set; }

        public Customer? Customer { get; set; }

        public DateTime PlacedAt { get; set; }

        public decimal Amount { get; set; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsOfJoin_accepts_filtered_and_paged_inner_queries(bool duckLake)
    {
        await using var store = await Store.CreateSeededAsync(duckLake, DbPath);
        await using var context = store.Create();

        var filtered = context.Trades
            .LeftAsOfJoin(
                context.Prices.Where(price => price.Value > 11),
                (trade, price) => trade.Symbol == price.Symbol && trade.At >= price.At,
                (trade, price) => new { trade.Id, Value = (decimal?)price!.Value });
        var paged = context.Trades
            .AsOfJoin(
                context.Prices.OrderBy(price => price.Id).Take(3),
                (trade, price) => trade.Symbol == price.Symbol && trade.At >= price.At,
                (trade, price) => new { trade.Id, PriceId = price.Id });

        var filteredSql = filtered.ToQueryString();

        Assert.Equal(
            [(1, (decimal?)null), (2, null), (3, 12m), (4, 50m)],
            filtered.ToList().OrderBy(row => row.Id).Select(row => (row.Id, row.Value)).ToList());
        Assert.Equal([(2, 1), (3, 2), (4, 3)], paged.ToList().OrderBy(row => row.Id).Select(row => (row.Id, row.PriceId)).ToList());
        Assert.Contains("ASOF LEFT JOIN (", filteredSql);
        Assert.Contains(") AS p0 ON t.\"Symbol\" = p0.\"Symbol\" AND t.\"At\" >= p0.\"At\"", filteredSql);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsOfJoin_chains_and_nests_in_subqueries(bool duckLake)
    {
        await using var store = await Store.CreateSeededAsync(duckLake, DbPath);
        await using var context = store.Create();

        var chained = context.Trades
            .AsOfJoin(context.Prices, (trade, price) => trade.Symbol == price.Symbol && trade.At >= price.At, (trade, price) => new { trade, price })
            .AsOfJoin(
                context.Prices,
                (row, next) => row.trade.Symbol == next.Symbol && row.trade.At <= next.At,
                (row, next) => new { row.trade.Id, Previous = row.price.Id, Next = next.Id });
        var nested = context.Trades
            .Where(trade => context.Trades
                .AsOfJoin(context.Prices, (other, price) => other.Symbol == price.Symbol && other.At >= price.At, (other, price) => other.Id)
                .Contains(trade.Id))
            .Select(trade => trade.Id);

        Assert.Equal(
            [(2, 1, 1), (3, 2, 4)],
            chained.ToList().OrderBy(row => row.Id).Select(row => (row.Id, row.Previous, row.Next)).ToList());
        Assert.Equal([2, 3, 4], nested.ToList().Order().ToList());
        Assert.Equal(2, CountOccurrences(chained.ToQueryString(), "ASOF JOIN"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Qualify_composes_with_filters_grouping_and_correlated_subqueries(bool duckLake)
    {
        await using var store = await Store.CreateSeededAsync(duckLake, DbPath);
        await using var context = store.Create();

        var filtered = context.Orders
            .Where(order => order.Id > 1)
            .OrderByDescending(order => order.PlacedAt)
            .QualifyRowNumber(order => order.CustomerId, n => n == 1)
            .Select(order => order.Id);
        var latestPerCustomer = context.Customers
            .OrderBy(customer => customer.Id)
            .Select(customer => context.Orders
                .Where(order => order.CustomerId == customer.Id)
                .OrderByDescending(order => order.PlacedAt)
                .QualifyRowNumber(order => order.CustomerId, n => n == 1)
                .Select(order => order.Id)
                .FirstOrDefault());

        var filteredSql = filtered.ToQueryString();

        Assert.Equal([3, 5, 6], filtered.ToList().Order().ToList());
        Assert.Equal([5, 3, 6], latestPerCustomer.ToList());
        Assert.Contains("WHERE o.\"Id\" > 1\n    QUALIFY row_number()", filteredSql.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Qualify_with_an_empty_partition_ranks_all_rows(bool duckLake)
    {
        await using var store = await Store.CreateSeededAsync(duckLake, DbPath);
        await using var context = store.Create();

        var query = context.Orders
            .OrderByDescending(order => order.PlacedAt)
            .QualifyRowNumber(order => new { }, n => n <= 2)
            .Select(order => order.Id);

        Assert.Equal([5, 4], query.ToList());
        Assert.Contains("QUALIFY row_number() OVER (ORDER BY o.\"PlacedAt\" DESC) <= 2", query.ToQueryString());
    }

    [Fact]
    public async Task Qualify_rejects_a_predicate_that_ignores_the_rank()
    {
        await using var store = await Store.CreateSeededAsync(duckLake: false, DbPath);
        await using var context = store.Create();
        var keep = true;

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Orders.OrderBy(order => order.PlacedAt).QualifyRowNumber(order => order.CustomerId, n => keep));

        Assert.Equal(DuckDBQualifyPlanner.RankRequired, exception.Message);
    }

    private static int CountOccurrences(string text, string value)
        => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;

    public sealed class Trade
    {
        public int Id { get; set; }

        public required string Symbol { get; set; }

        public DateTime At { get; set; }
    }

    public sealed class Price
    {
        public int Id { get; set; }

        public required string Symbol { get; set; }

        public DateTime At { get; set; }

        public decimal Value { get; set; }
    }
}