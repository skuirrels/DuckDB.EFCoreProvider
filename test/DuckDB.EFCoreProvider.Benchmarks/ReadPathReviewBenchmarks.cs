using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>
///     Isolates the per-row cost of materializing BLOB columns through the provider.
///     <para>
///         DuckDB.NET's <c>GetStream</c> returns a seekable <see cref="System.IO.UnmanagedMemoryStream" />
///         over the blob's native memory with a known <c>Length</c>. The provider's
///         <c>DuckDBBlobTypeMapping.ReadStream</c> materializes that into a <c>byte[]</c> per row, so any
///         intermediate copies (e.g. a growing <see cref="System.IO.MemoryStream" /> plus a final
///         <c>ToArray()</c>) multiply the bytes allocated per blob. With 2,000 rows x 4 KB the payload is
///         ~7.8 MB; the benchmark's Allocated column shows how many times over that payload is being paid.
///     </para>
/// </summary>
[MemoryDiagnoser]
public class ReadPathReviewBenchmarks
{
    private const int RowCount = 2_000;
    private const int BlobBytes = 4_096;

    private string _dbPath = "";

    [GlobalSetup]
    public void GlobalSetup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"review_blob_{Guid.NewGuid():N}.db");
        using var context = new BlobContext(_dbPath);
        context.Database.EnsureCreated();
        context.BulkInsert(Enumerable.Range(1, RowCount)
            .Select(i =>
            {
                var data = new byte[BlobBytes];
                Random.Shared.NextBytes(data);
                return new BlobRow { Id = i, Data = data };
            }));
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            File.Delete(file);
        }
    }

    [Benchmark]
    public long ReadBlobs_NoTracking()
    {
        using var context = new BlobContext(_dbPath);
        long total = 0;
        foreach (var row in context.Rows.AsNoTracking())
        {
            total += row.Data.Length;
        }

        return total;
    }

    private sealed class BlobContext(string dbPath) : DbContext
    {
        public DbSet<BlobRow> Rows => Set<BlobRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB("DataSource=" + dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<BlobRow>().Property(e => e.Id).ValueGeneratedNever();
    }

    private sealed class BlobRow
    {
        public int Id { get; set; }
        public byte[] Data { get; set; } = [];
    }
}
