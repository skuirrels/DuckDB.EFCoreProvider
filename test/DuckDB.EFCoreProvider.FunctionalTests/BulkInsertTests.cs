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

    [ConditionalFact]
    public void BulkInsert_preserves_nullable_values_and_value_converters()
    {
        using var context = new ConvertedBulkContext(FileOptions<ConvertedBulkContext>());
        context.Database.EnsureCreated();

        var firstPayload = new byte[] { 1, 2, 3 };
        var inserted = context.BulkInsert(
        [
            new ConvertedBulkRow
            {
                Id = 1,
                OptionalQuantity = 42,
                OptionalActive = true,
                State = BulkState.Ready,
                Description = "converted",
                Payload = firstPayload
            },
            new ConvertedBulkRow
            {
                Id = 2,
                OptionalQuantity = null,
                OptionalActive = null,
                State = BulkState.Pending,
                Description = null,
                Payload = null
            }
        ]);

        Assert.Equal(2, inserted);

        var rows = context.Rows.AsNoTracking().OrderBy(row => row.Id).ToArray();
        Assert.Equal(42, rows[0].OptionalQuantity);
        Assert.True(rows[0].OptionalActive);
        Assert.Equal(BulkState.Ready, rows[0].State);
        Assert.Equal("converted", rows[0].Description);
        Assert.Equal(firstPayload, rows[0].Payload);

        Assert.Null(rows[1].OptionalQuantity);
        Assert.Null(rows[1].OptionalActive);
        Assert.Equal(BulkState.Pending, rows[1].State);
        Assert.Null(rows[1].Description);
        Assert.Null(rows[1].Payload);
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

    private sealed class ConvertedBulkContext(DbContextOptions<ConvertedBulkContext> options) : DbContext(options)
    {
        public DbSet<ConvertedBulkRow> Rows => Set<ConvertedBulkRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ConvertedBulkRow>().Property(e => e.Id).ValueGeneratedNever();
            modelBuilder.Entity<ConvertedBulkRow>().Property(e => e.State).HasConversion<string>();
        }
    }

    private sealed class ConvertedBulkRow
    {
        public int Id { get; set; }
        public int? OptionalQuantity { get; set; }
        public bool? OptionalActive { get; set; }
        public BulkState State { get; set; }
        public string? Description { get; set; }
        public byte[]? Payload { get; set; }
    }

    private enum BulkState
    {
        Pending,
        Ready
    }
}