using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class BulkUpdateBatchingTests : DuckDBTestBase
{
    private UpdateContext CreateContext()
        => new(FileOptions<UpdateContext>(duckdb => duckdb.EnableBulkUpdateBatching()));

    [ConditionalFact]
    public void SaveChanges_with_update_batching_applies_distinct_values_per_row()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                Enumerable.Range(1, 200).Select(i => new Item { Id = i, Name = $"orig-{i}", Value = i }));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            // Update every row to a distinct new value; the multi-row VALUES join must map each row correctly.
            foreach (var item in context.Items.OrderBy(x => x.Id))
            {
                item.Name = $"new-{item.Id}";
                item.Value = item.Id * 1000;
            }

            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(200, context.Items.Count());
            Assert.All(context.Items.AsEnumerable(), x =>
            {
                Assert.Equal($"new-{x.Id}", x.Name);
                Assert.Equal(x.Id * 1000, x.Value);
            });
        }
    }

    [ConditionalFact]
    public void SaveChanges_with_update_batching_updates_only_targeted_rows()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                Enumerable.Range(1, 10).Select(i => new Item { Id = i, Name = $"orig-{i}", Value = i }));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            foreach (var item in context.Items.Where(x => x.Id % 2 == 0))
            {
                item.Name = "even";
            }

            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.All(context.Items.AsEnumerable(), x =>
                Assert.Equal(x.Id % 2 == 0 ? "even" : $"orig-{x.Id}", x.Name));
        }
    }

    [ConditionalFact]
    public void SaveChanges_with_update_batching_handles_composite_keys()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            for (var a = 1; a <= 5; a++)
            {
                for (var b = 1; b <= 5; b++)
                {
                    context.Add(new CompositeItem { KeyA = a, KeyB = b, Name = $"{a}-{b}" });
                }
            }

            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            foreach (var item in context.CompositeItems)
            {
                item.Name = $"upd-{item.KeyA}-{item.KeyB}";
            }

            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(25, context.CompositeItems.Count());
            Assert.All(context.CompositeItems.AsEnumerable(), x =>
                Assert.Equal($"upd-{x.KeyA}-{x.KeyB}", x.Name));
        }
    }

    [ConditionalFact]
    public void SaveChanges_with_update_batching_handles_mixed_insert_and_update()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                Enumerable.Range(1, 5).Select(i => new Item { Id = i, Name = $"orig-{i}", Value = i }));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            // Update existing rows...
            foreach (var item in context.Items)
            {
                item.Name = $"upd-{item.Id}";
            }

            // ...and insert new ones in the same SaveChanges.
            context.AddRange(
                Enumerable.Range(6, 5).Select(i => new Item { Id = i, Name = $"ins-{i}", Value = i }));

            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(10, context.Items.Count());
            Assert.Equal("upd-1", context.Items.Single(x => x.Id == 1).Name);
            Assert.Equal("ins-6", context.Items.Single(x => x.Id == 6).Name);
        }
    }

    [ConditionalFact]
    public async Task SaveChangesAsync_with_update_batching_persists_updates()
    {
        await using (var context = CreateContext())
        {
            await context.Database.EnsureCreatedAsync();
            context.AddRange(Enumerable.Range(1, 100).Select(i => new Item { Id = i, Name = "o", Value = i }));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            foreach (var item in context.Items)
            {
                item.Value += 1;
            }

            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            Assert.All(context.Items.AsEnumerable(), x => Assert.Equal(x.Id + 1, x.Value));
        }
    }

    [ConditionalFact]
    public void EnableBulkUpdateBatching_sets_the_option_and_is_off_by_default()
    {
        using (var enabled = CreateContext())
        {
            Assert.True(enabled.GetService<IDbContextOptions>()
                .FindExtension<DuckDBOptionsExtension>()!.BulkUpdateBatching);
        }

        using var disabled = new UpdateContext(FileOptions<UpdateContext>());

        Assert.False(disabled.GetService<IDbContextOptions>()
            .FindExtension<DuckDBOptionsExtension>()!.BulkUpdateBatching);
    }

    private sealed class UpdateContext(DbContextOptions<UpdateContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
        public DbSet<CompositeItem> CompositeItems => Set<CompositeItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>().Property(e => e.Id).ValueGeneratedNever();
            modelBuilder.Entity<CompositeItem>().HasKey(e => new { e.KeyA, e.KeyB });
        }
    }

    private sealed class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public long Value { get; set; }
    }

    private sealed class CompositeItem
    {
        public int KeyA { get; set; }
        public int KeyB { get; set; }
        public string Name { get; set; } = "";
    }
}
