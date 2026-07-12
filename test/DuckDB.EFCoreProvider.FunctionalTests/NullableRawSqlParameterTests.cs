using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class NullableRawSqlParameterTests : DuckDBTestBase
{
    [ConditionalFact]
    public void ExecuteSqlInterpolated_binds_null_values_synchronously()
    {
        using var context = new ParameterContext(FileOptions<ParameterContext>());
        context.Database.EnsureCreated();

        context.Database.ExecuteSqlInterpolated(
            $"INSERT INTO \"Rows\" (\"Id\", \"Text\", \"Number\", \"OccurredAt\", \"ExternalId\") VALUES ({1}, {(string?)null}, {(int?)null}, {(DateTime?)null}, {(Guid?)null})");

        var row = context.Rows.Single();
        Assert.Null(row.Text);
        Assert.Null(row.Number);
        Assert.Null(row.OccurredAt);
        Assert.Null(row.ExternalId);
    }

    [ConditionalFact]
    public async Task ExecuteSqlInterpolated_binds_null_values_in_every_position()
    {
        await using var context = new ParameterContext(FileOptions<ParameterContext>());
        await context.Database.EnsureCreatedAsync();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"Rows\" (\"Id\", \"Text\", \"Number\", \"OccurredAt\", \"ExternalId\") VALUES ({1}, {(string?)null}, {42}, {(DateTime?)null}, {(Guid?)null})");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"Rows\" (\"Id\", \"Text\", \"Number\", \"OccurredAt\", \"ExternalId\") VALUES ({2}, {"middle"}, {(int?)null}, {DateTime.UtcNow}, {Guid.NewGuid()})");
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO \"Rows\" (\"Id\", \"Text\", \"Number\", \"OccurredAt\", \"ExternalId\") VALUES ({3}, {"final"}, {7}, {DateTime.UtcNow}, {(Guid?)null})");

        var rows = await context.Rows.OrderBy(row => row.Id).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Null(rows[0].Text);
        Assert.Null(rows[0].OccurredAt);
        Assert.Null(rows[0].ExternalId);
        Assert.Null(rows[1].Number);
        Assert.Null(rows[2].ExternalId);
    }

    [ConditionalFact]
    public void ExecuteSqlRaw_normalizes_explicit_DuckDBParameter_names()
    {
        using var context = new ParameterContext(FileOptions<ParameterContext>());
        context.Database.EnsureCreated();
        var parameter = new DuckDBParameter("$text", DBNull.Value);

        context.Database.ExecuteSqlRaw(
            "INSERT INTO \"Rows\" (\"Id\", \"Text\") VALUES (1, {0})",
            parameter);

        Assert.Null(context.Rows.Single().Text);
    }

    private sealed class ParameterContext(DbContextOptions<ParameterContext> options) : DbContext(options)
    {
        public DbSet<ParameterRow> Rows => Set<ParameterRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ParameterRow>().Property(row => row.Id).ValueGeneratedNever();
    }

    private sealed class ParameterRow
    {
        public int Id { get; set; }
        public string? Text { get; set; }
        public int? Number { get; set; }
        public DateTime? OccurredAt { get; set; }
        public Guid? ExternalId { get; set; }
    }
}