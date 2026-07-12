using DuckDB.EFCoreProvider.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class ParquetExportTests : DuckDBTestBase
{
    [ConditionalFact]
    public async Task ExportToParquetAsync_preserves_query_parameters_and_writes_selected_rows()
    {
        var path = DbPath + ".parquet";
        try
        {
            await using var context = await CreateSeededContext();
            var minimum = 20;

            await context.Database.ExportToParquetAsync(
                context.Rows.Where(row => row.Value >= minimum),
                path,
                options => options.Compression(DuckDBParquetCompression.Zstd));

#pragma warning disable EF1003 // The provider-owned temporary path is SQL-literal escaped above.
            var values = await context.Database
                .SqlQueryRaw<int>("SELECT \"Value\" AS \"Value\" FROM read_parquet('" + Escape(path) + "') ORDER BY \"Value\"")
                .ToListAsync();
#pragma warning restore EF1003
            Assert.Equal([20, 30], values);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [ConditionalFact]
    public async Task ExportToParquetAsync_supports_typed_partition_columns()
    {
        var path = DbPath + "_partitioned";
        try
        {
            await using var context = await CreateSeededContext();

            await context.Database.ExportToParquetAsync(
                context.Rows,
                path,
                options => options.PartitionBy(row => row.Category).OverwriteOrIgnore());

            Assert.True(Directory.Exists(Path.Combine(path, "Category=A")));
            Assert.True(Directory.Exists(Path.Combine(path, "Category=B")));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [ConditionalFact]
    public async Task ExportToParquetAsync_rejects_query_from_a_different_context()
    {
        await using var databaseContext = await CreateSeededContext();
        await using var queryContext = new ExportContext(
            new DbContextOptionsBuilder<ExportContext>().UseDuckDB("DataSource=:memory:").Options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            databaseContext.Database.ExportToParquetAsync(queryContext.Rows, DbPath + ".parquet"));

        Assert.Contains("same DbContext", exception.Message);
    }

    private async Task<ExportContext> CreateSeededContext()
    {
        var context = new ExportContext(FileOptions<ExportContext>());
        await context.Database.EnsureCreatedAsync();
        context.Rows.AddRange(
            new ExportRow { Id = 1, Value = 10, Category = "A" },
            new ExportRow { Id = 2, Value = 20, Category = "A" },
            new ExportRow { Id = 3, Value = 30, Category = "B" });
        await context.SaveChangesAsync();
        return context;
    }

    private static string Escape(string path) => path.Replace("'", "''");

    private sealed class ExportContext(DbContextOptions<ExportContext> options) : DbContext(options)
    {
        public DbSet<ExportRow> Rows => Set<ExportRow>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ExportRow>().Property(row => row.Id).ValueGeneratedNever();
    }

    private sealed class ExportRow
    {
        public int Id { get; set; }
        public int Value { get; set; }
        public required string Category { get; set; }
    }
}