using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Update.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data.Common;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class BulkDeleteBatchingTests : DuckDBTestBase
{
    private DeleteContext CreateContext()
        => new(FileOptions<DeleteContext>(duckdb => duckdb
            .EnableBulkInsertBatching()
            .EnableBulkDeleteBatching()));

    private DeleteContext CreateContext(DbCommandInterceptor interceptor)
        => new(new DbContextOptionsBuilder<DeleteContext>(
                FileOptions<DeleteContext>(duckdb => duckdb
                    .EnableBulkInsertBatching()
                    .EnableBulkDeleteBatching()))
            .AddInterceptors(interceptor)
            .Options);

    [ConditionalFact]
    public void SaveChanges_with_delete_batching_removes_only_targeted_rows()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(Enumerable.Range(1, 200).Select(i => new Item { Id = i, Name = $"n{i}" }));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            context.Items.RemoveRange(context.Items.Where(x => x.Id % 2 == 0));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(100, context.Items.Count());
            Assert.True(context.Items.All(x => x.Id % 2 == 1));
        }
    }

    [ConditionalFact]
    public void SaveChanges_with_delete_batching_handles_composite_keys()
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
            context.CompositeItems.RemoveRange(context.CompositeItems.Where(x => x.KeyA == 3));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(20, context.CompositeItems.Count());
            Assert.True(context.CompositeItems.All(x => x.KeyA != 3));
        }
    }

    [ConditionalFact]
    public void SaveChanges_with_delete_batching_handles_orphan_cleanup_and_child_replacement()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            var parent = new Parent
            {
                Id = 1,
                Name = "p",
                Children = Enumerable.Range(1, 50).Select(i => new Child { Id = i, Label = $"old-{i}" }).ToList()
            };
            context.Add(parent);
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            // Replace the entire child collection: old children become orphans (deleted), new ones inserted.
            var parent = context.Parents.Include(p => p.Children).Single();
            parent.Children.Clear();
            for (var i = 100; i < 130; i++)
            {
                parent.Children.Add(new Child { Id = i, Label = $"new-{i}" });
            }

            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var children = context.Children.OrderBy(c => c.Id).ToList();
            Assert.Equal(30, children.Count);
            Assert.True(children.All(c => c.Id >= 100 && c.Label.StartsWith("new-")));
        }
    }

    [ConditionalFact]
    public void SaveChanges_with_delete_batching_handles_mixed_insert_update_delete()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(Enumerable.Range(1, 10).Select(i => new Item { Id = i, Name = $"n{i}" }));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            context.Items.RemoveRange(context.Items.Where(x => x.Id <= 3));           // delete 1..3
            foreach (var item in context.Items.Where(x => x.Id > 3 && x.Id <= 6))      // update 4..6
            {
                item.Name = "upd";
            }

            context.AddRange(Enumerable.Range(11, 3).Select(i => new Item { Id = i, Name = "ins" })); // insert 11..13
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(10, context.Items.Count());
            Assert.False(context.Items.Any(x => x.Id <= 3));
            Assert.Equal("upd", context.Items.Single(x => x.Id == 4).Name);
            Assert.Equal("ins", context.Items.Single(x => x.Id == 11).Name);
        }
    }

    [ConditionalFact]
    public async Task SaveChangesAsync_with_delete_batching_removes_rows()
    {
        await using (var context = CreateContext())
        {
            await context.Database.EnsureCreatedAsync();
            context.AddRange(Enumerable.Range(1, 100).Select(i => new Item { Id = i, Name = "n" }));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            context.Items.RemoveRange(context.Items);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            Assert.Equal(0, await context.Items.CountAsync());
        }
    }

    [ConditionalFact]
    public void SaveChanges_with_delete_batching_preserves_planned_sql_shapes()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new Item { Id = 1, Name = "one" },
                new Item { Id = 2, Name = "two" },
                new CompositeItem { KeyA = 1, KeyB = 10, Name = "first" },
                new CompositeItem { KeyA = 2, KeyB = 20, Name = "second" });
            context.SaveChanges();
        }

        var interceptor = new CommandCaptureInterceptor();
        using (var context = CreateContext(interceptor))
        {
            context.Items.RemoveRange(context.Items);
            context.CompositeItems.RemoveRange(context.CompositeItems);
            context.SaveChanges();
        }

        var sql = string.Join(
            Environment.NewLine,
            interceptor.CommandTexts.Where(commandText => commandText.Contains("DELETE ", StringComparison.Ordinal)));
        Assert.Equal(2, sql.Split("DELETE FROM ", StringSplitOptions.None).Length - 1);
        Assert.Contains("DELETE FROM \"Items\" WHERE \"Id\" IN (", sql);
        Assert.Contains("DELETE FROM \"CompositeItems\" USING (VALUES ", sql);
        Assert.Contains("AS v(\"KeyA\", \"KeyB\")", sql);
        Assert.Contains(
            "WHERE \"CompositeItems\".\"KeyA\" = v.\"KeyA\" AND \"CompositeItems\".\"KeyB\" = v.\"KeyB\"",
            sql);
    }

    [ConditionalFact]
    public void Bulk_delete_planner_rejects_an_empty_command_run()
    {
        Assert.False(DuckDBBulkDeletePlanner.TryCreate([], out var plan));
        Assert.Null(plan);
    }

    [ConditionalFact]
    public void SaveChanges_with_delete_batching_preserves_concurrency_checks()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new ConcurrencyItem { Id = 1, Name = "original", Version = 1 });
            context.SaveChanges();
        }

        var interceptor = new CommandCaptureInterceptor();
        using var staleContext = CreateContext(interceptor);
        var staleItem = staleContext.ConcurrencyItems.Single(item => item.Id == 1);

        using (var currentContext = CreateContext())
        {
            var currentItem = currentContext.ConcurrencyItems.Single(item => item.Id == 1);
            currentItem.Name = "changed";
            currentItem.Version = 2;
            currentContext.SaveChanges();
        }

        staleContext.Remove(staleItem);

        Assert.Throws<DbUpdateConcurrencyException>(() => staleContext.SaveChanges());

        var deleteSql = Assert.Single(interceptor.CommandTexts.Where(
            commandText => commandText.Contains("DELETE FROM \"ConcurrencyItems\"", StringComparison.Ordinal)));
        Assert.Contains("\"Id\"", deleteSql);
        Assert.Contains("\"Version\"", deleteSql);
        Assert.DoesNotContain(" IN (", deleteSql);
        Assert.DoesNotContain(" USING (VALUES ", deleteSql);

        using var verification = CreateContext();
        Assert.Equal(2, verification.ConcurrencyItems.Single(item => item.Id == 1).Version);
    }

    [ConditionalFact]
    public void EnableBulkDeleteBatching_sets_the_option_and_is_off_by_default()
    {
        using (var enabled = CreateContext())
        {
            Assert.True(enabled.GetService<IDbContextOptions>()
                .FindExtension<DuckDBOptionsExtension>()!.BulkDeleteBatching);
        }

        using var disabled = new DeleteContext(FileOptions<DeleteContext>());

        Assert.False(disabled.GetService<IDbContextOptions>()
            .FindExtension<DuckDBOptionsExtension>()!.BulkDeleteBatching);
    }

    private sealed class DeleteContext(DbContextOptions<DeleteContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
        public DbSet<CompositeItem> CompositeItems => Set<CompositeItem>();
        public DbSet<Parent> Parents => Set<Parent>();
        public DbSet<Child> Children => Set<Child>();
        public DbSet<ConcurrencyItem> ConcurrencyItems => Set<ConcurrencyItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>().Property(e => e.Id).ValueGeneratedNever();
            modelBuilder.Entity<CompositeItem>().HasKey(e => new { e.KeyA, e.KeyB });
            modelBuilder.Entity<Parent>().Property(e => e.Id).ValueGeneratedNever();
            modelBuilder.Entity<ConcurrencyItem>(entity =>
            {
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Version).IsConcurrencyToken();
            });
            modelBuilder.Entity<Child>(child =>
            {
                child.Property(e => e.Id).ValueGeneratedNever();
                // Required relationship so clearing the collection cascade-deletes the orphaned children,
                // mirroring real orphan cleanup / child-collection replacement.
                child.HasOne<Parent>()
                    .WithMany(p => p.Children)
                    .HasForeignKey(e => e.ParentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }

    private sealed class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class CompositeItem
    {
        public int KeyA { get; set; }
        public int KeyB { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class Parent
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public List<Child> Children { get; set; } = [];
    }

    private sealed class Child
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Label { get; set; } = "";
    }

    private sealed class ConcurrencyItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Version { get; set; }
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> CommandTexts { get; } = [];

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            CommandTexts.Add(command.CommandText);
            return result;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            CommandTexts.Add(command.CommandText);
            return result;
        }
    }
}