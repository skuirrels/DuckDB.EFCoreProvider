using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Update.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data.Common;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class BulkUpdateBatchingTests : DuckDBTestBase
{
    private UpdateContext CreateContext()
        => new(FileOptions<UpdateContext>(duckdb => duckdb.EnableBulkUpdateBatching()));

    private UpdateContext CreateContext(DbCommandInterceptor interceptor)
        => new(new DbContextOptionsBuilder<UpdateContext>(
                FileOptions<UpdateContext>(duckdb => duckdb.EnableBulkUpdateBatching()))
            .AddInterceptors(interceptor)
            .Options);

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
    public void SaveChanges_with_update_batching_preserves_planned_sql_shape()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new Item { Id = 1, Name = "one", Value = 10 },
                new Item { Id = 2, Name = "two", Value = 20 });
            context.SaveChanges();
        }

        var interceptor = new CommandCaptureInterceptor();
        using (var context = CreateContext(interceptor))
        {
            foreach (var item in context.Items.OrderBy(item => item.Id))
            {
                item.Name += "-updated";
                item.Value *= 10;
            }

            context.SaveChanges();
        }

        var sql = Assert.Single(interceptor.CommandTexts.Where(
            commandText => commandText.StartsWith("UPDATE ", StringComparison.Ordinal)));
        Assert.Contains("UPDATE \"Items\" SET \"Name\" = v.\"Name\", \"Value\" = v.\"Value\"", sql);
        Assert.Contains(
            "FROM (VALUES ($p0, $p1, $p2), ($p3, $p4, $p5)) AS v(\"Id\", \"Name\", \"Value\")",
            sql);
        Assert.Contains("WHERE \"Items\".\"Id\" = v.\"Id\";", sql);
    }

    [ConditionalFact]
    public void Bulk_update_planner_rejects_an_empty_command_run()
    {
        Assert.False(DuckDBBulkUpdatePlanner.TryCreate([], out var plan));
        Assert.Null(plan);
    }

    [ConditionalFact]
    public void SaveChanges_with_update_batching_separates_incompatible_column_shapes()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new Item { Id = 1, Name = "one", Value = 10 },
                new Item { Id = 2, Name = "two", Value = 20 });
            context.SaveChanges();
        }

        var interceptor = new CommandCaptureInterceptor();
        using (var context = CreateContext(interceptor))
        {
            var items = context.Items.OrderBy(item => item.Id).ToArray();
            items[0].Name = "one-updated";
            items[1].Value = 200;
            context.SaveChanges();
        }

        var sql = string.Join(
            Environment.NewLine,
            interceptor.CommandTexts.Where(commandText => commandText.Contains("UPDATE ", StringComparison.Ordinal)));
        Assert.Equal(2, sql.Split("UPDATE ", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, sql.Split("FROM (VALUES ", StringSplitOptions.None).Length - 1);

        using var verificationContext = CreateContext();
        Assert.Equal("one-updated", verificationContext.Items.Single(item => item.Id == 1).Name);
        Assert.Equal(200, verificationContext.Items.Single(item => item.Id == 2).Value);
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