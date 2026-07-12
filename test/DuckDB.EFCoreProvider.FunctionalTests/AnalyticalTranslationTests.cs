using DuckDB.EFCoreProvider.Extensions.DbFunctionsExtensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class AnalyticalTranslationTests : DuckDBTestBase
{
    [ConditionalFact]
    public void SplitPart_translates_and_uses_one_based_indexing()
    {
        using var context = CreateSeededContext();

        var part = context.Rows
            .Where(row => row.Id == 1)
            .Select(row => EF.Functions.SplitPart(row.Label, "|", 2))
            .Single();

        Assert.Equal("middle", part);
    }

    [ConditionalFact]
    public void StandardDeviationSample_translates_as_an_aggregate()
    {
        using var context = CreateSeededContext();

        var deviation = context.Rows
            .GroupBy(_ => 1)
            .Select(group => EF.Functions.StandardDeviationSample(group.Select(row => row.Value)))
            .Single();

        Assert.Equal(10d, deviation);
    }

    [ConditionalFact]
    public void ArgMax_and_ArgMin_translate_tuple_value_and_order_inputs()
    {
        using var context = CreateSeededContext();

        var result = context.Rows
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Maximum = EF.Functions.ArgMax(group.Select(row => ValueTuple.Create(row.Label, row.Value))),
                Minimum = EF.Functions.ArgMin(group.Select(row => ValueTuple.Create(row.Label, row.Value)))
            })
            .Single();

        Assert.Equal("right", result.Maximum);
        Assert.Equal("left|middle|right", result.Minimum);
    }

    [ConditionalFact]
    public void ArgMax_preserves_source_predicate()
    {
        using var context = CreateSeededContext();

        var maximum = context.Rows
            .GroupBy(_ => 1)
            .Select(group => EF.Functions.ArgMax(
                group.Where(row => row.Value < 30).Select(row => ValueTuple.Create(row.Label, row.Value))))
            .Single();

        Assert.Equal("centre", maximum);
    }

    private AnalyticsContext CreateSeededContext()
    {
        var context = new AnalyticsContext(FileOptions<AnalyticsContext>());
        context.Database.EnsureCreated();
        context.Rows.AddRange(
            new AnalyticsRow { Id = 1, Label = "left|middle|right", Value = 10 },
            new AnalyticsRow { Id = 2, Label = "centre", Value = 20 },
            new AnalyticsRow { Id = 3, Label = "right", Value = 30 });
        context.SaveChanges();
        return context;
    }

    private sealed class AnalyticsContext(DbContextOptions<AnalyticsContext> options) : DbContext(options)
    {
        public DbSet<AnalyticsRow> Rows => Set<AnalyticsRow>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<AnalyticsRow>().Property(row => row.Id).ValueGeneratedNever();
    }

    private sealed class AnalyticsRow
    {
        public int Id { get; set; }
        public required string Label { get; set; }
        public int Value { get; set; }
    }
}