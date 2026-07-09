using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore;

public class ParquetTests : IClassFixture<ParquetTests.ParquetFixture>
{
    public ParquetTests(ParquetFixture fixture, ITestOutputHelper testOutputHelper)
    {
        Fixture = fixture;
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    private ParquetFixture Fixture { get; }

    [Fact]
    public void Simple_query_uses_read_parquet()
    {
        using var context = CreateContext();
        var _ = context.MyData.ToList();

        AssertSql(
            """
            SELECT m."Id"
            FROM read_parquet('parquet/my_data.parquet') AS m
            """);
    }

    [Fact]
    public void Where_query_uses_read_parquet()
    {
        using var context = CreateContext();
        var _ = context.MyData.Where(x => x.Id > 10).ToList();

        AssertSql(
            """
            SELECT m."Id"
            FROM read_parquet('parquet/my_data.parquet') AS m
            WHERE m."Id" > 10
            """);
    }

    [Fact]
    public void Join_query_uses_read_parquet()
    {
        using var context = CreateContext(Fixture.JoinDatabaseConnectionString);
        var query =
            from parquetRow in context.MyData
            join otherRow in context.Others on parquetRow.Id equals otherRow.Id
            select parquetRow;

        var _ = query.ToList();

        AssertSql(
            """
            SELECT m."Id"
            FROM read_parquet('parquet/my_data.parquet') AS m
            INNER JOIN "Others" AS o ON m."Id" = o."Id"
            """);
    }

    [Fact]
    public void Relationship_join_between_two_parquet_sets_uses_read_parquet_for_both()
    {
        using var context = CreateContext();
        var relationshipQuery = context.MyData.SelectMany(
            m => m.Related,
            (m, r) => new { m.Id, r.Value }
        );

        var _ = relationshipQuery.ToList();

        AssertSql(
            """
            SELECT m."Id", r."Value"
            FROM read_parquet('parquet/my_data.parquet') AS m
            INNER JOIN read_parquet('parquet/related.parquet') AS r ON m."Id" = r."MyDataId"
            """);
    }

    [Fact]
    public void Include_eager_load_across_two_parquet_sets_uses_read_parquet_for_both()
    {
        using var context = CreateContext();
        var roots = context.MyData
            .Where(m => m.Id <= 2)
            .Include(m => m.Related)
            .OrderBy(m => m.Id)
            .ToList();

        Assert.Equal(new[] { 1, 2 }, roots.Select(m => m.Id));
        Assert.Equal(new[] { 100, 200 }, roots[0].Related.OrderBy(r => r.Value).Select(r => r.Value));
        Assert.Equal(new[] { 300 }, roots[1].Related.Select(r => r.Value));

        AssertSql(
            """
            SELECT m."Id", r."Id", r."MyDataId", r."Value"
            FROM read_parquet('parquet/my_data.parquet') AS m
            LEFT JOIN read_parquet('parquet/related.parquet') AS r ON m."Id" = r."MyDataId"
            WHERE m."Id" <= 2
            ORDER BY m."Id"
            """);
    }

    [Fact]
    public void Dynamic_parquet_path_from_context_configuration_uses_read_parquet()
    {
        using var context = CreateDynamicContext(ParquetFixture.MyDataParquetFile);
        var _ = context.DynamicMyData.ToList();

        AssertSql(
            """
            SELECT d."Id"
            FROM read_parquet('parquet/my_data.parquet') AS d
            """);
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

    private ParquetContext CreateContext(string? connectionString = null)
    {
        var options = new DbContextOptionsBuilder<ParquetContext>()
            .UseInternalServiceProvider(Fixture.ServiceProvider)
            .UseDuckDB(connectionString ?? "DataSource=:memory:")
            .Options;
        return new ParquetContext(options);
    }

    private DynamicParquetContext CreateDynamicContext(string parquetPath)
    {
        var options = new DbContextOptionsBuilder<DynamicParquetContext>()
            .UseInternalServiceProvider(Fixture.ServiceProvider)
            .UseDuckDB("DataSource=:memory:")
            .Options;
        return new DynamicParquetContext(options, parquetPath);
    }

    private sealed class ParquetContext : DbContext
    {
        public ParquetContext(DbContextOptions<ParquetContext> options)
            : base(options) { }

        public DbSet<MyData> MyData => Set<MyData>();
        public DbSet<OtherData> Others => Set<OtherData>();
        public DbSet<RelatedParquetData> RelatedParquetData => Set<RelatedParquetData>();
    }

    [FromParquet("parquet/my_data.parquet")]
    private sealed class MyData
    {
        public int Id { get; set; }
        public List<RelatedParquetData> Related { get; set; } = [];
    }

    private sealed class OtherData
    {
        public int Id { get; set; }
    }

    private sealed class DynamicMyData
    {
        public int Id { get; set; }
    }

    [FromParquet("parquet/related.parquet")]
    private sealed class RelatedParquetData
    {
        public int Id { get; set; }
        public int MyDataId { get; set; }
        public int Value { get; set; }
        public MyData? MyData { get; set; }
    }

    private sealed class DynamicParquetContext : DbContext
    {
        private readonly string _parquetPath;

        public DynamicParquetContext(
            DbContextOptions<DynamicParquetContext> options,
            string parquetPath
        )
            : base(options)
        {
            _parquetPath = parquetPath;
        }

        public DbSet<DynamicMyData> DynamicMyData => Set<DynamicMyData>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DynamicMyData>().FromParquet(_parquetPath);
        }
    }

    public sealed class ParquetFixture
        : ServiceProviderFixtureBase, ITestSqlLoggerFactory, IDisposable
    {
        private const string ParquetDirectory = "parquet";
        public const string MyDataParquetFile = ParquetDirectory + "/my_data.parquet";
        private const string RelatedParquetFile = ParquetDirectory + "/related.parquet";
        private const string JoinDatabaseFile = ParquetDirectory + "/join.db";

        public string JoinDatabaseConnectionString { get; }

        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;

        public ParquetFixture()
        {
            WriteParquet(MyDataParquetFile, "DataSource=:memory:", conn =>
            {
                Execute(conn, "CREATE TABLE t (\"Id\" INTEGER)");
                using var a = conn.CreateAppender("t");
                foreach (var id in new[] { 1, 2, 3, 15, 20 })
                    a.CreateRow().AppendValue(id).EndRow();
            });

            WriteParquet(RelatedParquetFile, "DataSource=:memory:", conn =>
            {
                Execute(conn, "CREATE TABLE t (\"Id\" INTEGER, \"MyDataId\" INTEGER, \"Value\" INTEGER)");
                using var a = conn.CreateAppender("t");
                foreach (var (id, myDataId, value) in new[] { (1, 1, 100), (2, 1, 200), (3, 2, 300) })
                    a.CreateRow().AppendValue(id).AppendValue(myDataId).AppendValue(value).EndRow();
            });

            var dbPath = FullPath(JoinDatabaseFile);
            using var joinConn = new DuckDBConnection($"DataSource={dbPath}");
            joinConn.Open();
            Execute(joinConn, "CREATE TABLE \"Others\" (\"Id\" INTEGER)");
            using (var a = joinConn.CreateAppender("Others"))
            {
                a.CreateRow().AppendValue(1).EndRow();
                a.CreateRow().AppendValue(2).EndRow();
            }
            JoinDatabaseConnectionString = $"DataSource={dbPath}";
        }

        public void Dispose()
        {
            var dir = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ParquetDirectory));
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }

        private static void WriteParquet(string relativePath, string connectionString, Action<DuckDBConnection> seed)
        {
            var fullPath = FullPath(relativePath);
            using var conn = new DuckDBConnection(connectionString);
            conn.Open();
            seed(conn);
            Execute(conn, $"COPY t TO '{fullPath.Replace("\\", "\\\\").Replace("'", "''")}' (FORMAT PARQUET)");
        }

        private static void Execute(DuckDBConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static string FullPath(string relativePath)
        {
            var fullPath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            return fullPath;
        }
    }
}

