using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>
///     Isolates the per-command LINQ allocations in the modification-command batch-merge decision
///     (<c>DuckDBModificationCommandBatch</c>'s <c>CanBulkInsert</c> / <c>CanBeInsertedInSameStatement</c>).
///     A narrow row and a large row count keep EF's per-row baseline small so the fixed per-command
///     iterator/delegate allocations dominate the measured delta.
/// </summary>
[MemoryDiagnoser]
public class HotPathReviewBenchmarks
{
    private const int InsertRowCount = 4_000;

    private List<ReviewRow> _rows = [];
    private string _insertDbPath = "";

    [GlobalSetup]
    public void GlobalSetup()
        => _rows = Enumerable.Range(1, InsertRowCount)
            .Select(i => new ReviewRow { Id = i, Name = "row-" + i, Value = i * 1.5 })
            .ToList();

    [IterationSetup(Target = nameof(MergeDecisionInsertBatching))]
    public void SetupInsert()
    {
        _insertDbPath = NewDbPath("review_insert");
        using var context = new ReviewContext(_insertDbPath);
        context.Database.EnsureCreated();
    }

    [IterationCleanup(Target = nameof(MergeDecisionInsertBatching))]
    public void CleanupInsert()
        => DeleteDb(_insertDbPath);

    /// <summary>
    ///     Exercises the batch-merge decision for a run of <see cref="InsertRowCount" /> same-shape inserts.
    ///     Each buffered command runs <c>CanBulkInsert</c> and <c>CanBeInsertedInSameStatement</c>.
    /// </summary>
    [Benchmark]
    public int MergeDecisionInsertBatching()
    {
        using var context = new ReviewContext(_insertDbPath);
        context.AddRange(_rows);
        return context.SaveChanges();
    }

    private static string NewDbPath(string prefix)
        => Path.Combine(Path.GetTempPath(), prefix + "_" + Guid.NewGuid().ToString("N") + ".db");

    private static void DeleteDb(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*"))
        {
            File.Delete(file);
        }
    }

    private sealed class ReviewContext(string dbPath) : DbContext
    {
        public DbSet<ReviewRow> Rows => Set<ReviewRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB("DataSource=" + dbPath, duckdb => duckdb.EnableBulkInsertBatching());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ReviewRow>().Property(e => e.Id).ValueGeneratedNever();
    }

    private sealed class ReviewRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public double Value { get; set; }
    }
}
