using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class CharacterConversionCompatibilityTests
{
    [Theory]
    [InlineData('A')]
    [InlineData('é')]
    [InlineData('7')]
    public async Task Numeric_casts_respect_text_and_numeric_character_mappings(char value)
    {
        using var connection = new DuckDBConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new CharacterContext(
            new DbContextOptionsBuilder<CharacterContext>().UseDuckDB(connection)
                .EnableServiceProviderCaching(false)
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(
            new Row { Id = 1, Text = value, Numeric = value, NullableNumeric = value },
            new Row { Id = 2, Text = value, Numeric = value });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var numericQuery = context.Set<Row>().OrderBy(row => row.Id).Select(row => new
        {
            Int = (int)row.Numeric,
            Long = (long)row.Numeric,
            UInt = (uint)row.Numeric,
            ULong = (ulong)row.Numeric,
            UShort = (ushort)row.Numeric,
            Float = (float)row.Numeric,
            Double = (double)row.Numeric,
            Decimal = (decimal)row.Numeric,
            Nullable = (int?)row.NullableNumeric
        });
        Assert.DoesNotContain("unicode", numericQuery.ToQueryString());
        var rows = await numericQuery.ToListAsync();
        foreach (var row in rows)
        {
            Assert.Equal((int)value, row.Int);
            Assert.Equal((long)value, row.Long);
            Assert.Equal((uint)value, row.UInt);
            Assert.Equal((ulong)value, row.ULong);
            Assert.Equal((ushort)value, row.UShort);
            Assert.Equal((float)value, row.Float);
            Assert.Equal((double)value, row.Double);
            Assert.Equal((decimal)value, row.Decimal);
        }
        Assert.Equal((int)value, rows[0].Nullable);
        Assert.Null(rows[1].Nullable);

        var textQuery = context.Set<Row>().Where(row => row.Id == 1).Select(row => (int)row.Text);
        Assert.Contains("unicode", textQuery.ToQueryString());
        Assert.Equal((int)value, await textQuery.SingleAsync());
    }

    private sealed class CharacterContext(DbContextOptions<CharacterContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Row>().Property(row => row.Id).ValueGeneratedNever();
            modelBuilder.Entity<Row>().Property(row => row.Numeric).HasConversion<int>();
            modelBuilder.Entity<Row>().Property(row => row.NullableNumeric).HasConversion<int>();
        }
    }

    private sealed class Row
    {
        public int Id { get; set; }
        public char Text { get; set; }
        public char Numeric { get; set; }
        public char? NullableNumeric { get; set; }
    }
}