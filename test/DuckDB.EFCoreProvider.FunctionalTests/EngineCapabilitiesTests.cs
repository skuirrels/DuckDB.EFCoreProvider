using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
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
    }

    private sealed class CapabilityContext(DbContextOptions<CapabilityContext> options) : DbContext(options)
    {
        public DbSet<CapabilityItem> CapabilityItems => Set<CapabilityItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<CapabilityItem>().Property(item => item.Id).ValueGeneratedNever();
    }

    private sealed class CapabilityItem
    {
        public int Id { get; set; }
    }

    private sealed record TestCapabilities(
        bool SupportsReturning = true,
        bool SupportsSaveChangesBatching = true,
        bool SupportsSequences = true,
        bool SupportsGeneratedColumns = true,
        bool SupportsSqlDefaultExpressions = true,
        bool SupportsIndexes = true,
        bool SupportsSchemaConstraints = true,
        bool SupportsTieredStorage = true) : IDuckDBEngineCapabilities;
}