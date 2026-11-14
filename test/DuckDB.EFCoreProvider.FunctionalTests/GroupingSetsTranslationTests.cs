using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Extensions.DbFunctionsExtensions;
using DuckDB.EFCoreProvider.Query.Internal;
using System.Linq.Expressions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class GroupingSetsTranslationTests : DuckDBTestBase
{
    [ConditionalFact]
    public void GroupByRollup_returns_detail_subtotal_and_grand_total_rows()
    {
        using var context = CreateSeededContext();

        var query = context.Sales
            .GroupByRollup(sale => new { sale.Region, sale.Product!.Category })
            .Select(group => new
            {
                group.Key.Region,
                group.Key.Category,
                Total = group.Sum(sale => sale.Amount),
                Level = EF.Functions.Grouping(group.Key.Region, group.Key.Category)
            });

        var sql = query.ToQueryString();
        var rows = query.ToList()
            .Select(row => (row.Region, row.Category, row.Total, row.Level))
            .OrderBy(row => row.Level)
            .ThenBy(row => row.Region)
            .ThenBy(row => row.Category)
            .ToList();

        Assert.Equal(
            [
                ("North", "Hardware", 10m, 0),
                ("North", "Software", 20m, 0),
                ("South", "Hardware", 70m, 0),
                ("North", null, 30m, 1),
                ("South", null, 70m, 1),
                (null, null, 100m, 3)
            ],
            rows);
        Assert.Contains("GROUP BY ROLLUP (s.\"Region\", p.\"Category\")", sql);
        Assert.Contains("CAST(grouping(s.\"Region\", p.\"Category\") AS INTEGER)", sql);
    }

    [ConditionalFact]
    public void GroupByCube_returns_every_combination_of_key_members()
    {
        using var context = CreateSeededContext();

        var query = context.Sales
            .GroupByCube(sale => new { sale.Region, Year = (int?)sale.Year })
            .Select(group => new { group.Key.Region, group.Key.Year, Total = group.Sum(sale => sale.Amount) });

        var sql = query.ToQueryString();
        var rows = query.ToList()
            .Select(row => (row.Region, row.Year, row.Total))
            .OrderBy(row => row.Region is null)
            .ThenBy(row => row.Region)
            .ThenBy(row => row.Year is null)
            .ThenBy(row => row.Year)
            .ToList();

        Assert.Equal(
            [
                ("North", 2025, 30m),
                ("North", null, 30m),
                ("South", 2025, 30m),
                ("South", 2026, 40m),
                ("South", null, 70m),
                (null, 2025, 60m),
                (null, 2026, 40m),
                (null, null, 100m)
            ],
            rows);
        Assert.Contains("GROUP BY CUBE (s.\"Region\", s.\"Year\")", sql);
    }

    [ConditionalFact]
    public void GroupByGroupingSets_returns_only_the_listed_sets()
    {
        using var context = CreateSeededContext();

        var query = context.Sales
            .GroupByGroupingSets(
                sale => new { sale.Region, Year = (int?)sale.Year },
                key => key.Region,
                key => new { key.Year },
                key => new { })
            .Select(group => new
            {
                group.Key.Region,
                group.Key.Year,
                Count = group.Count(),
                RegionRolledUp = EF.Functions.Grouping(group.Key.Region)
            });

        var sql = query.ToQueryString();
        var rows = query.ToList()
            .Select(row => (row.Region, row.Year, row.Count, row.RegionRolledUp))
            .OrderBy(row => row.Region is null)
            .ThenBy(row => row.Region)
            .ThenBy(row => row.Year is null)
            .ThenBy(row => row.Year)
            .ToList();

        Assert.Equal(
            [
                ("North", null, 2, 0),
                ("South", null, 2, 0),
                (null, 2025, 3, 1),
                (null, 2026, 1, 1),
                (null, null, 4, 1)
            ],
            rows);
        Assert.Contains("GROUP BY GROUPING SETS ((s.\"Region\"), (s.\"Year\"), ())", sql);
    }

    [ConditionalFact]
    public void Grouping_distinguishes_a_null_key_value_from_a_subtotal()
    {
        using var context = CreateSeededContext();

        var rows = context.Sales
            .GroupByRollup(sale => sale.Channel)
            .Select(group => new { Channel = group.Key, Count = group.Count(), IsTotal = EF.Functions.Grouping(group.Key) })
            .ToList()
            .OrderBy(row => row.IsTotal)
            .ThenBy(row => row.Channel)
            .Select(row => (row.Channel, row.Count, row.IsTotal))
            .ToList();

        Assert.Equal([(null, 1, 0), ("Online", 3, 0), (null, 4, 1)], rows);
    }

    [ConditionalFact]
    public void Grouping_sets_compose_with_having_ordering_paging_and_parameters()
    {
        using var context = CreateSeededContext();
        var minimumYear = 2025;

        var query = context.Sales
            .Where(sale => sale.Year >= minimumYear)
            .GroupByRollup(sale => new { sale.Region })
            .Where(group => group.Sum(sale => sale.Amount) > 25m)
            .Select(group => new { group.Key.Region, Total = group.Sum(sale => sale.Amount) })
            .OrderByDescending(row => row.Total)
            .Take(2);

        var rows = query.ToList();
        var sql = query.ToQueryString();

        Assert.Equal([(null, 100m), ("South", 70m)], rows.Select(row => (row.Region, row.Total)).ToList());
        Assert.Contains("GROUP BY ROLLUP (s.\"Region\")", sql);
        Assert.Contains("HAVING", sql);

        minimumYear = 2026;
        Assert.Equal(
            [(null, 40m), ("South", 40m)],
            query.ToList().Select(row => (row.Region, row.Total)).OrderBy(row => row.Region).ToList());
    }

    [ConditionalFact]
    public async Task Grouping_sets_can_be_counted_and_executed_asynchronously()
    {
        await using var context = CreateSeededContext();

        var count = await context.Sales
            .GroupByRollup(sale => new { sale.Region, sale.Product!.Category })
            .Select(group => new { group.Key.Region, group.Key.Category })
            .CountAsync();

        var totals = await context.Sales
            .GroupByCube(sale => sale.Region)
            .Select(group => group.Sum(sale => sale.Amount))
            .ToListAsync();

        Assert.Equal(6, count);
        Assert.Equal([30m, 70m, 100m], totals.Order().ToList());
    }

    [ConditionalFact]
    public void Plain_GroupBy_is_unchanged()
    {
        using var context = CreateSeededContext();

        var sql = context.Sales
            .GroupBy(sale => sale.Region)
            .Select(group => new { group.Key, Total = group.Sum(sale => sale.Amount) })
            .ToQueryString();

        Assert.Contains("GROUP BY s.\"Region\"", sql);
        Assert.DoesNotContain("ROLLUP", sql);
    }

    [ConditionalFact]
    public void Non_nullable_value_key_members_are_rejected_when_the_query_is_built()
    {
        using var context = CreateSeededContext();

        var rollup = Assert.Throws<InvalidOperationException>(
            () => context.Sales.GroupByRollup(sale => new { sale.Region, sale.Year }));
        var scalar = Assert.Throws<InvalidOperationException>(() => context.Sales.GroupByCube(sale => sale.Year));

        Assert.Contains("'Year'", rollup.Message);
        Assert.Contains("(Int32?)", rollup.Message);
        Assert.Contains("nullable key", scalar.Message);
    }

    [ConditionalFact]
    public void A_non_nullable_member_present_in_every_grouping_set_is_accepted()
    {
        using var context = CreateSeededContext();

        var rows = context.Sales
            .GroupByGroupingSets(
                sale => new { sale.Year, sale.Region },
                key => new { key.Year, key.Region },
                key => key.Year)
            .Select(group => new { group.Key.Year, group.Key.Region, Count = group.Count() })
            .ToList();

        Assert.Equal(5, rows.Count);
        Assert.Equal(4, rows.Where(row => row.Region is not null).Sum(row => row.Count));
    }

    [ConditionalFact]
    public void Querying_group_elements_is_rejected()
    {
        using var context = CreateSeededContext();

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Sales
                .GroupByRollup(sale => sale.Region)
                .Select(group => new { group.Key, First = group.OrderBy(sale => sale.Id).Select(sale => sale.Id).First() })
                .ToList());

        var collection = Assert.Throws<InvalidOperationException>(
            () => context.Sales
                .GroupByRollup(sale => sale.Region)
                .Select(group => new { group.Key, Items = group.ToList() })
                .ToList());

        Assert.Equal(DuckDBGroupingSetsPostprocessor.ElementAccessNotSupported, exception.Message);
        Assert.Equal(DuckDBGroupingSetsPostprocessor.ElementAccessNotSupported, collection.Message);
    }

    [ConditionalFact]
    public void Only_the_multi_level_grouping_is_rewritten_when_combined_with_a_plain_grouping()
    {
        using var context = CreateSeededContext();

        var query = context.Sales
            .GroupByRollup(sale => sale.Region)
            .Select(group => new { Region = (string?)group.Key, Total = group.Sum(sale => sale.Amount) })
            .Concat(
                context.Sales
                    .GroupBy(sale => sale.Region)
                    .Select(group => new { Region = (string?)group.Key, Total = group.Sum(sale => sale.Amount) }));

        var sql = query.ToQueryString();
        var rows = query.ToList()
            .Select(row => (row.Region, row.Total))
            .OrderBy(row => row.Region)
            .ThenBy(row => row.Total)
            .ToList();

        Assert.Equal([(null, 100m), ("North", 30m), ("North", 30m), ("South", 70m), ("South", 70m)], rows);
        Assert.Contains("GROUP BY ROLLUP (s.\"Region\")", sql);
        Assert.Contains("GROUP BY s0.\"Region\"", sql);
    }

    [ConditionalFact]
    public void Entity_key_members_are_rejected()
    {
        using var context = CreateSeededContext();

        Assert.Throws<InvalidOperationException>(
            () => context.Sales
                .GroupByRollup(sale => new { sale.Product })
                .Select(group => group.Count())
                .ToList());
    }

    [ConditionalFact]
    public void Duplicate_key_members_are_rejected()
    {
        using var context = CreateSeededContext();

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Sales
                .GroupByRollup(sale => new { First = sale.Region, Second = sale.Region })
                .Select(group => group.Count())
                .ToList());

        Assert.Contains("more than once", exception.Message);
    }

    [ConditionalFact]
    public void Nested_key_members_are_rejected_when_the_query_is_built()
    {
        using var context = CreateSeededContext();

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Sales.GroupByRollup(sale => new { sale.Region, Period = new { sale.Year } }));

        Assert.Contains("'Period'", exception.Message);
    }

    [ConditionalFact]
    public void Computed_and_constant_key_members_are_grouped_after_pushdown()
    {
        using var context = CreateSeededContext();

        var query = context.Sales
            .GroupByRollup(sale => new { Upper = sale.Region.ToUpper(), Scope = "All" })
            .Select(group => new { group.Key.Upper, group.Key.Scope, Total = group.Sum(sale => sale.Amount) });

        var sql = query.ToQueryString();
        var rows = query.ToList()
            .Select(row => (row.Upper, row.Scope, row.Total))
            .OrderBy(row => row.Upper is null)
            .ThenBy(row => row.Upper)
            .ThenBy(row => row.Scope is null)
            .ToList();

        Assert.Equal(
            [
                ("NORTH", "All", 30m),
                ("NORTH", null, 30m),
                ("SOUTH", "All", 70m),
                ("SOUTH", null, 70m),
                (null, null, 100m)
            ],
            rows);
        Assert.Contains("GROUP BY ROLLUP (s0.\"Upper\", s0.\"Scope\")", sql);
    }

    [ConditionalFact]
    public void A_paged_source_is_grouped_as_a_subquery()
    {
        using var context = CreateSeededContext();

        var query = context.Sales
            .OrderBy(sale => sale.Id)
            .Take(3)
            .GroupByRollup(sale => sale.Region)
            .Select(group => new { Region = group.Key, Total = group.Sum(sale => sale.Amount) });

        var sql = query.ToQueryString();
        var rows = query.ToList().Select(row => (row.Region, row.Total)).OrderBy(row => row.Region).ToList();

        Assert.Equal([(null, 60m), ("North", 30m), ("South", 30m)], rows);
        Assert.Contains("LIMIT", sql);
        Assert.Contains("GROUP BY ROLLUP (s0.\"Region\")", sql);
    }

    [ConditionalFact]
    public void Grouping_sets_execute_against_a_DuckLake_catalog()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake_grouping_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "data"));

        try
        {
            var options = new DbContextOptionsBuilder<SalesContext>()
                .UseDuckLake(Path.Combine(root, "metadata.ducklake"), duckLake => duckLake.DataPath(Path.Combine(root, "data")))
                .Options;

            using var context = new SalesContext(options);
            context.Database.EnsureCreated();
            Seed(context);

            var rows = context.Sales
                .GroupByRollup(sale => new { sale.Region, sale.Product!.Category })
                .Select(group => new { group.Key.Region, group.Key.Category, Total = group.Sum(sale => sale.Amount) })
                .ToList();

            Assert.Equal(6, rows.Count);
            Assert.Equal(100m, rows.Single(row => row.Region is null).Total);
            Assert.Equal(30m, rows.Single(row => row is { Region: "North", Category: null }).Total);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(PlannedSets))]
    public void Planner_resolves_grouping_set_selectors_to_key_member_positions(
        LambdaExpression groupingSet,
        int[] expected)
    {
        Expression<Func<SaleRow, Key>> keySelector = sale => new Key { Region = sale.Region, Channel = sale.Channel };

        var plan = DuckDBGroupingSetsPlanner.Plan(DuckDBGroupingSetsKind.GroupingSets, keySelector, [groupingSet]);

        Assert.Equal(2, plan.KeyMemberCount);
        Assert.Equal(expected, plan.Sets.Single());
    }

    public static TheoryData<LambdaExpression, int[]> PlannedSets()
        => new()
        {
            { (Expression<Func<Key, object?>>)(key => new { key.Channel, key.Region }), [1, 0] },
            { (Expression<Func<Key, object?>>)(key => key.Channel), [1] },
            { (Expression<Func<Key, object?>>)(key => key), [0, 1] },
            { (Expression<Func<Key, object?>>)(key => new { }), [] },
            { (Expression<Func<Key, object?>>)(key => null), [] }
        };

    [ConditionalFact]
    public void Planner_rejects_sets_that_do_not_select_key_members()
    {
        Expression<Func<SaleRow, object>> keySelector = sale => new { sale.Region, sale.Channel };
        Expression<Func<object, object?>> notAMember = key => key.ToString();
        Expression<Func<SaleRow, object>> emptyKey = sale => new { };

        Assert.Throws<InvalidOperationException>(
            () => DuckDBGroupingSetsPlanner.Plan(DuckDBGroupingSetsKind.GroupingSets, keySelector, [notAMember]));
        Assert.Throws<InvalidOperationException>(
            () => DuckDBGroupingSetsPlanner.Plan(DuckDBGroupingSetsKind.Rollup, emptyKey, []));
        Assert.Throws<ArgumentException>(
            () => new DuckDBGroupingSetsPlan(DuckDBGroupingSetsKind.GroupingSets, 1, []));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DuckDBGroupingSetsPlan(DuckDBGroupingSetsKind.GroupingSets, 1, [[1]]));
    }

    [ConditionalFact]
    public void Grouping_sets_operators_reject_missing_sets()
    {
        using var context = CreateSeededContext();

        Assert.Throws<ArgumentException>(() => context.Sales.GroupByGroupingSets(sale => sale.Region));
    }

    private SalesContext CreateSeededContext()
    {
        var context = new SalesContext(FileOptions<SalesContext>());
        context.Database.EnsureCreated();
        Seed(context);
        return context;
    }

    private static void Seed(SalesContext context)
    {
        context.Products.AddRange(
            new ProductRow { Id = 1, Category = "Hardware" },
            new ProductRow { Id = 2, Category = "Software" });
        context.Sales.AddRange(
            new SaleRow { Id = 1, Region = "North", ProductId = 1, Year = 2025, Amount = 10m, Channel = "Online" },
            new SaleRow { Id = 2, Region = "North", ProductId = 2, Year = 2025, Amount = 20m, Channel = "Online" },
            new SaleRow { Id = 3, Region = "South", ProductId = 1, Year = 2025, Amount = 30m, Channel = null },
            new SaleRow { Id = 4, Region = "South", ProductId = 1, Year = 2026, Amount = 40m, Channel = "Online" });
        context.SaveChanges();
    }

    public sealed class Key
    {
        public string? Region { get; set; }
        public string? Channel { get; set; }
    }

    private sealed class SalesContext(DbContextOptions<SalesContext> options) : DbContext(options)
    {
        public DbSet<SaleRow> Sales => Set<SaleRow>();
        public DbSet<ProductRow> Products => Set<ProductRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProductRow>().Property(product => product.Id).ValueGeneratedNever();
            modelBuilder.Entity<SaleRow>().Property(sale => sale.Id).ValueGeneratedNever();
            modelBuilder.Entity<SaleRow>().Property(sale => sale.Amount).HasPrecision(18, 2);
        }
    }

    private sealed class ProductRow
    {
        public int Id { get; set; }
        public required string Category { get; set; }
    }

    private sealed class SaleRow
    {
        public int Id { get; set; }
        public required string Region { get; set; }
        public int ProductId { get; set; }
        public ProductRow? Product { get; set; }
        public int Year { get; set; }
        public decimal Amount { get; set; }
        public string? Channel { get; set; }
    }
}
