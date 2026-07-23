using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Migrations;
using DuckDB.EFCoreProvider.Update.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class EngineCapabilitiesTests : DuckDBTestBase
{
    [ConditionalFact]
    public void Native_profile_exposes_native_engine_capabilities()
    {
        using var context = new CapabilityContext(FileOptions<CapabilityContext>());

        AssertCapabilities(context.GetService<IDuckDBEngineCapabilities>(), supported: true);
    }

    [ConditionalFact]
    public void DuckLake_profile_exposes_restricted_engine_capabilities()
    {
        var options = new DbContextOptionsBuilder<CapabilityContext>()
            .UseDuckLake("capabilities.ducklake")
            .Options;
        using var context = new CapabilityContext(options);

        AssertCapabilities(context.GetService<IDuckDBEngineCapabilities>(), supported: false);
    }

    [ConditionalFact]
    public void Scoped_batching_options_reuse_the_internal_service_provider()
    {
        using var defaultContext = new CapabilityContext(FileOptions<CapabilityContext>());
        using var batchingContext = new CapabilityContext(FileOptions<CapabilityContext>(options => options
            .EnableBulkInsertBatching()
            .EnableBulkUpdateBatching()
            .EnableBulkDeleteBatching()));

        Assert.Same(
            defaultContext.GetService<IDuckDBEngineCapabilities>(),
            batchingContext.GetService<IDuckDBEngineCapabilities>());

        var defaultOptions = defaultContext.GetService<IDbContextOptions>().FindExtension<DuckDBOptionsExtension>()!;
        var batchingOptions = batchingContext.GetService<IDbContextOptions>().FindExtension<DuckDBOptionsExtension>()!;
        Assert.False(defaultOptions.BulkInsertBatching);
        Assert.False(defaultOptions.BulkUpdateBatching);
        Assert.False(defaultOptions.BulkDeleteBatching);
        Assert.True(batchingOptions.BulkInsertBatching);
        Assert.True(batchingOptions.BulkUpdateBatching);
        Assert.True(batchingOptions.BulkDeleteBatching);
    }

    [ConditionalFact]
    public void Value_generation_convention_uses_sequence_capability_for_strategy()
    {
        using var serviceProvider = CreateCapabilityServiceProvider<NoSequenceCapabilities>();
        using var context = new DefaultKeyContext(
            FileOptionsWithCapabilities<DefaultKeyContext>(serviceProvider));

        var id = context.Model.FindEntityType(typeof(DefaultKeyItem))!.FindProperty(nameof(DefaultKeyItem.Id))!;

        Assert.Equal(ValueGenerated.OnAdd, id.ValueGenerated);
        Assert.Equal(DuckDBValueGenerationStrategy.None, id.GetValueGenerationStrategy());
    }

    [ConditionalFact]
    public void Migration_history_uses_migrations_capability()
    {
        using var serviceProvider = CreateCapabilityServiceProvider<NoMigrationsCapabilities>();
        using var context = new CapabilityContext(
            FileOptionsWithCapabilities<CapabilityContext>(serviceProvider));
        var repository = context.GetService<IHistoryRepository>();

        var exception = Assert.Throws<NotSupportedException>(() => repository.GetCreateIfNotExistsScript());

        Assert.Contains("migrations are not supported", exception.Message);
        Assert.Contains("configured DuckDB engine capabilities", exception.Message);
        Assert.DoesNotContain("DuckLake", exception.Message);
    }

    [ConditionalFact]
    public void Upsert_uses_configured_strategy_capability()
    {
        using var serviceProvider = CreateCapabilityServiceProvider<MergeUpsertCapabilities>();
        using var context = new CapabilityUpsertContext(
            FileOptionsWithCapabilities<CapabilityUpsertContext>(serviceProvider));
        context.Database.ExecuteSqlRaw(
            """
            CREATE TABLE "CapabilityUpsertItems" (
                "Id" INTEGER NOT NULL,
                "Name" VARCHAR NOT NULL
            );
            """);

        Assert.Equal(2, context.Upsert(
        [
            new CapabilityUpsertItem { Id = 1, Name = "first" },
            new CapabilityUpsertItem { Id = 2, Name = "second" }
        ]));
        Assert.Equal(2, context.Upsert(
        [
            new CapabilityUpsertItem { Id = 1, Name = "updated" },
            new CapabilityUpsertItem { Id = 3, Name = "third" }
        ]));

        var rows = context.CapabilityUpsertItems.AsNoTracking().OrderBy(item => item.Id).ToArray();
        Assert.Equal([1, 2, 3], rows.Select(item => item.Id));
        Assert.Equal("updated", rows[0].Name);
    }

    [ConditionalFact]
    public void Migration_capabilities_validate_sequences_independently()
    {
        using var context = new CapabilityContext(FileOptions<CapabilityContext>());
        var generator = CreateMigrationsGenerator(
            context,
            new TestCapabilities(SupportsSequences: false));

        var exception = Assert.Throws<NotSupportedException>(() => generator.Generate(
            [new CreateSequenceOperation { Name = "unsupported_sequence" }]));

        Assert.Contains("does not support sequences", exception.Message);
    }

    [ConditionalFact]
    public void Injected_migration_generator_uses_capability_service()
    {
        using var serviceProvider = CreateCapabilityServiceProvider<NoSequenceCapabilities>();
        using var context = new CapabilityContext(
            FileOptionsWithCapabilities<CapabilityContext>(serviceProvider));
        var generator = Assert.IsType<DuckDBMigrationsSqlGenerator>(
            context.GetService<IMigrationsSqlGenerator>());

        var exception = Assert.Throws<NotSupportedException>(() => generator.Generate(
            [new CreateSequenceOperation { Name = "unsupported_sequence" }]));

        Assert.Contains("configured DuckDB engine", exception.Message);
        Assert.DoesNotContain("DuckLake", exception.Message);
    }

    [ConditionalFact]
    public void Model_validation_uses_capability_service()
    {
        using var serviceProvider = CreateCapabilityServiceProvider<NoSequenceCapabilities>();
        using var context = new AutoIncrementCapabilityContext(
            FileOptionsWithCapabilities<AutoIncrementCapabilityContext>(serviceProvider));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("does not support auto-increment", exception.Message);
        Assert.Contains("AutoIncrementCapabilityItem.Id", exception.Message);
        Assert.DoesNotContain("DuckLake", exception.Message);
    }

    [ConditionalFact]
    public void Tiered_storage_validation_uses_capability_service()
    {
        using var serviceProvider = CreateCapabilityServiceProvider<NoTieredStorageCapabilities>();
        using var context = new TieredCapabilityContext(
            FileOptionsWithCapabilities<TieredCapabilityContext>(serviceProvider));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("provider tiered storage", exception.Message);
        Assert.Contains("configured DuckDB engine capabilities", exception.Message);
        Assert.DoesNotContain("DuckLake", exception.Message);
    }

    [ConditionalFact]
    public void SaveChanges_batching_uses_capability_service()
    {
        using var serviceProvider = CreateCapabilityServiceProvider<NoBatchingCapabilities>();
        var options = new DbContextOptionsBuilder<CapabilityContext>(
                FileOptions<CapabilityContext>(duckDB => duckDB.EnableBulkInsertBatching()))
            .UseInternalServiceProvider(serviceProvider)
            .Options;
        using var context = new CapabilityContext(options);
        context.Database.EnsureCreated();
        context.CapabilityItems.Add(new CapabilityItem { Id = 1, Name = "unsupported" });

        var exception = Assert.Throws<NotSupportedException>(() => context.SaveChanges());

        Assert.Contains("configured DuckDB engine capabilities", exception.Message);
        Assert.DoesNotContain("DuckLake", exception.Message);
    }

    [ConditionalFact]
    public void SaveChanges_uses_returning_capability_independently_of_batching_capability()
    {
        using var serviceProvider = CreateCapabilityServiceProvider<NoBatchingCapabilities>();
        using var context = new DefaultKeyContext(
            FileOptionsWithCapabilities<DefaultKeyContext>(serviceProvider));
        context.Database.EnsureCreated();
        var item = new DefaultKeyItem();

        context.Add(item);

        Assert.Equal(1, context.SaveChanges());
        Assert.NotEqual(0, item.Id);
        Assert.Equal(item.Id, context.DefaultKeyItems.AsNoTracking().Single().Id);
    }

    [ConditionalFact]
    public void SaveChanges_batching_and_returning_capabilities_are_independent()
    {
        using var serviceProvider = CreateCapabilityServiceProvider<BatchingWithoutReturningCapabilities>();
        var options = new DbContextOptionsBuilder<CapabilityContext>(
                FileOptions<CapabilityContext>(duckDB => duckDB
                    .EnableBulkInsertBatching()
                    .EnableBulkUpdateBatching()
                    .EnableBulkDeleteBatching()))
            .UseInternalServiceProvider(serviceProvider)
            .Options;
        using var context = new CapabilityContext(options);
        context.Database.EnsureCreated();

        var batchFactory = context.GetService<IModificationCommandBatchFactory>();
        Assert.IsType<DuckDBModificationCommandBatch>(batchFactory.Create());

        context.AddRange(
            new CapabilityItem { Id = 1, Name = "first" },
            new CapabilityItem { Id = 2, Name = "second" });

        Assert.Equal(2, context.SaveChanges());
        Assert.Equal(2, context.CapabilityItems.Count());

        foreach (var item in context.CapabilityItems)
        {
            item.Name += "-updated";
        }

        Assert.Equal(2, context.SaveChanges());
        context.ChangeTracker.Clear();
        Assert.All(
            context.CapabilityItems.AsNoTracking(),
            item => Assert.EndsWith("-updated", item.Name));

        context.RemoveRange(context.CapabilityItems);

        Assert.Equal(2, context.SaveChanges());
        Assert.Empty(context.CapabilityItems);
    }

    [ConditionalFact]
    public void SaveChanges_batching_without_returning_rejects_store_generated_values()
    {
        using var serviceProvider = CreateCapabilityServiceProvider<BatchingWithoutReturningCapabilities>();
        var options = new DbContextOptionsBuilder<DefaultKeyContext>(
                FileOptions<DefaultKeyContext>(duckDB => duckDB.EnableBulkInsertBatching()))
            .UseInternalServiceProvider(serviceProvider)
            .Options;
        using var context = new DefaultKeyContext(options);
        context.Database.EnsureCreated();
        context.AddRange(new DefaultKeyItem(), new DefaultKeyItem());

        var exception = Assert.Throws<NotSupportedException>(() => context.SaveChanges());

        Assert.Contains("does not support INSERT/UPDATE RETURNING", exception.Message);
        Assert.Contains("store-generated", exception.Message);
    }

    [ConditionalFact]
    public void Non_returning_update_path_uses_capability_service()
    {
        using var serviceProvider = CreateCapabilityServiceProvider<NoReturningCapabilities>();
        using var context = new CapabilityContext(
            FileOptionsWithCapabilities<CapabilityContext>(serviceProvider));
        context.Database.EnsureCreated();
        var item = new CapabilityItem { Id = 1, Name = "first" };

        context.Add(item);
        Assert.Equal(1, context.SaveChanges());
        item.Name = "updated";
        Assert.Equal(1, context.SaveChanges());

        Assert.Equal("updated", context.CapabilityItems.AsNoTracking().Single().Name);
    }

    [ConditionalFact]
    public void Migration_capabilities_validate_sequence_renames_independently()
    {
        using var context = new CapabilityContext(FileOptions<CapabilityContext>());
        var generator = CreateMigrationsGenerator(
            context,
            new TestCapabilities(SupportsSequences: false));

        var exception = Assert.Throws<NotSupportedException>(() => generator.Generate(
            [new RenameSequenceOperation { Name = "old_sequence", NewName = "new_sequence" }]));

        Assert.Contains("does not support sequences", exception.Message);
    }

    [ConditionalFact]
    public void Migration_capabilities_validate_auto_increment_columns_independently()
    {
        using var context = new CapabilityContext(FileOptions<CapabilityContext>());
        var generator = CreateMigrationsGenerator(
            context,
            new TestCapabilities(SupportsSequences: false));
        var column = CreateAutoIncrementColumn();

        var exception = Assert.Throws<NotSupportedException>(() => generator.Generate([column]));

        Assert.Contains("does not support sequences", exception.Message);
    }

    [ConditionalFact]
    public void Migration_capabilities_validate_nested_auto_increment_columns_independently()
    {
        using var context = new CapabilityContext(FileOptions<CapabilityContext>());
        var generator = CreateMigrationsGenerator(
            context,
            new TestCapabilities(SupportsSequences: false));
        var table = new CreateTableOperation { Name = "Items", Columns = { CreateAutoIncrementColumn() } };

        var exception = Assert.Throws<NotSupportedException>(() => generator.Generate([table]));

        Assert.Contains("does not support sequences", exception.Message);
    }

    [ConditionalFact]
    public void Migration_capabilities_validate_generated_columns_independently()
    {
        using var context = new CapabilityContext(FileOptions<CapabilityContext>());
        var generator = CreateMigrationsGenerator(
            context,
            new TestCapabilities(SupportsGeneratedColumns: false));
        var column = new AddColumnOperation
        {
            Table = "Items",
            Name = "Computed",
            ClrType = typeof(int),
            IsNullable = false,
            ComputedColumnSql = "\"Id\" + 1"
        };

        var exception = Assert.Throws<NotSupportedException>(() => generator.Generate([column]));

        Assert.Contains("does not support generated columns", exception.Message);
        Assert.DoesNotContain("DuckLake", exception.Message);
    }

    [ConditionalFact]
    public void Migration_capabilities_validate_sql_defaults_independently()
    {
        using var context = new CapabilityContext(FileOptions<CapabilityContext>());
        var generator = CreateMigrationsGenerator(
            context,
            new TestCapabilities(SupportsSqlDefaultExpressions: false));
        var column = new AddColumnOperation
        {
            Table = "Items",
            Name = "CreatedAt",
            ClrType = typeof(DateTime),
            IsNullable = false,
            DefaultValueSql = "CURRENT_TIMESTAMP"
        };

        var exception = Assert.Throws<NotSupportedException>(() => generator.Generate([column]));

        Assert.Contains("does not support generated columns or SQL default expressions", exception.Message);
        Assert.DoesNotContain("DuckLake", exception.Message);
    }

    [ConditionalFact]
    public void Migration_capabilities_filter_indexes_without_stripping_supported_constraints()
    {
        using var context = new CapabilityContext(FileOptions<CapabilityContext>());
        var generator = CreateMigrationsGenerator(
            context,
            new TestCapabilities(SupportsIndexes: false));

        var sql = GenerateTableAndIndex(generator);

        Assert.Contains("CONSTRAINT \"PK_Items\" PRIMARY KEY (\"Id\")", sql);
        Assert.DoesNotContain("CREATE INDEX", sql);
    }

    [ConditionalFact]
    public void Migration_capabilities_filter_index_renames_independently()
    {
        using var context = new CapabilityContext(FileOptions<CapabilityContext>());
        var generator = CreateMigrationsGenerator(
            context,
            new TestCapabilities(SupportsIndexes: false));

        var commands = generator.Generate(
            [new RenameIndexOperation { Table = "Items", Name = "IX_Items_Old", NewName = "IX_Items_New" }]);

        Assert.Empty(commands);
    }

    [ConditionalFact]
    public void Migration_capabilities_filter_constraints_without_dropping_supported_indexes()
    {
        using var context = new CapabilityContext(FileOptions<CapabilityContext>());
        var generator = CreateMigrationsGenerator(
            context,
            new TestCapabilities(SupportsSchemaConstraints: false));

        var sql = GenerateTableAndIndex(generator);

        Assert.DoesNotContain("PRIMARY KEY", sql);
        Assert.Contains("CREATE INDEX \"IX_Items_Name\"", sql);
    }

    [ConditionalFact]
    public void Migration_capabilities_preserve_rebuilds_when_indexes_are_unsupported()
    {
        using var context = new CapabilityContext(
            FileOptions<CapabilityContext>(options => options.EnableMigrationTableRebuilds()));
        var generator = CreateMigrationsGenerator(
            context,
            new TestCapabilities(SupportsIndexes: false));
        var operation = new AddPrimaryKeyOperation
        {
            Table = "CapabilityItems",
            Name = "PK_CapabilityItems",
            Columns = ["Id"]
        };

        var sql = string.Join(
            Environment.NewLine,
            generator.Generate([operation], context.GetService<IDesignTimeModel>().Model)
                .Select(command => command.CommandText));

        Assert.Contains("__ef_rebuild_CapabilityItems", sql);
    }

    private static DuckDBMigrationsSqlGenerator CreateMigrationsGenerator(
        DbContext context,
        IDuckDBEngineCapabilities capabilities)
        => new(context.GetService<MigrationsSqlGeneratorDependencies>(), capabilities);

    private static string GenerateTableAndIndex(DuckDBMigrationsSqlGenerator generator)
    {
        var table = new CreateTableOperation
        {
            Name = "Items",
            Columns =
            {
                new AddColumnOperation
                {
                    Table = "Items",
                    Name = "Id",
                    ClrType = typeof(int),
                    IsNullable = false
                },
                new AddColumnOperation
                {
                    Table = "Items",
                    Name = "Name",
                    ClrType = typeof(string),
                    IsNullable = false
                }
            },
            PrimaryKey = new AddPrimaryKeyOperation
            {
                Table = "Items",
                Name = "PK_Items",
                Columns = ["Id"]
            }
        };
        var index = new CreateIndexOperation
        {
            Table = "Items",
            Name = "IX_Items_Name",
            Columns = ["Name"]
        };

        return string.Join(Environment.NewLine, generator.Generate([table, index]).Select(command => command.CommandText));
    }

    private static AddColumnOperation CreateAutoIncrementColumn()
    {
        var column = new AddColumnOperation
        {
            Table = "Items",
            Name = "Id",
            ClrType = typeof(int),
            IsNullable = false
        };
        column[DuckDBAnnotationNames.ValueGenerationStrategy] = DuckDBValueGenerationStrategy.AutoIncrement;

        return column;
    }

    private static void AssertCapabilities(
        IDuckDBEngineCapabilities capabilities,
        bool supported)
    {
        Assert.Equal(supported, capabilities.SupportsReturning);
        Assert.Equal(supported, capabilities.SupportsSaveChangesBatching);
        Assert.Equal(supported, capabilities.SupportsSequences);
        Assert.Equal(supported, capabilities.SupportsGeneratedColumns);
        Assert.Equal(supported, capabilities.SupportsSqlDefaultExpressions);
        Assert.Equal(supported, capabilities.SupportsIndexes);
        Assert.Equal(supported, capabilities.SupportsSchemaConstraints);
        Assert.Equal(supported, capabilities.SupportsTieredStorage);
        Assert.Equal(supported, capabilities.SupportsEfMigrations);
        Assert.Equal(
            supported ? DuckDBUpsertStrategy.InsertOnConflict : DuckDBUpsertStrategy.Merge,
            capabilities.UpsertStrategy);
    }

    private static ServiceProvider CreateCapabilityServiceProvider<TCapabilities>()
        where TCapabilities : class, IDuckDBEngineCapabilities
        => new ServiceCollection()
            .AddEntityFrameworkDuckDB()
            .AddSingleton<IDuckDBEngineCapabilities, TCapabilities>()
            .BuildServiceProvider(validateScopes: true);

    private DbContextOptions<TContext> FileOptionsWithCapabilities<TContext>(IServiceProvider serviceProvider)
        where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>(FileOptions<TContext>())
            .UseInternalServiceProvider(serviceProvider)
            .Options;

    private sealed class CapabilityContext(DbContextOptions<CapabilityContext> options) : DbContext(options)
    {
        public DbSet<CapabilityItem> CapabilityItems => Set<CapabilityItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<CapabilityItem>().Property(item => item.Id).ValueGeneratedNever();
    }

    private sealed class CapabilityItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class AutoIncrementCapabilityContext(
        DbContextOptions<AutoIncrementCapabilityContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<AutoIncrementCapabilityItem>()
                .Property(item => item.Id)
                .UseAutoIncrement();
    }

    private sealed class AutoIncrementCapabilityItem
    {
        public int Id { get; set; }
    }

    private sealed class TieredCapabilityContext(
        DbContextOptions<TieredCapabilityContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ToTieredStore<TieredCapabilityItem>(
                item => item.EffectiveAt,
                Path.Combine(Path.GetTempPath(), "capability-tiered-archive"));
    }

    private sealed class TieredCapabilityItem
    {
        public int Id { get; set; }
        public DateTime EffectiveAt { get; set; }
    }

    private sealed class DefaultKeyContext(DbContextOptions<DefaultKeyContext> options) : DbContext(options)
    {
        public DbSet<DefaultKeyItem> DefaultKeyItems => Set<DefaultKeyItem>();
    }

    private sealed class DefaultKeyItem
    {
        public int Id { get; set; }
    }

    private sealed class CapabilityUpsertContext(DbContextOptions<CapabilityUpsertContext> options) : DbContext(options)
    {
        public DbSet<CapabilityUpsertItem> CapabilityUpsertItems => Set<CapabilityUpsertItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CapabilityUpsertItem>(entity =>
            {
                entity.ToTable("CapabilityUpsertItems");
                entity.Property(item => item.Id).ValueGeneratedNever();
            });
        }
    }

    private sealed class CapabilityUpsertItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private abstract class NativeCapabilities : IDuckDBEngineCapabilities
    {
        public virtual bool SupportsReturning => true;
        public virtual bool SupportsSaveChangesBatching => true;
        public virtual bool SupportsSequences => true;
        public virtual bool SupportsGeneratedColumns => true;
        public virtual bool SupportsSqlDefaultExpressions => true;
        public virtual bool SupportsIndexes => true;
        public virtual bool SupportsSchemaConstraints => true;
        public virtual bool SupportsTieredStorage => true;
        public virtual bool SupportsEfMigrations => true;
        public virtual DuckDBUpsertStrategy UpsertStrategy => DuckDBUpsertStrategy.InsertOnConflict;
    }

    private sealed class NoSequenceCapabilities : NativeCapabilities
    {
        public override bool SupportsSequences => false;
    }

    private sealed class NoMigrationsCapabilities : NativeCapabilities
    {
        public override bool SupportsEfMigrations => false;
    }

    private sealed class NoBatchingCapabilities : NativeCapabilities
    {
        public override bool SupportsSaveChangesBatching => false;
    }

    private sealed class NoReturningCapabilities : NativeCapabilities
    {
        public override bool SupportsReturning => false;
        public override bool SupportsSaveChangesBatching => false;
    }

    private sealed class BatchingWithoutReturningCapabilities : NativeCapabilities
    {
        public override bool SupportsReturning => false;
    }

    private sealed class NoTieredStorageCapabilities : NativeCapabilities
    {
        public override bool SupportsTieredStorage => false;
    }

    private sealed class MergeUpsertCapabilities : NativeCapabilities
    {
        public override DuckDBUpsertStrategy UpsertStrategy => DuckDBUpsertStrategy.Merge;
    }

    private sealed record TestCapabilities(
        bool SupportsReturning = true,
        bool SupportsSaveChangesBatching = true,
        bool SupportsSequences = true,
        bool SupportsGeneratedColumns = true,
        bool SupportsSqlDefaultExpressions = true,
        bool SupportsIndexes = true,
        bool SupportsSchemaConstraints = true,
        bool SupportsTieredStorage = true,
        bool SupportsEfMigrations = true,
        DuckDBUpsertStrategy UpsertStrategy = DuckDBUpsertStrategy.InsertOnConflict) : IDuckDBEngineCapabilities;
}