using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class TieredStorageSharedBindingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "duckdb-tier-shared-" + Guid.NewGuid().ToString("N"));

    public TieredStorageSharedBindingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public void Shared_child_bindings_are_retained_independently_of_registration_order()
    {
        using var forward = CreateContext("forward.duckdb", "forward", reverseRegistration: false);
        using var reverse = CreateContext("reverse.duckdb", "reverse", reverseRegistration: true);

        var forwardBindings = forward.Model.FindEntityType(typeof(SharedChild))!
            .GetTieredStoreBindings()
            .Select(binding => (
                binding.BindingId,
                binding.RootEntityName,
                binding.ParentEntityName,
                binding.ParentNavigationName,
                binding.ControlKey))
            .ToArray();
        var reverseBindings = reverse.Model.FindEntityType(typeof(SharedChild))!
            .GetTieredStoreBindings()
            .Select(binding => (
                binding.BindingId,
                binding.RootEntityName,
                binding.ParentEntityName,
                binding.ParentNavigationName,
                binding.ControlKey))
            .ToArray();

        Assert.Equal(2, forwardBindings.Length);
        Assert.Equal(forwardBindings, reverseBindings);
        Assert.Null(forward.Model.FindEntityType(typeof(SharedChild))!.GetTieredStoreRoot());

        var forwardAggregates = DuckDBTierAggregate.ResolveAll(forward.Model)
            .ToDictionary(
                aggregate => aggregate.ControlKey,
                aggregate => aggregate.Nodes.Single(node => node.Entity.ClrType == typeof(SharedChild)).BindingId);
        var reverseAggregates = DuckDBTierAggregate.ResolveAll(reverse.Model)
            .ToDictionary(
                aggregate => aggregate.ControlKey,
                aggregate => aggregate.Nodes.Single(node => node.Entity.ClrType == typeof(SharedChild)).BindingId);
        Assert.Equal(forwardAggregates, reverseAggregates);
    }

    [Fact]
    public void Duplicate_root_control_keys_are_rejected_during_model_validation()
    {
        using var context = CreateContext(
            "duplicate-control.duckdb",
            "duplicate-control",
            duplicateControlKeys: true);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("same control key 'shared-root'", exception.Message);
        Assert.Contains(nameof(RootA), exception.Message);
        Assert.Contains(nameof(RootB), exception.Message);
    }

    [Fact]
    public async Task Shared_child_archives_and_maintenance_remain_root_scoped()
    {
        var dbName = "scoped.duckdb";
        var archiveName = "scoped";
        using (var context = CreateContext(dbName, archiveName))
        {
            context.Database.EnsureCreated();
            SeedIndependentRoots(context);

            var rootAArchive = await context.Database.ArchiveTierAsync<RootA>(new DateTime(2024, 2, 1));

            Assert.Equal("root-a", rootAArchive.Binding?.ControlKey);
            Assert.Single(context.RootBs);
            Assert.Single(context.SharedChildren);
            Assert.Equal([101, 202], context.SharedHistory.OrderBy(child => child.Id).Select(child => child.Id).ToArray());
            Assert.All(rootAArchive.Nodes, node => Assert.False(string.IsNullOrWhiteSpace(node.BindingId)));
        }

        using (var restarted = CreateContext(dbName, archiveName))
        {
            restarted.Database.EnsureTieredStoresCreated();
            Assert.Equal([101, 202], restarted.SharedHistory.OrderBy(child => child.Id).Select(child => child.Id).ToArray());

            var rootBArchive = await restarted.Database.ArchiveTierAsync<RootB>(new DateTime(2024, 2, 1));

            Assert.Equal("root-b", rootBArchive.Binding?.ControlKey);
            Assert.Empty(restarted.SharedChildren);
            Assert.Equal([101, 202], restarted.SharedHistory.OrderBy(child => child.Id).Select(child => child.Id).ToArray());

            var rootBBefore = await restarted.Database.GetArchiveGenerationInventoryAsync<RootB>();
            var compactedA = await restarted.Database.CompactArchiveTierAsync<RootA>();
            var rootBAfter = await restarted.Database.GetArchiveGenerationInventoryAsync<RootB>();

            Assert.Equal("root-a", compactedA.Binding?.ControlKey);
            Assert.Equal("root-b", rootBBefore.Binding?.ControlKey);
            Assert.Equal("root-b", rootBAfter.Binding?.ControlKey);
            Assert.Equal(rootBBefore.ActiveGenerationId, rootBAfter.ActiveGenerationId);
            Assert.Equal(InventorySnapshot(rootBBefore), InventorySnapshot(rootBAfter));

            var rootAInventory = await restarted.Database.GetArchiveGenerationInventoryAsync<RootA>();
            Assert.Equal("root-a", rootAInventory.Binding?.ControlKey);
            var publishedA = rootAInventory.Generations.Single(
                generation => generation.State == TierArchiveGenerationState.Published);
            var cleanup = await restarted.Database.PlanArchiveGenerationCleanupAsync<RootA>(
                [publishedA.GenerationId]);
            Assert.Equal("root-a", cleanup.ControlKey);
            Assert.Equal("root-a", cleanup.Binding?.ControlKey);

            var restored = await restarted.Database.RestoreArchiveTierAsync<RootA>(
                new TierRestoreOptions
                {
                    Scope = TierMaintenanceScope.ForRootMatchKeys(
                        TierRowIdentity.For<RootA>(
                            new Dictionary<string, object?> { [nameof(RootA.Id)] = 1 })),
                });

            Assert.Equal("root-a", restored.Publication.Binding?.ControlKey);
            Assert.Single(restarted.RootAs);
            Assert.Single(restarted.SharedChildren);
            Assert.Empty(restarted.RootBs);
            Assert.Equal([101, 202], restarted.SharedHistory.OrderBy(child => child.Id).Select(child => child.Id).ToArray());

            var rootBAfterRestore = await restarted.Database.GetArchiveGenerationInventoryAsync<RootB>();
            Assert.Equal(rootBAfter.ActiveGenerationId, rootBAfterRestore.ActiveGenerationId);
            Assert.Equal(InventorySnapshot(rootBAfter), InventorySnapshot(rootBAfterRestore));
        }
    }

    [Fact]
    public async Task Retention_trim_refreshes_a_shared_child_view_without_changing_peer_generation()
    {
        using var context = CreateContext("retention-shared.duckdb", "retention-shared");
        context.Database.EnsureCreated();
        SeedIndependentRoots(context);
        await context.Database.ArchiveTierAsync<RootA>(new DateTime(2024, 2, 1));
        await context.Database.ArchiveTierAsync<RootB>(new DateTime(2024, 2, 1));
        var rootBBefore = await context.Database.GetArchiveGenerationInventoryAsync<RootB>();

        var plan = await context.Database.PlanArchiveRetentionAsync<RootA>(
            new TierArchiveRetentionOptions { RetainFrom = new DateTime(2024, 2, 1) });
        var result = await context.Database.PublishArchiveRetentionAsync<RootA>(plan);

        Assert.Equal(TierArchiveOperation.RetentionTrim, result.Operation);
        Assert.Empty(context.RootAHistory);
        Assert.Single(context.RootBHistory);
        Assert.Equal([202], context.SharedHistory.Select(child => child.Id).ToArray());
        var rootBAfter = await context.Database.GetArchiveGenerationInventoryAsync<RootB>();
        Assert.Equal(rootBBefore.ActiveGenerationId, rootBAfter.ActiveGenerationId);
        Assert.Equal(InventorySnapshot(rootBBefore), InventorySnapshot(rootBAfter));
    }

    [Fact]
    public async Task Shared_child_archived_key_conflict_identifies_the_selected_root_binding()
    {
        using var context = CreateContext("binding-conflict.duckdb", "binding-conflict");
        context.Database.EnsureCreated();
        SeedIndependentRoots(context);
        var archived = await context.Database.ArchiveTierAsync<RootA>(new DateTime(2024, 2, 1));
        context.ChangeTracker.Clear();
        context.RootAs.Add(new RootA
        {
            Id = 1,
            ArchivedAt = new DateTime(2024, 1, 10),
        });
        context.SharedChildren.Add(new SharedChild
        {
            Id = 101,
            RootAId = 1,
            Value = "corrected",
        });
        context.SaveChanges();

        var exception = await Assert.ThrowsAsync<TierArchivedKeyConflictException>(
            () => context.Database.ArchiveTierAsync<RootA>(new DateTime(2024, 2, 1)));

        Assert.Equal(typeof(SharedChild), exception.EntityType);
        Assert.Equal(archived.Binding, exception.Binding);
        Assert.Equal("root-a", exception.Binding?.ControlKey);
        Assert.Contains("control 'root-a'", exception.Message);
        Assert.Equal("corrected", context.SharedChildren.Single(child => child.Id == 101).Value);
    }

    [Fact]
    public async Task Reversed_registration_and_archive_order_produces_the_same_history()
    {
        using var context = CreateContext(
            "reverse-order.duckdb",
            "reverse-order",
            reverseRegistration: true);
        context.Database.EnsureCreated();
        SeedIndependentRoots(context);

        await context.Database.ArchiveTierAsync<RootB>(new DateTime(2024, 2, 1));
        Assert.Single(context.SharedChildren);
        Assert.Equal([101, 202], context.SharedHistory.OrderBy(child => child.Id).Select(child => child.Id).ToArray());

        await context.Database.ArchiveTierAsync<RootA>(new DateTime(2024, 2, 1));
        Assert.Empty(context.SharedChildren);
        Assert.Equal([101, 202], context.SharedHistory.OrderBy(child => child.Id).Select(child => child.Id).ToArray());
    }

    [Fact]
    public async Task Ambiguous_shared_child_fails_before_archive_write_or_hot_delete()
    {
        var archiveName = "ambiguous";
        using var context = CreateContext("ambiguous.duckdb", archiveName);
        context.Database.EnsureCreated();
        context.RootAs.Add(new RootA { Id = 1, ArchivedAt = new DateTime(2024, 1, 10) });
        context.RootBs.Add(new RootB { Id = 2, ArchivedAt = new DateTime(2024, 1, 11) });
        context.SharedChildren.Add(new SharedChild
        {
            Id = 303,
            RootAId = 1,
            RootBId = 2,
            Value = "ambiguous",
        });
        context.SaveChanges();

        var preflight = await context.Database.PreflightTieredStorageAsync<RootA>();
        var ownership = preflight.Capabilities.Single(
            capability => capability.Capability == TierStorageCapability.BindingOwnership);
        Assert.False(ownership.Supported);
        Assert.Equal("root-a", preflight.Binding?.ControlKey);

        var exception = await Assert.ThrowsAsync<TierAmbiguousBindingException>(
            () => context.Database.ArchiveTierAsync<RootA>(new DateTime(2024, 2, 1)));

        Assert.Equal("shared_children", exception.Table);
        Assert.Equal(1, exception.AmbiguousRows);
        Assert.Equal(["root-a", "root-b"], exception.Bindings.Select(binding => binding.ControlKey).Order().ToArray());
        Assert.Single(context.RootAs);
        Assert.Single(context.RootBs);
        Assert.Single(context.SharedChildren);
        Assert.False(Directory.Exists(Path.Combine(_root, archiveName, "a", "shared_children")));
        Assert.False(Directory.Exists(Path.Combine(_root, archiveName, "b", "shared_children")));
    }

    [Fact]
    public async Task Ambiguous_shared_child_blocks_contract_rewrite_before_copy()
    {
        var dbName = "ambiguous-rewrite.duckdb";
        var archiveName = "ambiguous-rewrite";
        using (var original = CreateContext(dbName, archiveName))
        {
            original.Database.EnsureCreated();
            original.RootAs.Add(new RootA
            {
                Id = 1,
                ArchivedAt = new DateTime(2024, 1, 10),
                Children =
                [
                    new SharedChild
                    {
                        Id = 101,
                        Value = "archived",
                    },
                ],
            });
            original.SaveChanges();
            await original.Database.ArchiveTierAsync<RootA>(new DateTime(2024, 2, 1));
            original.ChangeTracker.Clear();
            original.Database.ExecuteSqlRaw("ALTER TABLE root_a ADD COLUMN \"Note\" TEXT;");
            original.RootAs.Add(new RootA { Id = 10, ArchivedAt = new DateTime(2024, 3, 10) });
            original.RootBs.Add(new RootB { Id = 20, ArchivedAt = new DateTime(2024, 3, 11) });
            original.SharedChildren.Add(new SharedChild
            {
                Id = 303,
                RootAId = 10,
                RootBId = 20,
                Value = "ambiguous",
            });
            original.SaveChanges();
        }

        using var evolved = CreateContext(dbName, archiveName, includeRootANote: true);
        var plan = await evolved.Database.PlanArchiveContractRewriteAsync<RootA>(
            new TierArchiveRewriteOptions());
        Assert.Contains(
            plan.Inspection.Differences,
            difference => difference.Kind == TierArchiveContractDifferenceKind.ColumnAdded
                          && difference.Column == nameof(RootA.Note));
        var revisionsPath = Path.Combine(_root, archiveName, "a", "_revisions");
        Assert.False(Directory.Exists(revisionsPath));

        var exception = await Assert.ThrowsAsync<TierAmbiguousBindingException>(
            () => evolved.Database.RewriteArchiveContractAsync<RootA>(plan));

        Assert.Equal("shared_children", exception.Table);
        Assert.Equal(["root-a", "root-b"], exception.Bindings.Select(binding => binding.ControlKey).Order().ToArray());
        Assert.False(Directory.Exists(revisionsPath));
        Assert.Single(evolved.SharedChildren);
    }

    [Fact]
    public async Task Shared_binding_preflight_is_safe_before_the_database_schema_exists()
    {
        using var context = CreateContext("preflight-empty.duckdb", "preflight-empty");

        var preflight = await context.Database.PreflightTieredStorageAsync<RootA>();

        Assert.True(preflight.Succeeded);
        Assert.Equal("root-a", preflight.Binding?.ControlKey);
        Assert.Contains(
            preflight.Capabilities,
            capability => capability.Capability == TierStorageCapability.BindingOwnership
                          && capability.Supported);
    }

    private SharedBindingContext CreateContext(
        string databaseName,
        string archiveName,
        bool reverseRegistration = false,
        bool duplicateControlKeys = false,
        bool includeRootANote = false)
        => new(
            Path.Combine(_root, databaseName),
            Path.Combine(_root, archiveName),
            reverseRegistration,
            duplicateControlKeys,
            includeRootANote);

    private static void SeedIndependentRoots(SharedBindingContext context)
    {
        context.RootAs.Add(new RootA
        {
            Id = 1,
            ArchivedAt = new DateTime(2024, 1, 10),
            Children =
            [
                new SharedChild
                {
                    Id = 101,
                    Value = "root-a",
                },
            ],
        });
        context.RootBs.Add(new RootB
        {
            Id = 2,
            ArchivedAt = new DateTime(2024, 1, 11),
            Children =
            [
                new SharedChild
                {
                    Id = 202,
                    Value = "root-b",
                },
            ],
        });
        context.SaveChanges();
    }

    private static object[] InventorySnapshot(TierArchiveGenerationInventory inventory)
        => inventory.Generations.Select(generation => (object)new
        {
            generation.GenerationId,
            generation.State,
            generation.ArchivePath,
            generation.Watermark,
            generation.FileCount,
            generation.TotalBytes,
            Files = string.Join("\n", generation.RepresentativeFiles.Order()),
        }).ToArray();

    private sealed class RootA
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
        public string? Note { get; set; }
        public List<SharedChild> Children { get; set; } = [];
    }

    private sealed class RootB
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
        public List<SharedChild> Children { get; set; } = [];
    }

    private sealed class SharedChild
    {
        public int Id { get; set; }
        public int? RootAId { get; set; }
        public RootA? RootA { get; set; }
        public int? RootBId { get; set; }
        public RootB? RootB { get; set; }
        public string Value { get; set; } = null!;
    }

    private sealed class RootAHistory
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
        public string? Note { get; set; }
    }

    private sealed class RootBHistory
    {
        public int Id { get; set; }
        public DateTime ArchivedAt { get; set; }
    }

    private sealed class SharedChildHistory
    {
        public int Id { get; set; }
        public int? RootAId { get; set; }
        public int? RootBId { get; set; }
        public string Value { get; set; } = null!;
    }

    private sealed class SharedBindingContext(
        string dbPath,
        string archivePath,
        bool reverseRegistration,
        bool duplicateControlKeys,
        bool includeRootANote) : DbContext
    {
        public DbSet<RootA> RootAs => Set<RootA>();
        public DbSet<RootB> RootBs => Set<RootB>();
        public DbSet<SharedChild> SharedChildren => Set<SharedChild>();
        public DbSet<RootAHistory> RootAHistory => Set<RootAHistory>();
        public DbSet<RootBHistory> RootBHistory => Set<RootBHistory>();
        public DbSet<SharedChildHistory> SharedHistory => Set<SharedChildHistory>();
        public string ModelKey
            => archivePath + "|" + reverseRegistration + "|" + duplicateControlKeys + "|" + includeRootANote;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, SharedBindingModelCacheKeyFactory>()
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RootA>(builder =>
            {
                builder.ToTable("root_a");
                builder.HasKey(root => root.Id);
                builder.HasMany(root => root.Children)
                    .WithOne(child => child.RootA)
                    .HasForeignKey(child => child.RootAId);
                if (!includeRootANote)
                {
                    builder.Ignore(root => root.Note);
                }
            });
            modelBuilder.Entity<RootB>(builder =>
            {
                builder.ToTable("root_b");
                builder.HasKey(root => root.Id);
                builder.HasMany(root => root.Children)
                    .WithOne(child => child.RootB)
                    .HasForeignKey(child => child.RootBId);
            });
            modelBuilder.Entity<SharedChild>(builder =>
            {
                builder.ToTable("shared_children");
                builder.HasKey(child => child.Id);
            });
            modelBuilder.Entity<RootAHistory>(builder =>
            {
                if (!includeRootANote)
                {
                    builder.Ignore(root => root.Note);
                }
            });

            var rootAControlKey = duplicateControlKeys ? "shared-root" : "root-a";
            var rootBControlKey = duplicateControlKeys ? "shared-root" : "root-b";

            void ConfigureA()
                => modelBuilder.ToTieredStore<RootA>(
                        root => root.ArchivedAt,
                        Path.Combine(archivePath, "a"),
                        controlKey: rootAControlKey)
                    .WithReadModel<RootAHistory>()
                    .Including<SharedChild>(
                        root => root.Children,
                        child => child.WithReadModel<SharedChildHistory>());

            void ConfigureB()
                => modelBuilder.ToTieredStore<RootB>(
                        root => root.ArchivedAt,
                        Path.Combine(archivePath, "b"),
                        controlKey: rootBControlKey)
                    .WithReadModel<RootBHistory>()
                    .Including<SharedChild>(
                        root => root.Children,
                        child => child.WithReadModel<SharedChildHistory>());

            if (reverseRegistration)
            {
                ConfigureB();
                ConfigureA();
            }
            else
            {
                ConfigureA();
                ConfigureB();
            }
        }
    }

    private sealed class SharedBindingModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => context.GetType().GetProperty("ModelKey")?.GetValue(context) is string modelKey
                ? (context.GetType(), modelKey, designTime)
                : (object)(context.GetType(), designTime);
    }
}
