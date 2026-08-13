using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Extensions.Internal;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class UpsertTests : DuckDBTestBase
{
    private UpsertContext CreateContext()
        => new(FileOptions<UpsertContext>());

    [Theory]
    [InlineData(3, 500, 500)]
    [InlineData(3, 100_000, 33_333)]
    [InlineData(50, 100_000, 2_000)]
    [InlineData(100_001, 100_000, 1)]
    public void Upsert_batching_honors_explicit_limits_and_the_staged_cell_budget(
        int columnCount,
        int requestedBatchSize,
        int expectedBatchSize)
        => Assert.Equal(
            expectedBatchSize,
            DuckDBUpsertBatching.EffectiveBatchSize(columnCount, requestedBatchSize));

    [Fact]
    public void Upsert_default_batch_request_uses_the_width_aware_cell_budget()
        => Assert.Equal(
            DuckDBUpsertBatching.MaxStagedCellCount,
            DuckDBUpsertBatching.DefaultRequestedBatchSize);

    [Fact]
    public void Upsert_public_overloads_expose_the_width_aware_default()
    {
        var batchParameters = typeof(DuckDBUpsertExtensions)
            .GetMethods()
            .Where(method => method.Name is nameof(DuckDBUpsertExtensions.Upsert) or nameof(DuckDBUpsertExtensions.UpsertAsync))
            .SelectMany(method => method.GetParameters())
            .Where(parameter => parameter.Name == "batchSize")
            .ToArray();

        Assert.Equal(3, batchParameters.Length);
        Assert.All(
            batchParameters,
            parameter => Assert.Equal(DuckDBUpsertBatching.DefaultRequestedBatchSize, parameter.DefaultValue));
    }

    [Theory]
    [InlineData(33_333, 10_000, 10_000)]
    [InlineData(33_333, 100_000, 33_333)]
    [InlineData(33_333, 0, 0)]
    [InlineData(33_333, null, DuckDBUpsertBatching.StreamingInitialBatchCapacity)]
    [InlineData(100, null, 100)]
    public void Upsert_batch_capacity_uses_known_input_size_without_overallocating(
        int effectiveBatchSize,
        int? sourceCount,
        int expectedCapacity)
        => Assert.Equal(
            expectedCapacity,
            DuckDBUpsertBatching.InitialBatchCapacity(effectiveBatchSize, sourceCount));

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
            // 250 updates + 250 inserts, spanning five batches through one reused staging table.
            context.Database.OpenConnection();
            context.Upsert(
                Enumerable.Range(1, 500).Select(i => new Item { Id = i, Name = $"n{i}", Quantity = i }),
                batchSize: 100);

            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                "SELECT count(*) FROM duckdb_tables() WHERE starts_with(table_name, '__duckdb_upsert_')";
            Assert.Equal(0L, command.ExecuteScalar());
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

    [ConditionalFact]
    public async Task UpsertAsync_can_conflict_on_alternate_key_and_use_store_generated_values()
    {
        var externalId = Guid.NewGuid();
        var inserted = new GeneratedItem
        {
            Id = 999,
            ExternalId = externalId,
            Name = "inserted",
            GeneratedValue = -1,
            GeneratedSqlValue = -1,
        };

        await using (var context = CreateContext())
        {
            await context.Database.EnsureCreatedAsync();
            Assert.Equal(1, await context.UpsertAsync([inserted], item => item.ExternalId));
        }

        Assert.Equal(999, inserted.Id);
        Assert.Equal(-1, inserted.GeneratedValue);
        Assert.Equal(-1, inserted.GeneratedSqlValue);

        long generatedId;
        await using (var context = CreateContext())
        {
            var stored = await context.GeneratedItems.AsNoTracking().SingleAsync();
            generatedId = stored.Id;
            Assert.NotEqual(inserted.Id, stored.Id);
            Assert.Equal(42, stored.GeneratedValue);
            Assert.Equal(43, stored.GeneratedSqlValue);
        }

        await using (var context = CreateContext())
        {
            Assert.Equal(
                2,
                await context.UpsertAsync(
                [
                    new GeneratedItem
                    {
                        Id = 1_000,
                        ExternalId = externalId,
                        Name = "updated",
                        GeneratedValue = -2,
                        GeneratedSqlValue = -2,
                    },
                    new GeneratedItem
                    {
                        Id = 1_001,
                        ExternalId = Guid.NewGuid(),
                        Name = "second",
                        GeneratedValue = -3,
                        GeneratedSqlValue = -3,
                    },
                ],
                item => item.ExternalId));
        }

        await using (var context = CreateContext())
        {
            var stored = await context.GeneratedItems.AsNoTracking().OrderBy(item => item.Id).ToListAsync();
            Assert.Equal(2, stored.Count);
            Assert.Equal(generatedId, stored.Single(item => item.ExternalId == externalId).Id);
            Assert.Equal("updated", stored.Single(item => item.ExternalId == externalId).Name);
            Assert.All(stored, item => Assert.Equal(42, item.GeneratedValue));
            Assert.All(stored, item => Assert.Equal(43, item.GeneratedSqlValue));
        }
    }

    [ConditionalFact]
    public async Task UpsertAsync_accepts_composite_unique_index_as_conflict_target()
    {
        await using (var context = CreateContext())
        {
            await context.Database.EnsureCreatedAsync();
            await context.UpsertAsync(
            [
                new CompositeConflictItem { Id = 1, TenantId = 7, ExternalId = "A", Name = "first" }
            ],
                item => new { item.TenantId, item.ExternalId });
        }

        await using (var context = CreateContext())
        {
            await context.UpsertAsync(
            [
                new CompositeConflictItem { Id = 2, TenantId = 7, ExternalId = "A", Name = "updated" }
            ],
                item => new { item.TenantId, item.ExternalId });

            var stored = await context.CompositeConflictItems.AsNoTracking().SingleAsync();
            Assert.Equal(1, stored.Id);
            Assert.Equal("updated", stored.Name);
        }
    }

    [ConditionalFact]
    public async Task UpsertAsync_keeps_client_generated_OnAdd_primary_keys_in_the_insert()
    {
        var originalId = Guid.NewGuid();
        var externalId = new EventKey(Guid.NewGuid());

        await using (var context = CreateContext())
        {
            await context.Database.EnsureCreatedAsync();
            Assert.Equal(
                1,
                await context.UpsertAsync(
                [
                    new ClientGeneratedItem
                    {
                        Id = originalId,
                        ExternalId = externalId,
                        Name = "inserted",
                    }
                ],
                    item => item.ExternalId));
        }

        await using (var context = CreateContext())
        {
            Assert.Equal(
                1,
                await context.UpsertAsync(
                [
                    new ClientGeneratedItem
                    {
                        Id = Guid.NewGuid(),
                        ExternalId = externalId,
                        Name = "updated",
                    }
                ],
                    item => item.ExternalId));

            var stored = await context.ClientGeneratedItems.AsNoTracking().SingleAsync();
            Assert.Equal(originalId, stored.Id);
            Assert.Equal(externalId, stored.ExternalId);
            Assert.Equal("updated", stored.Name);
        }
    }

    [ConditionalFact]
    public async Task UpsertAsync_rejects_non_unique_conflict_target()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.UpsertAsync(
        [
            new GeneratedItem { ExternalId = Guid.NewGuid(), Name = "not-unique" }
        ],
            item => item.Name));

        Assert.Contains("primary key, alternate key, or unique index", exception.Message);
    }

    private sealed class UpsertContext(DbContextOptions<UpsertContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
        public DbSet<CompositeItem> CompositeItems => Set<CompositeItem>();
        public DbSet<KeyOnly> KeyOnlies => Set<KeyOnly>();
        public DbSet<GeneratedItem> GeneratedItems => Set<GeneratedItem>();
        public DbSet<CompositeConflictItem> CompositeConflictItems => Set<CompositeConflictItem>();
        public DbSet<ClientGeneratedItem> ClientGeneratedItems => Set<ClientGeneratedItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>().Property(e => e.Id).ValueGeneratedNever();
            modelBuilder.Entity<CompositeItem>().HasKey(e => new { e.KeyA, e.KeyB });
            modelBuilder.Entity<KeyOnly>().Property(e => e.Id).ValueGeneratedNever();
            modelBuilder.Entity<GeneratedItem>(entity =>
            {
                entity.Property(item => item.Id).UseAutoIncrement();
                entity.HasAlternateKey(item => item.ExternalId);
                entity.Property(item => item.ExternalId).HasColumnName("external_event_id");
                entity.Property(item => item.GeneratedValue).HasDefaultValue(42).ValueGeneratedOnAdd();
                entity.Property(item => item.GeneratedSqlValue).HasDefaultValueSql("43").ValueGeneratedOnAdd();
            });
            modelBuilder.Entity<CompositeConflictItem>(entity =>
            {
                entity.Property(item => item.Id).ValueGeneratedNever();
                entity.HasIndex(item => new { item.TenantId, item.ExternalId }).IsUnique();
            });
            modelBuilder.Entity<ClientGeneratedItem>(entity =>
            {
                entity.Property(item => item.Id).ValueGeneratedOnAdd();
                entity.Property(item => item.ExternalId)
                    .HasConversion(value => value.Value, value => new EventKey(value))
                    .HasColumnName("external_event_id");
                entity.HasAlternateKey(item => item.ExternalId);
            });
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

    private sealed class GeneratedItem
    {
        public long Id { get; set; }
        public Guid ExternalId { get; set; }
        public string Name { get; set; } = "";
        public int GeneratedValue { get; set; }
        public int GeneratedSqlValue { get; set; }
    }

    private sealed class CompositeConflictItem
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public string ExternalId { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private sealed class ClientGeneratedItem
    {
        public Guid Id { get; set; }
        public EventKey ExternalId { get; set; }
        public string Name { get; set; } = "";
    }

    private readonly record struct EventKey(Guid Value);
}