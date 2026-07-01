using DuckDB.EFCoreProvider.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class UpsertTests : DuckDBTestBase
{
    private UpsertContext CreateContext()
        => new(FileOptions<UpsertContext>());

    [ConditionalFact]
    public void Upsert_inserts_new_and_updates_existing()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new Item { Id = 1, Name = "orig-1", Quantity = 10 },
                new Item { Id = 2, Name = "orig-2", Quantity = 20 });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            // 1 exists (update), 2 exists (update), 3 is new (insert).
            var processed = context.Upsert(new[]
            {
                new Item { Id = 1, Name = "new-1", Quantity = 100 },
                new Item { Id = 2, Name = "new-2", Quantity = 200 },
                new Item { Id = 3, Name = "new-3", Quantity = 300 },
            });

            Assert.Equal(3, processed);
        }

        using (var context = CreateContext())
        {
            var items = context.Items.OrderBy(i => i.Id).ToList();
            Assert.Equal(3, items.Count);
            Assert.Equal(("new-1", 100), (items[0].Name, items[0].Quantity));
            Assert.Equal(("new-2", 200), (items[1].Name, items[1].Quantity));
            Assert.Equal(("new-3", 300), (items[2].Name, items[2].Quantity));
        }
    }

    [ConditionalFact]
    public void Upsert_applies_distinct_values_per_row_across_a_batch()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(Enumerable.Range(1, 250).Select(i => new Item { Id = i, Name = "old", Quantity = 0 }));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            // 250 updates + 250 inserts, spanning multiple batches (default batch size 100).
            context.Upsert(Enumerable.Range(1, 500).Select(i => new Item { Id = i, Name = $"n{i}", Quantity = i }));
        }

        using (var context = CreateContext())
        {
            Assert.Equal(500, context.Items.Count());
            Assert.All(context.Items.AsEnumerable(), x =>
            {
                Assert.Equal($"n{x.Id}", x.Name);
                Assert.Equal(x.Id, x.Quantity);
            });
        }
    }

    [ConditionalFact]
    public void Upsert_handles_composite_keys()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new CompositeItem { KeyA = 1, KeyB = 1, Name = "orig" });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            context.Upsert(new[]
            {
                new CompositeItem { KeyA = 1, KeyB = 1, Name = "updated" }, // existing
                new CompositeItem { KeyA = 1, KeyB = 2, Name = "inserted" }, // new
            });
        }

        using (var context = CreateContext())
        {
            Assert.Equal(2, context.CompositeItems.Count());
            Assert.Equal("updated", context.CompositeItems.Single(x => x.KeyA == 1 && x.KeyB == 1).Name);
            Assert.Equal("inserted", context.CompositeItems.Single(x => x.KeyA == 1 && x.KeyB == 2).Name);
        }
    }

    [ConditionalFact]
    public void Upsert_with_only_key_columns_does_nothing_on_conflict()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new KeyOnly { Id = 1 });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            // Id 1 conflicts (DO NOTHING), Id 2 inserts — must not throw.
            var processed = context.Upsert(new[] { new KeyOnly { Id = 1 }, new KeyOnly { Id = 2 } });
            Assert.Equal(2, processed);
        }

        using (var context = CreateContext())
        {
            Assert.Equal(2, context.KeyOnlies.Count());
        }
    }

    [ConditionalFact]
    public async Task UpsertAsync_inserts_and_updates()
    {
        await using (var context = CreateContext())
        {
            await context.Database.EnsureCreatedAsync();
            context.Add(new Item { Id = 1, Name = "orig", Quantity = 1 });
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            await context.UpsertAsync(new[]
            {
                new Item { Id = 1, Name = "upd", Quantity = 9 },
                new Item { Id = 2, Name = "ins", Quantity = 2 },
            });
        }

        await using (var context = CreateContext())
        {
            Assert.Equal(2, await context.Items.CountAsync());
            Assert.Equal("upd", context.Items.Single(x => x.Id == 1).Name);
            Assert.Equal("ins", context.Items.Single(x => x.Id == 2).Name);
        }
    }

    private sealed class UpsertContext(DbContextOptions<UpsertContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
        public DbSet<CompositeItem> CompositeItems => Set<CompositeItem>();
        public DbSet<KeyOnly> KeyOnlies => Set<KeyOnly>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>().Property(e => e.Id).ValueGeneratedNever();
            modelBuilder.Entity<CompositeItem>().HasKey(e => new { e.KeyA, e.KeyB });
            modelBuilder.Entity<KeyOnly>().Property(e => e.Id).ValueGeneratedNever();
        }
    }

    private sealed class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
    }

    private sealed class CompositeItem
    {
        public int KeyA { get; set; }
        public int KeyB { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class KeyOnly
    {
        public int Id { get; set; }
    }
}
