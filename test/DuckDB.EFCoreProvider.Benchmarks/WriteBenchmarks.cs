using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>
///     Compares the cost of inserting rows through <see cref="DbContext.SaveChanges()" /> (per-statement
///     <c>INSERT … RETURNING</c>) versus the Appender-backed <c>BulkInsert</c> fast path.
/// </summary>
[MemoryDiagnoser]
public class WriteBenchmarks
{
    [Params(10_000, 100_000)]
    public int RowCount;

    private List<BenchRow> _rows = [];
    private string _dbPath = "";

    [GlobalSetup]
    public void GlobalSetup()
        => _rows = Enumerable.Range(1, RowCount)
            .Select(i => new BenchRow { Id = i, Name = $"row-{i}", Value = i * 1.5, Active = i % 2 == 0 })
            .ToList();

    [IterationSetup]
    public void IterationSetup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bench_{Guid.NewGuid():N}.db");
        using var context = new BenchContext(_dbPath);
        context.Database.EnsureCreated();
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Benchmark(Baseline = true)]
    public void SaveChanges()
    {
        using var context = new BenchContext(_dbPath);
        context.AddRange(_rows);
        context.SaveChanges();
    }

    [Benchmark]
    public int BulkInsert()
    {
        using var context = new BenchContext(_dbPath);
        return context.BulkInsert(_rows);
    }
}
