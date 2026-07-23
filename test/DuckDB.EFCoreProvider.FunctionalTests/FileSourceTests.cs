using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Linq.Expressions;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore;

public class FileSourceTests : IClassFixture<FileSourceTests.FileSourceFixture>
{
    public FileSourceTests(FileSourceFixture fixture, ITestOutputHelper testOutputHelper)
    {
        Fixture = fixture;
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    private FileSourceFixture Fixture { get; }

    [Fact]
    public void Csv_query_uses_read_csv_and_returns_rows()
    {
        using var context = CreateContext();

        var ids = context.CsvRows.OrderBy(x => x.Id).Select(x => x.Id).ToList();

        Assert.Equal(new[] { 1, 2, 3 }, ids);
        Assert.Contains("read_csv('csv/data.csv')", Fixture.TestSqlLoggerFactory.Sql);
    }

    [Fact]
    public void Json_query_uses_read_json_and_returns_rows()
    {
        using var context = CreateContext();

        var ids = context.JsonRows.OrderBy(x => x.Id).Select(x => x.Id).ToList();

        Assert.Equal(new[] { 10, 20, 30 }, ids);
        Assert.Contains("read_json('json/data.json')", Fixture.TestSqlLoggerFactory.Sql);
    }

    [Fact]
    public void Csv_where_filters_through_read_csv()
    {
        using var context = CreateContext();

        var ids = context.CsvRows.Where(x => x.Id >= 2).OrderBy(x => x.Id).Select(x => x.Id).ToList();

        Assert.Equal(new[] { 2, 3 }, ids);
        Assert.Contains("read_csv('csv/data.csv')", Fixture.TestSqlLoggerFactory.Sql);
    }

    [Fact]
    public void File_path_is_rendered_as_a_mapped_sql_literal()
    {
        using var context = CreateContext();

        var ids = context.QuotedPathRows.Select(x => x.Id).ToList();

        Assert.Equal([4], ids);
        Assert.Contains("read_csv('csv/data''quoted.csv')", Fixture.TestSqlLoggerFactory.Sql);
    }

    [Fact]
    public void Custom_file_source_function_is_safely_delimited()
    {
        using var context = CreateContext();

        var ids = context.CustomCsvRows.OrderBy(x => x.Id).Select(x => x.Id).ToList();

        Assert.Equal([1, 2, 3], ids);
        Assert.Contains("\"read_csv_auto\"('csv/data.csv')", Fixture.TestSqlLoggerFactory.Sql);
    }

    [Fact]
    public void Precompiled_query_quote_preserves_custom_schema_qualified_function_metadata()
    {
        var function = DuckDBFileSourceFunction.Parse("analytics.read_csv_auto");
        var path = new SqlConstantExpression("csv/data.csv", typeof(string), typeMapping: null);
        var expression = new DuckDBFileSourceExpression("f", function, path);

        var quoted = expression.Quote();
        var recreated = Expression.Lambda<Func<DuckDBFileSourceExpression>>(quoted).Compile()();

        Assert.Equal(function, recreated.Function);
        Assert.Equal("analytics", recreated.Schema);
        Assert.Equal("read_csv_auto", recreated.Name);
        Assert.False(recreated.IsBuiltIn);
        Assert.Equal("csv/data.csv", Assert.IsType<SqlConstantExpression>(recreated.Path).Value);
    }

    [Fact]
    public void Model_validation_rejects_mixed_file_and_ordinary_shared_table_mappings()
    {
        using var context = new MixedFileSourceContext(
            new DbContextOptionsBuilder<MixedFileSourceContext>()
                .UseDuckDB("DataSource=:memory:")
                .Options);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("mixes the file-backed entity mapping", exception.Message);
    }

    [Fact]
    public void Model_validation_rejects_conflicting_shared_table_file_sources()
    {
        using var context = new ConflictingFileSourceContext(
            new DbContextOptionsBuilder<ConflictingFileSourceContext>()
                .UseDuckDB("DataSource=:memory:")
                .Options);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);

        Assert.Contains("conflicting DuckDB file sources", exception.Message);
    }

    [Fact]
    public void Model_validation_allows_identical_shared_table_file_sources()
    {
        using var context = new IdenticalFileSourceContext(
            new DbContextOptionsBuilder<IdenticalFileSourceContext>()
                .UseDuckDB("DataSource=:memory:")
                .Options);

        Assert.NotNull(context.Model.FindEntityType(typeof(IdenticalFileSourceDerived)));
    }

    private FileSourceContext CreateContext()
        => new(new DbContextOptionsBuilder<FileSourceContext>()
            .UseInternalServiceProvider(Fixture.ServiceProvider)
            .UseDuckDB("DataSource=:memory:")
            .Options);

    private sealed class FileSourceContext(DbContextOptions<FileSourceContext> options) : DbContext(options)
    {
        public DbSet<CsvRow> CsvRows => Set<CsvRow>();
        public DbSet<CustomCsvRow> CustomCsvRows => Set<CustomCsvRow>();
        public DbSet<JsonRow> JsonRows => Set<JsonRow>();
        public DbSet<QuotedPathRow> QuotedPathRows => Set<QuotedPathRow>();
    }

    private sealed class MixedFileSourceContext(DbContextOptions<MixedFileSourceContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MixedFileSourceBase>();
            modelBuilder.Entity<MixedFileSourceDerived>()
                .HasBaseType<MixedFileSourceBase>()
                .FromCsv("csv/data.csv");
        }
    }

    private sealed class ConflictingFileSourceContext(DbContextOptions<ConflictingFileSourceContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ConflictingFileSourceBase>().FromCsv("csv/data.csv");
            modelBuilder.Entity<ConflictingFileSourceDerived>().FromJsonFile("json/data.json");
        }
    }

    private sealed class IdenticalFileSourceContext(DbContextOptions<IdenticalFileSourceContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<IdenticalFileSourceBase>().FromCsv("csv/data.csv");
            modelBuilder.Entity<IdenticalFileSourceDerived>().FromCsv("csv/data.csv");
        }
    }

    [FromCsv("csv/data.csv")]
    private sealed class CsvRow
    {
        public int Id { get; set; }
    }

    [CustomFileSource("read_csv_auto", "csv/data.csv")]
    private sealed class CustomCsvRow
    {
        public int Id { get; set; }
    }

    [FromJsonFile("json/data.json")]
    private sealed class JsonRow
    {
        public int Id { get; set; }
    }

    [FromCsv("csv/data'quoted.csv")]
    private sealed class QuotedPathRow
    {
        public int Id { get; set; }
    }

    private class MixedFileSourceBase
    {
        public int Id { get; set; }
    }

    private sealed class MixedFileSourceDerived : MixedFileSourceBase;

    private class ConflictingFileSourceBase
    {
        public int Id { get; set; }
    }

    private sealed class ConflictingFileSourceDerived : ConflictingFileSourceBase;

    private class IdenticalFileSourceBase
    {
        public int Id { get; set; }
    }

    private sealed class IdenticalFileSourceDerived : IdenticalFileSourceBase;

    private sealed class CustomFileSourceAttribute(string function, string path) : DuckDBFileSourceAttribute(function, path);

    public sealed class FileSourceFixture : ServiceProviderFixtureBase, ITestSqlLoggerFactory, IDisposable
    {
        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        public FileSourceFixture()
        {
            Write("csv/data.csv", "Id\n1\n2\n3\n");
            Write("json/data.json", "[{\"Id\":10},{\"Id\":20},{\"Id\":30}]");
            Write("csv/data'quoted.csv", "Id\n4\n");
        }

        public void Dispose()
        {
            foreach (var dir in new[] { "csv", "json" })
            {
                var full = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, dir));
                if (Directory.Exists(full))
                {
                    Directory.Delete(full, recursive: true);
                }
            }
        }

        private static void Write(string relativePath, string content)
        {
            var full = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
    }
}