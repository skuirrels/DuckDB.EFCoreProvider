using DuckDB.EFCoreProvider.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Coverage for decimal mapping: confirms precision and scale are honored, including high-precision
///     (HUGEINT-backed) values, scale rounding, and negatives.
/// </summary>
public class DecimalPrecisionTests : DuckDBTestBase
{
    private DecimalContext CreateContext()
        => new(FileOptions<DecimalContext>());

    [ConditionalFact]
    public void Default_decimal_round_trips()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Money { Id = 1, Default = 1234.567m });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(1234.567m, context.Set<Money>().Single(x => x.Id == 1).Default);
        }
    }

    [ConditionalFact]
    public void Custom_scale_round_trips_at_configured_scale()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            // Scale 2: a value with more fractional digits is rounded to the column's scale.
            context.Add(new Money { Id = 1, Scale2 = 9.129m });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(9.13m, context.Set<Money>().Single(x => x.Id == 1).Scale2);
        }
    }

    [ConditionalFact]
    public void High_precision_decimal_round_trips()
    {
        // DECIMAL(38, 10) is backed by HUGEINT and holds values far beyond a 64-bit integer.
        var big = 12345678901234567.0123456789m;

        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Money { Id = 1, HighPrecision = big });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(big, context.Set<Money>().Single(x => x.Id == 1).HighPrecision);
        }
    }

    [ConditionalFact]
    public void Negative_and_zero_decimals_round_trip()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new Money { Id = 1, Default = -42.500m, HighPrecision = -1m },
                new Money { Id = 2, Default = 0m, HighPrecision = 0m });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var rows = context.Set<Money>().OrderBy(x => x.Id).ToList();
            Assert.Equal(-42.500m, rows[0].Default);
            Assert.Equal(-1m, rows[0].HighPrecision);
            Assert.Equal(0m, rows[1].Default);
        }
    }

    [ConditionalFact]
    public void Configured_precision_and_scale_are_reflected_in_the_store_type()
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(Money))!.FindProperty(nameof(Money.HighPrecision))!;

        Assert.Equal(38, property.GetPrecision());
        Assert.Equal(10, property.GetScale());
        Assert.Contains("38", property.GetColumnType()!);
        Assert.Contains("10", property.GetColumnType()!);
    }

    private sealed class DecimalContext(DbContextOptions<DecimalContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Money>(entity =>
            {
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Scale2).HasPrecision(18, 2);
                entity.Property(e => e.HighPrecision).HasPrecision(38, 10);
            });
        }
    }

    private sealed class Money
    {
        public int Id { get; set; }
        public decimal Default { get; set; }
        public decimal Scale2 { get; set; }
        public decimal HighPrecision { get; set; }
    }
}
