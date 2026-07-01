using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>
///     Measures read/materialization throughput for a no-tracking query over a populated table.
/// </summary>
[MemoryDiagnoser]
public class ReadBenchmarks
{
    [Params(10_000, 100_000)]
    public int RowCount;

    private string _dbPath = "";

    [GlobalSetup]
    public void GlobalSetup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bench_read_{Guid.NewGuid():N}.db");
        using var context = new BenchContext(_dbPath);
        context.Database.EnsureCreated();
        context.BulkInsert(Enumerable.Range(1, RowCount)
            .Select(i => new BenchRow { Id = i, Name = $"row-{i}", Value = i * 1.5, Active = i % 2 == 0 }));
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Benchmark]
    public int ReadAll_NoTracking()
    {
        using var context = new BenchContext(_dbPath);
        return context.Rows.AsNoTracking().ToList().Count;
    }

    [Benchmark]
    public int Where_NoTracking()
    {
        using var context = new BenchContext(_dbPath);
        return context.Rows.AsNoTracking().Where(r => r.Active && r.Value > 100).ToList().Count;
    }
}
