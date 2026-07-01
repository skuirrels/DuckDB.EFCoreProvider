using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.TestUtilities;
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

    private FileSourceContext CreateContext()
        => new(new DbContextOptionsBuilder<FileSourceContext>()
            .UseInternalServiceProvider(Fixture.ServiceProvider)
            .UseDuckDB("DataSource=:memory:")
            .Options);

    private sealed class FileSourceContext(DbContextOptions<FileSourceContext> options) : DbContext(options)
    {
        public DbSet<CsvRow> CsvRows => Set<CsvRow>();
        public DbSet<JsonRow> JsonRows => Set<JsonRow>();
    }

    [FromCsv("csv/data.csv")]
    private sealed class CsvRow
    {
        public int Id { get; set; }
    }

    [FromJsonFile("json/data.json")]
    private sealed class JsonRow
    {
        public int Id { get; set; }
    }

    public sealed class FileSourceFixture : ServiceProviderFixtureBase, ITestSqlLoggerFactory, IDisposable
    {
        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        public FileSourceFixture()
        {
            Write("csv/data.csv", "Id\n1\n2\n3\n");
            Write("json/data.json", "[{\"Id\":10},{\"Id\":20},{\"Id\":30}]");
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
