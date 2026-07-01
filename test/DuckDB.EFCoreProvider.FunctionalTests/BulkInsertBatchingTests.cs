using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class BulkInsertBatchingTests : DuckDBTestBase
{
    private BatchingContext CreateContext(bool enableBatching)
        => new(FileOptions<BatchingContext>(duckdb =>
        {
            if (enableBatching)
            {
                duckdb.EnableBulkInsertBatching();
            }
        }));

    [ConditionalFact]
    public void SaveChanges_with_batching_persists_all_rows_with_correct_values()
    {
        using (var context = CreateContext(enableBatching: true))
        {
            context.Database.EnsureCreated();

            context.AddRange(
                Enumerable.Range(1, 500)
                    .Select(i => new ExplicitKeyRow { Id = i, Name = $"row-{i}", Value = i * 1.5, Active = i % 2 == 0 }));

            context.SaveChanges();
        }

        using (var context = CreateContext(enableBatching: true))
        {
            Assert.Equal(500, context.ExplicitKeyRows.Count());

            var first = context.ExplicitKeyRows.Single(x => x.Id == 1);
            Assert.Equal("row-1", first.Name);
            Assert.Equal(1.5, first.Value);
            Assert.False(first.Active);

            var last = context.ExplicitKeyRows.Single(x => x.Id == 500);
            Assert.Equal("row-500", last.Name);
            Assert.True(last.Active);
        }
    }

    [ConditionalFact]
    public void SaveChanges_with_batching_correlates_store_generated_keys()
    {
        var rows = Enumerable.Range(0, 50)
            .Select(i => new GeneratedKeyRow { Name = $"g{i}" })
            .ToList();

        using var context = CreateContext(enableBatching: true);
        context.Database.EnsureCreated();

        context.AddRange(rows);
        context.SaveChanges();

        // Every row received a distinct, populated, store-generated key, correlated back to the right entity.
        Assert.All(rows, r => Assert.True(r.Id > 0));
        Assert.Equal(rows.Count, rows.Select(r => r.Id).Distinct().Count());

        foreach (var row in rows)
        {
            Assert.Equal(row.Name, context.GeneratedKeyRows.Single(x => x.Id == row.Id).Name);
        }
    }

    [ConditionalFact]
    public async Task SaveChangesAsync_with_batching_persists_rows()
    {
        await using var context = CreateContext(enableBatching: true);
        await context.Database.EnsureCreatedAsync();

        context.AddRange(
            Enumerable.Range(1, 250).Select(i => new ExplicitKeyRow { Id = i, Name = $"r{i}", Value = i }));

        await context.SaveChangesAsync();

        Assert.Equal(250, await context.ExplicitKeyRows.CountAsync());
    }

    [ConditionalFact]
    public void Batching_is_disabled_by_default()
    {
        using var context = CreateContext(enableBatching: false);
        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<DuckDBOptionsExtension>();

        Assert.NotNull(extension);
        Assert.False(extension!.BulkInsertBatching);
    }

    [ConditionalFact]
    public void EnableBulkInsertBatching_sets_the_option()
    {
        using var context = CreateContext(enableBatching: true);
        var extension = context.GetService<IDbContextOptions>()
            .FindExtension<DuckDBOptionsExtension>();

        Assert.NotNull(extension);
        Assert.True(extension!.BulkInsertBatching);
    }

    private sealed class BatchingContext(DbContextOptions<BatchingContext> options) : DbContext(options)
    {
        public DbSet<ExplicitKeyRow> ExplicitKeyRows => Set<ExplicitKeyRow>();
        public DbSet<GeneratedKeyRow> GeneratedKeyRows => Set<GeneratedKeyRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ExplicitKeyRow>().Property(e => e.Id).ValueGeneratedNever();
    }

    private sealed class ExplicitKeyRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public double Value { get; set; }
        public bool Active { get; set; }
    }

    private sealed class GeneratedKeyRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
