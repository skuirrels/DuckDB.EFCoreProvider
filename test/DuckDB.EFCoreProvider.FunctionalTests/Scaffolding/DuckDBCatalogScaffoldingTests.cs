using DuckDB.EFCoreProvider.Design.Internal;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Scaffolding;

public sealed class DuckDBCatalogScaffoldingTests
{
    [Fact]
    public void Reverse_engineering_is_scoped_to_the_selected_catalog_and_honors_schema_filters()
    {
        using var services = CreateDesignTimeServices();
        var factory = services.GetRequiredService<IDatabaseModelFactory>();
        using var connection = new DuckDBConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  ATTACH ':memory:' AS selected;
                                  ATTACH ':memory:' AS other;
                                  CREATE SCHEMA selected.extra;
                                  CREATE TABLE selected.main.a_no_key (id INTEGER, selected_value VARCHAR);
                                  CREATE TABLE selected.main.z_with_key (id INTEGER PRIMARY KEY, selected_value VARCHAR);
                                  CREATE TABLE selected.extra.extra_table (id INTEGER);
                                  CREATE TABLE other.main.other_table (id INTEGER PRIMARY KEY, other_value VARCHAR);
                                  USE selected;
                                  """;
            command.ExecuteNonQuery();
        }

        var allSchemas = factory.Create(
            connection,
            new DatabaseModelFactoryOptions([], []));
        var mainOnly = factory.Create(
            connection,
            new DatabaseModelFactoryOptions([], ["main"]));

        Assert.Equal("selected", allSchemas.DatabaseName);
        Assert.Equal(["a_no_key", "extra_table", "z_with_key"], allSchemas.Tables.Select(table => table.Name).Order());
        Assert.DoesNotContain(allSchemas.Tables, table => table.Name == "other_table");
        Assert.Null(allSchemas.Tables.Single(table => table.Name == "a_no_key").PrimaryKey);
        Assert.Equal("id", Assert.Single(allSchemas.Tables.Single(table => table.Name == "z_with_key").PrimaryKey!.Columns).Name);
        Assert.Equal(["a_no_key", "z_with_key"], mainOnly.Tables.Select(table => table.Name).Order());
    }

    [ConditionalFact]
    public async Task DuckLake_reverse_engineering_returns_only_user_tables_as_keyless_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake_scaffolding_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var metadataPath = Path.Combine(root, "metadata.ducklake");
        var dataPath = Path.Combine(root, "data");
        Directory.CreateDirectory(dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<ScaffoldingContext>()
                .UseDuckLake(
                    metadataPath,
                    duckLake => duckLake.CatalogName("analytics").DataPath(dataPath))
                .Options;
            await using var context = new ScaffoldingContext(options);
            await context.Database.EnsureCreatedAsync();
            await context.Database.OpenConnectionAsync();

            using var services = CreateDesignTimeServices();
            var model = services.GetRequiredService<IDatabaseModelFactory>().Create(
                context.Database.GetDbConnection(),
                new DatabaseModelFactoryOptions([], []));

            Assert.Equal("analytics", model.DatabaseName);
            var table = Assert.Single(model.Tables);
            Assert.Equal("items", table.Name);
            Assert.Equal("main", table.Schema);
            Assert.Null(table.PrimaryKey);
            Assert.Equal(["Id", "Value"], table.Columns.Select(column => column.Name).Order());

            await context.Database.CloseConnectionAsync();
            var commandLineModel = services.GetRequiredService<IDatabaseModelFactory>().Create(
                $"ducklake:{metadataPath}",
                new DatabaseModelFactoryOptions([], []));
            Assert.Equal("ducklake", commandLineModel.DatabaseName);
            Assert.Equal("items", Assert.Single(commandLineModel.Tables).Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("postgres:host=localhost;database=metadata;password=not-a-real-secret")]
    [InlineData("https://metadata.example/catalog")]
    public void DuckLake_command_line_scaffolding_rejects_non_local_metadata_sources(string metadataSource)
    {
        using var services = CreateDesignTimeServices();
        var factory = services.GetRequiredService<IDatabaseModelFactory>();

        var exception = Assert.Throws<ArgumentException>(
            () => factory.Create(
                $"ducklake:{metadataSource}",
                new DatabaseModelFactoryOptions([], [])));

        Assert.Equal("metadataPath", exception.ParamName);
        Assert.Contains("must be file paths", exception.Message);
    }

    private static ServiceProvider CreateDesignTimeServices()
    {
        var services = new ServiceCollection();
        new DuckDBDesignTimeServices().ConfigureDesignTimeServices(services);
        return services.BuildServiceProvider();
    }

    private sealed class ScaffoldingContext(DbContextOptions<ScaffoldingContext> options) : DbContext(options)
    {
        public DbSet<ScaffoldingItem> Items => Set<ScaffoldingItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ScaffoldingItem>(entity =>
            {
                entity.ToTable("items");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedNever();
            });
    }

    private sealed class ScaffoldingItem
    {
        public int Id { get; set; }
        public required string Value { get; set; }
    }
}