using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Internal;
using DuckDB.EFCoreProvider.Update.Internal;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class DualRolePrincipalUpdateTests : DuckDBTestBase
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Dual_role_principal_scalar_update_succeeds(
        bool enableBulkUpdateBatching,
        bool async)
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching, rootCount: 1);

        await using (var context = CreateContext(interceptor, enableBulkUpdateBatching))
        {
            var root = await context.Roots.SingleAsync();
            var originalExternalId = root.ExternalId;
            interceptor.CommandTexts.Clear();

            root.Payload = "after";
            if (async)
            {
                await context.SaveChangesAsync();
            }
            else
            {
                context.SaveChanges();
            }

            Assert.Equal(1, root.Id);
            Assert.Equal(originalExternalId, root.ExternalId);
            Assert.Equal(10, root.LookupId);
            Assert.Equal("AFTER", root.ComputedValue);

            var updateSql = Assert.Single(UpdateCommands(interceptor));
            Assert.DoesNotContain("RETURNING", updateSql, StringComparison.OrdinalIgnoreCase);
        }

        await AssertPersistedGraphAsync(
            expectedPayloads: ["after"],
            expectedLookupIds: [10]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dual_role_principal_unchanged_outbound_fk_write_is_omitted(
        bool enableBulkUpdateBatching)
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching, rootCount: 1);

        await using (var context = CreateContext(interceptor, enableBulkUpdateBatching))
        {
            var root = await context.Roots.SingleAsync();
            var originalExternalId = root.ExternalId;
            root.Payload = "after";
            context.Entry(root).Property(item => item.LookupId).IsModified = true;
            Assert.Equal(10, context.Entry(root).Property(item => item.LookupId).OriginalValue);
            Assert.Equal(10, root.LookupId);
            interceptor.CommandTexts.Clear();

            await context.SaveChangesAsync();

            var updateSql = Assert.Single(UpdateCommands(interceptor));
            Assert.DoesNotContain("\"LookupId\" =", updateSql, StringComparison.Ordinal);
            Assert.Equal(1, root.Id);
            Assert.Equal(originalExternalId, root.ExternalId);
            Assert.Equal(10, root.LookupId);
            Assert.Equal("AFTER", root.ComputedValue);
        }

        await AssertPersistedGraphAsync(
            expectedPayloads: ["after"],
            expectedLookupIds: [10]);
    }

    [Fact]
    public async Task Bulk_update_omits_unchanged_outbound_fk_columns()
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedWithoutComputedAsync(interceptor, rootCount: 2);

        await using (var context = CreateContextWithoutComputed(interceptor))
        {
            var roots = await context.Roots.OrderBy(root => root.Id).ToListAsync();
            for (var i = 0; i < roots.Count; i++)
            {
                roots[i].Payload = $"after-{i + 1}";
                context.Entry(roots[i]).Property(item => item.LookupId).IsModified = true;
            }

            interceptor.CommandTexts.Clear();
            await context.SaveChangesAsync();

            var updateSql = Assert.Single(UpdateCommands(interceptor));
            Assert.Contains("FROM (VALUES", updateSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"LookupId\" =", updateSql, StringComparison.Ordinal);
            Assert.DoesNotContain("AS v(\"Id\", \"LookupId\"", updateSql, StringComparison.Ordinal);
        }

        await using var verification = CreateContextWithoutComputed(new CommandCaptureInterceptor());
        var persistedRoots = await verification.Roots.AsNoTracking().OrderBy(root => root.Id).ToListAsync();
        Assert.Equal(["after-1", "after-2"], persistedRoots.Select(root => root.Payload));
        Assert.All(persistedRoots, root => Assert.Equal(10, root.LookupId));
        Assert.Equal(2, await verification.Children.CountAsync());
        Assert.All(
            await verification.Children.AsNoTracking().OrderBy(child => child.Id).ToListAsync(),
            child => Assert.Equal(ExternalIdFor(child.Id), child.RootExternalId));
    }

    [Fact]
    public async Task Sole_unchanged_outbound_fk_write_is_conditional_with_child()
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(
            interceptor,
            enableBulkUpdateBatching: false,
            rootCount: 1);

        await using var context = CreateContext(interceptor, enableBulkUpdateBatching: false);
        var root = await context.Roots.SingleAsync();
        context.Entry(root).Property(item => item.LookupId).IsModified = true;
        interceptor.CommandTexts.Clear();

        await context.SaveChangesAsync();

        var updateSql = Assert.Single(UpdateCommands(interceptor));
        Assert.Contains("\"LookupId\" IS DISTINCT FROM", updateSql, StringComparison.Ordinal);
        Assert.Contains("SELECT CAST(COUNT(*) AS INTEGER)", updateSql, StringComparison.Ordinal);

        await AssertPersistedGraphAsync(
            expectedPayloads: ["before-1"],
            expectedLookupIds: [10]);
    }

    [Fact]
    public async Task Sole_unchanged_outbound_fk_conditional_write_preserves_concurrency_check()
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching: false, rootCount: 1);

        await using var context = CreateContext(interceptor, enableBulkUpdateBatching: false);
        var root = await context.Roots.SingleAsync();
        context.Entry(root).Property(item => item.LookupId).IsModified = true;

        await using (var deletionContext = CreateContext(
                         new CommandCaptureInterceptor(),
                         enableBulkUpdateBatching: false))
        {
            await deletionContext.Database.ExecuteSqlRawAsync("DELETE FROM \"Children\"");
            await deletionContext.Database.ExecuteSqlRawAsync("DELETE FROM \"Roots\"");
        }

        interceptor.CommandTexts.Clear();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());

        var updateSql = Assert.Single(UpdateCommands(interceptor));
        Assert.Contains("\"LookupId\" IS DISTINCT FROM", updateSql, StringComparison.Ordinal);
        Assert.Contains("SELECT CAST(COUNT(*) AS INTEGER)", updateSql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detached_equal_snapshot_outbound_fk_write_preserves_stored_change()
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(
            interceptor,
            enableBulkUpdateBatching: false,
            rootCount: 1,
            includeChildren: false);

        await using (var context = CreateContext(interceptor, enableBulkUpdateBatching: false))
        {
            var root = new Root { Id = 1, LookupId = 20 };
            context.Attach(root).Property(item => item.LookupId).IsModified = true;
            Assert.Equal(20, context.Entry(root).Property(item => item.LookupId).OriginalValue);

            interceptor.CommandTexts.Clear();
            await context.SaveChangesAsync();

            var updateSql = Assert.Single(UpdateCommands(interceptor));
            Assert.Contains("\"LookupId\" IS DISTINCT FROM", updateSql, StringComparison.Ordinal);
        }

        await using var verification = CreateContext(new CommandCaptureInterceptor(), enableBulkUpdateBatching: false);
        Assert.Equal(20, (await verification.Roots.AsNoTracking().SingleAsync()).LookupId);
    }

    [Fact]
    public async Task Detached_equal_snapshot_outbound_fk_change_with_child_is_reported_atomically()
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching: false, rootCount: 1);

        await using (var context = CreateContext(interceptor, enableBulkUpdateBatching: false))
        {
            var root = new Root { Id = 1, LookupId = 20 };
            context.Attach(root).Property(item => item.LookupId).IsModified = true;
            Assert.Equal(20, context.Entry(root).Property(item => item.LookupId).OriginalValue);

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Contains("unsupported dual-role foreign-key update", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Roots", exception.Message, StringComparison.Ordinal);
            Assert.Contains("LookupId", exception.Message, StringComparison.Ordinal);
        }

        await AssertPersistedGraphAsync(
            expectedPayloads: ["before-1"],
            expectedLookupIds: [10]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dual_role_principal_changed_outbound_fk_with_child_is_reported_atomically(
        bool enableBulkUpdateBatching)
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching, rootCount: 1);

        await using (var context = CreateContext(interceptor, enableBulkUpdateBatching))
        {
            var root = await context.Roots.SingleAsync();
            root.Payload = "must-roll-back";
            root.LookupId = 20;

            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            Assert.Contains("unsupported dual-role foreign-key update", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Roots", exception.Message, StringComparison.Ordinal);
            Assert.Contains("LookupId", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("identity changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        await AssertPersistedGraphAsync(
            expectedPayloads: ["before-1"],
            expectedLookupIds: [10]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dual_role_principal_changed_outbound_fk_without_child_succeeds(
        bool enableBulkUpdateBatching)
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching, rootCount: 1, includeChildren: false);

        await using (var context = CreateContext(interceptor, enableBulkUpdateBatching))
        {
            var root = await context.Roots.SingleAsync();
            root.LookupId = 20;
            await context.SaveChangesAsync();
            Assert.Equal(20, root.LookupId);
        }

        await using var verification = CreateContext(new CommandCaptureInterceptor(), enableBulkUpdateBatching);
        Assert.Equal(20, (await verification.Roots.AsNoTracking().SingleAsync()).LookupId);
        Assert.Empty(await verification.Children.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Custom_value_comparer_does_not_hide_genuine_outbound_fk_change()
    {
        var interceptor = new CommandCaptureInterceptor();
        await using (var seedContext = CreateLooseComparerContext(interceptor))
        {
            await SeedCoreAsync(seedContext, rootCount: 1, includeChildren: false);
        }

        await using (var context = CreateLooseComparerContext(interceptor))
        {
            var root = await context.Roots.SingleAsync();
            root.Payload = "after";
            root.LookupId = 20;
            context.Entry(root).Property(item => item.LookupId).IsModified = true;

            await context.SaveChangesAsync();
        }

        await using var verification = CreateLooseComparerContext(new CommandCaptureInterceptor());
        var persisted = await verification.Roots.AsNoTracking().SingleAsync();
        Assert.Equal("after", persisted.Payload);
        Assert.Equal(20, persisted.LookupId);
    }

    [Fact]
    public async Task Missing_outbound_target_keeps_standard_foreign_key_diagnostic()
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(
            interceptor,
            enableBulkUpdateBatching: false,
            rootCount: 1,
            includeChildren: false);

        await using var context = CreateContext(interceptor, enableBulkUpdateBatching: false);
        var root = await context.Roots.SingleAsync();
        root.LookupId = 999;

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.DoesNotContain("unsupported dual-role foreign-key update", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("foreign key", exception.InnerException!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Native_duckdb_characterisation_for_dual_role_update()
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching: false, rootCount: 1);

        await using (var scalarContext = CreateContext(interceptor, enableBulkUpdateBatching: false))
        {
            var affected = await scalarContext.Database.ExecuteSqlRawAsync(
                "UPDATE \"Roots\" SET \"Payload\" = 'raw-after' WHERE \"Id\" = 1");
            Assert.Equal(1, affected);
        }

        await using (var foreignKeyContext = CreateContext(interceptor, enableBulkUpdateBatching: false))
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => foreignKeyContext.Database.ExecuteSqlRawAsync(
                    "UPDATE \"Roots\" SET \"LookupId\" = 10 WHERE \"Id\" = 1"));
            Assert.Contains("foreign key", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        await AssertPersistedGraphAsync(
            expectedPayloads: ["raw-after"],
            expectedLookupIds: [10]);
    }

    [Fact]
    public void DuckLake_capability_guard_disables_native_dual_role_fk_planning()
    {
        Assert.True(DuckDBDualRoleUpdatePlanner.AppliesTo(DuckDBEngineCapabilities.Native));
        Assert.False(
            DuckDBDualRoleUpdatePlanner.AppliesTo(
                DuckDBEngineCapabilities.FromDuckLakeProfile(isDuckLake: true)));
    }

    private async Task SeedAsync(
        CommandCaptureInterceptor interceptor,
        bool enableBulkUpdateBatching,
        int rootCount,
        bool includeChildren = true)
    {
        await using var context = CreateContext(interceptor, enableBulkUpdateBatching);
        await SeedCoreAsync(context, rootCount, includeChildren);
        interceptor.CommandTexts.Clear();
    }

    private async Task SeedWithoutComputedAsync(CommandCaptureInterceptor interceptor, int rootCount)
    {
        await using var context = CreateContextWithoutComputed(interceptor);
        await SeedCoreAsync(context, rootCount, includeChildren: true);
        interceptor.CommandTexts.Clear();
    }

    private static async Task SeedCoreAsync(DualRoleContextBase context, int rootCount, bool includeChildren)
    {
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new Lookup { Id = 10 }, new Lookup { Id = 20 });
        for (var id = 1; id <= rootCount; id++)
        {
            var externalId = ExternalIdFor(id);
            var root = new Root
            {
                Id = id,
                ExternalId = externalId,
                LookupId = 10,
                Payload = $"before-{id}"
            };
            if (includeChildren)
            {
                root.Children.Add(new Child { Id = id, RootExternalId = externalId });
            }

            context.Add(root);
        }

        await context.SaveChangesAsync();
    }

    private async Task AssertPersistedGraphAsync(
        IReadOnlyList<string> expectedPayloads,
        IReadOnlyList<int> expectedLookupIds)
    {
        await using var context = CreateContext(new CommandCaptureInterceptor(), enableBulkUpdateBatching: false);
        var roots = await context.Roots.AsNoTracking().OrderBy(root => root.Id).ToListAsync();
        Assert.Equal(expectedPayloads, roots.Select(root => root.Payload));
        Assert.Equal(expectedLookupIds, roots.Select(root => root.LookupId));
        Assert.Equal(expectedPayloads.Select(payload => payload.ToUpperInvariant()), roots.Select(root => root.ComputedValue));
        Assert.Equal(expectedPayloads.Count, await context.Children.CountAsync());
        Assert.All(
            await context.Children.AsNoTracking().OrderBy(child => child.Id).ToListAsync(),
            child => Assert.Equal(ExternalIdFor(child.Id), child.RootExternalId));
    }

    private DualRoleContext CreateContext(
        CommandCaptureInterceptor interceptor,
        bool enableBulkUpdateBatching)
    {
        var options = CreateOptions<DualRoleContext>(interceptor, enableBulkUpdateBatching);
        return new DualRoleContext(options);
    }

    private DualRoleWithoutComputedContext CreateContextWithoutComputed(CommandCaptureInterceptor interceptor)
    {
        var options = CreateOptions<DualRoleWithoutComputedContext>(interceptor, enableBulkUpdateBatching: true);
        return new DualRoleWithoutComputedContext(options);
    }

    private DualRoleLooseValueComparerContext CreateLooseComparerContext(CommandCaptureInterceptor interceptor)
    {
        var options = CreateOptions<DualRoleLooseValueComparerContext>(interceptor, enableBulkUpdateBatching: false);
        return new DualRoleLooseValueComparerContext(options);
    }

    private DbContextOptions<TContext> CreateOptions<TContext>(
        CommandCaptureInterceptor interceptor,
        bool enableBulkUpdateBatching)
        where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>()
            .UseDuckDB(
                $"DataSource={DbPath}",
                duckdb =>
                {
                    if (enableBulkUpdateBatching)
                    {
                        duckdb.EnableBulkUpdateBatching();
                    }
                })
            .AddInterceptors(interceptor)
            .Options;

    private static IEnumerable<string> UpdateCommands(CommandCaptureInterceptor interceptor)
        => interceptor.CommandTexts.Where(command => command.StartsWith("UPDATE ", StringComparison.Ordinal));

    private static Guid ExternalIdFor(int id)
        => new($"00000000-0000-0000-0000-{id:D12}");

    private abstract class DualRoleContextBase(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Lookup> Lookups => Set<Lookup>();
        public DbSet<Root> Roots => Set<Root>();
        public DbSet<Child> Children => Set<Child>();

        protected static void ConfigureModel(ModelBuilder modelBuilder, bool computedValue)
        {
            modelBuilder.Entity<Lookup>(entity => entity.Property(item => item.Id).ValueGeneratedNever());
            modelBuilder.Entity<Root>(entity =>
            {
                entity.Property(root => root.Id).ValueGeneratedNever();
                entity.HasAlternateKey(root => root.ExternalId);
                entity.HasOne<Lookup>()
                    .WithMany()
                    .HasForeignKey(root => root.LookupId);
                if (computedValue)
                {
                    entity.Property(root => root.ComputedValue).HasComputedColumnSql("upper(\"Payload\")");
                }
                else
                {
                    entity.Ignore(root => root.ComputedValue);
                }
            });
            modelBuilder.Entity<Child>(entity =>
            {
                entity.Property(child => child.Id).ValueGeneratedNever();
                entity.HasOne<Root>()
                    .WithMany(root => root.Children)
                    .HasForeignKey(child => child.RootExternalId)
                    .HasPrincipalKey(root => root.ExternalId);
            });
        }
    }

    private sealed class DualRoleContext(DbContextOptions<DualRoleContext> options) : DualRoleContextBase(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => ConfigureModel(modelBuilder, computedValue: true);
    }

    private sealed class DualRoleWithoutComputedContext(
        DbContextOptions<DualRoleWithoutComputedContext> options) : DualRoleContextBase(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => ConfigureModel(modelBuilder, computedValue: false);
    }

    private sealed class DualRoleLooseValueComparerContext(
        DbContextOptions<DualRoleLooseValueComparerContext> options) : DualRoleContextBase(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureModel(modelBuilder, computedValue: true);
            var property = modelBuilder.Entity<Root>()
                .Property(root => root.LookupId)
                .Metadata;
            property.SetValueComparer(
                new ValueComparer<int>(
                    (left, right) => true,
                    value => 0,
                    value => value));
            property.SetProviderValueComparer(
                new ValueComparer<int>(
                    (left, right) => left == right,
                    value => value,
                    value => value));
        }
    }

    private sealed class Lookup
    {
        public int Id { get; set; }
    }

    private sealed class Root
    {
        public int Id { get; set; }
        public Guid ExternalId { get; set; }
        public int LookupId { get; set; }
        public string Payload { get; set; } = "";
        public string? ComputedValue { get; private set; }
        public List<Child> Children { get; set; } = [];
    }

    private sealed class Child
    {
        public int Id { get; set; }
        public Guid RootExternalId { get; set; }
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> CommandTexts { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            CommandTexts.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandTexts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            CommandTexts.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CommandTexts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}