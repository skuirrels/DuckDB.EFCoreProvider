using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Update.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class ReferencedPrincipalUpdateTests : DuckDBTestBase
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Referenced_principal_update_refreshes_store_generated_value(
        bool enableBulkUpdateBatching,
        bool async)
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching);

        await using var context = CreateContext(interceptor, enableBulkUpdateBatching);
        var parent = await context.Parents.SingleAsync();
        Assert.Equal("BEFORE", parent.ComputedValue);
        interceptor.CommandTexts.Clear();

        parent.Payload = "after";
        if (async)
        {
            await context.SaveChangesAsync();
        }
        else
        {
            context.SaveChanges();
        }

        Assert.Equal("AFTER", parent.ComputedValue);

        var updateSql = Assert.Single(
            interceptor.CommandTexts.Where(command => command.StartsWith("UPDATE ", StringComparison.Ordinal)));
        Assert.DoesNotContain("RETURNING", updateSql, StringComparison.OrdinalIgnoreCase);

        var selectSql = Assert.Single(
            interceptor.CommandTexts.Where(command
                => command.StartsWith("SELECT ", StringComparison.Ordinal)
                    && command.Contains("\"ComputedValue\"", StringComparison.Ordinal)));
        Assert.Contains("FROM \"Parents\"", selectSql, StringComparison.Ordinal);
        Assert.Contains("WHERE \"Id\" =", selectSql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Referenced_principal_update_without_readback_avoids_returning(
        bool enableBulkUpdateBatching)
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching);

        await using var context = CreateContext(interceptor, enableBulkUpdateBatching);
        var parent = await context.PlainParents.SingleAsync();
        interceptor.CommandTexts.Clear();

        parent.Payload = "after";
        await context.SaveChangesAsync();

        var updateSql = Assert.Single(
            interceptor.CommandTexts.Where(command => command.StartsWith("UPDATE ", StringComparison.Ordinal)));
        Assert.DoesNotContain("RETURNING", updateSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            interceptor.CommandTexts,
            command => command.StartsWith("SELECT ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bulk_update_batching_merges_referenced_principal_updates_without_readback()
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching: true);

        await using (var seedContext = CreateContext(interceptor, enableBulkUpdateBatching: true))
        {
            var referencedId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            seedContext.Add(
                new PlainParent
                {
                    Id = 2,
                    ExternalId = referencedId,
                    Payload = "before",
                    Children =
                    [
                        new PlainChild
                        {
                            Id = 2,
                            ParentExternalId = referencedId
                        }
                    ]
                });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(interceptor, enableBulkUpdateBatching: true);
        var parents = await context.PlainParents.OrderBy(parent => parent.Id).ToListAsync();
        interceptor.CommandTexts.Clear();

        foreach (var parent in parents)
        {
            parent.Payload = "after";
        }

        await context.SaveChangesAsync();

        var updateSql = Assert.Single(
            interceptor.CommandTexts.Where(command => command.StartsWith("UPDATE ", StringComparison.Ordinal)));
        Assert.Contains("FROM (VALUES", updateSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RETURNING", updateSql, StringComparison.OrdinalIgnoreCase);

        context.ChangeTracker.Clear();
        Assert.All(
            await context.PlainParents.AsNoTracking().ToListAsync(),
            parent => Assert.Equal("after", parent.Payload));
    }

    [Fact]
    public void Referenced_table_index_is_cached_and_distinguishes_unreferenced_tables()
    {
        var interceptor = new CommandCaptureInterceptor();
        using var context = CreateContext(interceptor, enableBulkUpdateBatching: false);

        var first = DuckDBReferencedTableIndex.For(context.Model);
        var second = DuckDBReferencedTableIndex.For(context.Model);

        Assert.Same(first, second);
        Assert.True(first.Contains("Parents", schema: null));
        Assert.True(first.Contains("PlainParents", schema: null));
        Assert.False(first.Contains("IndependentItems", schema: null));
    }

    [Fact]
    public async Task Referenced_principal_update_uses_returning_when_capability_allows_it()
    {
        await using var serviceProvider = new ServiceCollection()
            .AddEntityFrameworkDuckDB()
            .AddSingleton<IDuckDBEngineCapabilities, ReferencedUpdateReturningCapabilities>()
            .BuildServiceProvider(validateScopes: true);
        var interceptor = new CommandCaptureInterceptor();
        await using var context = CreateContext(
            interceptor,
            enableBulkUpdateBatching: false,
            serviceProvider);
        await context.Database.EnsureCreatedAsync();
        context.Add(
            new Parent
            {
                Id = 1,
                ExternalId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Payload = "before"
            });
        await context.SaveChangesAsync();
        interceptor.CommandTexts.Clear();

        var parent = await context.Parents.SingleAsync();
        parent.Payload = "after";
        interceptor.CommandTexts.Clear();
        await context.SaveChangesAsync();

        Assert.Equal("AFTER", parent.ComputedValue);
        var updateSql = Assert.Single(
            interceptor.CommandTexts.Where(command => command.StartsWith("UPDATE ", StringComparison.Ordinal)));
        Assert.Contains("RETURNING", updateSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            interceptor.CommandTexts,
            command => command.StartsWith("SELECT ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Referenced_principal_update_preserves_concurrency_exception(
        bool enableBulkUpdateBatching)
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching);

        await using var firstContext = CreateContext(interceptor, enableBulkUpdateBatching);
        await using var staleContext = CreateContext(interceptor, enableBulkUpdateBatching);
        var first = await firstContext.Parents.SingleAsync();
        var stale = await staleContext.Parents.SingleAsync();

        first.Payload = "first-writer";
        await firstContext.SaveChangesAsync();

        stale.Payload = "stale-writer";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Bulk_update_batching_isolates_referenced_principal_readback()
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching: true);

        await using var context = CreateContext(interceptor, enableBulkUpdateBatching: true);
        var parent = await context.Parents.SingleAsync();
        var independent = await context.IndependentItems.SingleAsync();
        interceptor.CommandTexts.Clear();

        parent.Payload = "after";
        independent.Payload = "also-after";
        await context.SaveChangesAsync();

        Assert.Equal("AFTER", parent.ComputedValue);
        Assert.Equal(2, interceptor.CommandTexts.Count(command
            => command.StartsWith("UPDATE ", StringComparison.Ordinal)));

        context.ChangeTracker.Clear();
        Assert.Equal("after", (await context.Parents.SingleAsync()).Payload);
        Assert.Equal("also-after", (await context.IndependentItems.SingleAsync()).Payload);
    }

    [Fact]
    public async Task Readback_failure_rolls_back_referenced_principal_update()
    {
        var interceptor = new CommandCaptureInterceptor();
        await SeedAsync(interceptor, enableBulkUpdateBatching: false);

        await using (var context = CreateContext(interceptor, enableBulkUpdateBatching: false))
        {
            var parent = await context.Parents.SingleAsync();
            parent.Payload = "must-roll-back";
            interceptor.FailComputedReadback = true;

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        interceptor.FailComputedReadback = false;
        await using var verification = CreateContext(interceptor, enableBulkUpdateBatching: false);
        var persisted = await verification.Parents.AsNoTracking().SingleAsync();
        Assert.Equal("before", persisted.Payload);
        Assert.Equal("BEFORE", persisted.ComputedValue);
    }

    private async Task SeedAsync(
        CommandCaptureInterceptor interceptor,
        bool enableBulkUpdateBatching)
    {
        await using var context = CreateContext(interceptor, enableBulkUpdateBatching);
        await context.Database.EnsureCreatedAsync();

        var referencedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        context.Add(
            new Parent
            {
                Id = 1,
                ExternalId = referencedId,
                Payload = "before",
                Children =
                [
                    new Child
                    {
                        Id = 1,
                        ParentExternalId = referencedId
                    }
                ]
            });

        var plainReferencedId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        context.Add(
            new PlainParent
            {
                Id = 1,
                ExternalId = plainReferencedId,
                Payload = "before",
                Children =
                [
                    new PlainChild
                    {
                        Id = 1,
                        ParentExternalId = plainReferencedId
                    }
                ]
            });

        context.Add(new IndependentItem { Id = 1, Payload = "before" });

        await context.SaveChangesAsync();
        interceptor.CommandTexts.Clear();
    }

    private ReferencedPrincipalContext CreateContext(
        CommandCaptureInterceptor interceptor,
        bool enableBulkUpdateBatching,
        IServiceProvider? internalServiceProvider = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ReferencedPrincipalContext>()
            .UseDuckDB(
                $"DataSource={DbPath}",
                duckdb =>
                {
                    if (enableBulkUpdateBatching)
                    {
                        duckdb.EnableBulkUpdateBatching();
                    }
                })
            .AddInterceptors(interceptor);

        if (internalServiceProvider is not null)
        {
            optionsBuilder.UseInternalServiceProvider(internalServiceProvider);
        }

        return new ReferencedPrincipalContext(optionsBuilder.Options);
    }

    private sealed class ReferencedPrincipalContext(
        DbContextOptions<ReferencedPrincipalContext> options)
        : DbContext(options)
    {
        public DbSet<Parent> Parents => Set<Parent>();
        public DbSet<PlainParent> PlainParents => Set<PlainParent>();
        public DbSet<IndependentItem> IndependentItems => Set<IndependentItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Parent>(entity =>
            {
                entity.Property(parent => parent.Id).ValueGeneratedNever();
                entity.HasAlternateKey(parent => parent.ExternalId);
                entity.Property(parent => parent.Payload).IsConcurrencyToken();
                entity.Property(parent => parent.ComputedValue)
                    .HasComputedColumnSql("upper(\"Payload\")");
            });

            modelBuilder.Entity<Child>(entity =>
            {
                entity.Property(child => child.Id).ValueGeneratedNever();
                entity.HasOne<Parent>()
                    .WithMany(parent => parent.Children)
                    .HasForeignKey(child => child.ParentExternalId)
                    .HasPrincipalKey(parent => parent.ExternalId);
            });

            modelBuilder.Entity<PlainParent>(entity =>
            {
                entity.Property(parent => parent.Id).ValueGeneratedNever();
                entity.HasAlternateKey(parent => parent.ExternalId);
            });

            modelBuilder.Entity<PlainChild>(entity =>
            {
                entity.Property(child => child.Id).ValueGeneratedNever();
                entity.HasOne<PlainParent>()
                    .WithMany(parent => parent.Children)
                    .HasForeignKey(child => child.ParentExternalId)
                    .HasPrincipalKey(parent => parent.ExternalId);
            });

            modelBuilder.Entity<IndependentItem>()
                .Property(item => item.Id)
                .ValueGeneratedNever();
        }
    }

    private sealed class Parent
    {
        public long Id { get; set; }
        public Guid ExternalId { get; set; }
        public string Payload { get; set; } = "";
        public string? ComputedValue { get; private set; }
        public List<Child> Children { get; set; } = [];
    }

    private sealed class Child
    {
        public long Id { get; set; }
        public Guid ParentExternalId { get; set; }
    }

    private sealed class PlainParent
    {
        public long Id { get; set; }
        public Guid ExternalId { get; set; }
        public string Payload { get; set; } = "";
        public List<PlainChild> Children { get; set; } = [];
    }

    private sealed class PlainChild
    {
        public long Id { get; set; }
        public Guid ParentExternalId { get; set; }
    }

    private sealed class IndependentItem
    {
        public long Id { get; set; }
        public string Payload { get; set; } = "";
    }

    private sealed class ReferencedUpdateReturningCapabilities : IDuckDBEngineCapabilities
    {
        public bool SupportsReturning => true;
        public bool SupportsReturningOnReferencedTableUpdates => true;
        public bool SupportsSaveChangesBatching => true;
        public bool SupportsSequences => true;
        public bool SupportsGeneratedColumns => true;
        public bool SupportsSqlDefaultExpressions => true;
        public bool SupportsIndexes => true;
        public bool SupportsSchemaConstraints => true;
        public bool SupportsTieredStorage => true;
        public bool SupportsEfMigrations => true;
        public DuckDBUpsertStrategy UpsertStrategy => DuckDBUpsertStrategy.InsertOnConflict;
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> CommandTexts { get; } = [];

        public bool FailComputedReadback { get; set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Capture(command);
            FailReadbackIfRequested(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            FailReadbackIfRequested(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Capture(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }

        private void Capture(DbCommand command)
            => CommandTexts.Add(command.CommandText);

        private void FailReadbackIfRequested(DbCommand command)
        {
            if (FailComputedReadback
                && command.CommandText.StartsWith("SELECT \"ComputedValue\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FROM \"Parents\"", StringComparison.Ordinal))
            {
                command.CommandText = "SELECT missing_readback_column FROM \"Parents\";";
            }
        }
    }
}