using DuckDB.EFCoreProvider.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class BulkInsertTests : DuckDBTestBase
{
    private BulkContext CreateContext()
        => new(FileOptions<BulkContext>());

    [ConditionalFact]
    public void BulkInsert_appends_all_rows_with_correct_values()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var rows = Enumerable.Range(1, 1000)
            .Select(i => new BulkRow { Id = i, Name = $"row-{i}", Value = i * 1.5, Active = i % 2 == 0 })
            .ToList();

        var inserted = context.BulkInsert(rows);

        Assert.Equal(1000, inserted);
        Assert.Equal(1000, context.Rows.Count());

        var first = context.Rows.Single(x => x.Id == 1);
        Assert.Equal("row-1", first.Name);
        Assert.Equal(1.5, first.Value);
        Assert.False(first.Active);

        var last = context.Rows.Single(x => x.Id == 1000);
        Assert.Equal("row-1000", last.Name);
        Assert.True(last.Active);
    }

    [ConditionalFact]
    public async Task BulkInsertAsync_appends_rows()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var inserted = await context.BulkInsertAsync(
            Enumerable.Range(1, 250).Select(i => new BulkRow { Id = i, Name = $"r{i}", Value = i }));

        Assert.Equal(250, inserted);
        Assert.Equal(250, await context.Rows.CountAsync());
    }

    private sealed class BulkContext(DbContextOptions<BulkContext> options) : DbContext(options)
    {
        public DbSet<BulkRow> Rows => Set<BulkRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<BulkRow>().Property(e => e.Id).ValueGeneratedNever();
    }

    private sealed class BulkRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public double Value { get; set; }
        public bool Active { get; set; }
    }
}
