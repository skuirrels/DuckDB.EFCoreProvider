using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>Measures the configured-connection initialization path affected by command coalescing.</summary>
[MemoryDiagnoser]
public class ConnectionInitializationBenchmarks
{
    private string _defaultDbPath = "";
    private string _configuredDbPath = "";

    [GlobalSetup]
    public void Setup()
    {
        _defaultDbPath = BenchmarkFiles.NewDbPath("connection_default");
        _configuredDbPath = BenchmarkFiles.NewDbPath("connection_configured");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BenchmarkFiles.DeleteDb(_defaultDbPath);
        BenchmarkFiles.DeleteDb(_configuredDbPath);
    }

    [Benchmark(Baseline = true)]
    public void OpenDefaultConnection()
    {
        using var context = new InitializationContext(_defaultDbPath, configured: false);
        context.Database.OpenConnection();
        context.Database.CloseConnection();
    }

    [Benchmark]
    public void OpenConnectionWithThreeSettings()
    {
        using var context = new InitializationContext(_configuredDbPath, configured: true);
        context.Database.OpenConnection();
        context.Database.CloseConnection();
    }

    private sealed class InitializationContext(string dbPath, bool configured) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB(
                "DataSource=" + dbPath,
                duckdb =>
                {
                    if (configured)
                    {
                        duckdb.MemoryLimit("512MB").Threads(2).FileSearchPath(Path.GetTempPath());
                    }
                });
    }
}

/// <summary>Measures Upsert chunk sizes while reusing one staging table per operation.</summary>
[MemoryDiagnoser]
public class UpsertBatchSizeBenchmarks
{
    private const int RowCount = 1_000;
    private List<UpsertRow> _rows = [];
    private string _dbPath = "";

    [Params(25, 100, 500, 1_000)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
        => _rows = Enumerable.Range(1, RowCount)
            .Select(i => new UpsertRow { Id = i, Name = "upsert-" + i, Quantity = i })
            .ToList();

    [IterationSetup]
    public void IterationSetup()
    {
        _dbPath = BenchmarkFiles.NewDbPath("upsert_batch_size");
        using var context = new UpsertContext(_dbPath);
        context.Database.EnsureCreated();
        context.BulkInsert(_rows.Take(RowCount / 2));
    }

    [IterationCleanup]
    public void IterationCleanup()
        => BenchmarkFiles.DeleteDb(_dbPath);

    [Benchmark]
    public int Upsert()
    {
        using var context = new UpsertContext(_dbPath);
        return context.Upsert(_rows, BatchSize);
    }

    private sealed class UpsertContext(string dbPath) : DbContext
    {
        public DbSet<UpsertRow> Rows => Set<UpsertRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB("DataSource=" + dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<UpsertRow>().Property(row => row.Id).ValueGeneratedNever();
    }

    private sealed class UpsertRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
    }
}

/// <summary>Guards adaptive SaveChanges batching across narrow and wide entity shapes.</summary>
[MemoryDiagnoser]
public class SaveChangesWidthBenchmarks
{
    private const int RowCount = 700;
    private List<NarrowRow> _narrowRows = [];
    private List<WideRow> _wideRows = [];
    private string _narrowDbPath = "";
    private string _wideDbPath = "";

    [GlobalSetup]
    public void GlobalSetup()
    {
        _narrowRows = Enumerable.Range(1, RowCount)
            .Select(i => new NarrowRow { Id = i, Name = "row-" + i, Quantity = i, Active = (i & 1) == 0 })
            .ToList();
        _wideRows = Enumerable.Range(1, RowCount)
            .Select(i => new WideRow
            {
                Id = i,
                C01 = i,
                C02 = i,
                C03 = i,
                C04 = i,
                C05 = i,
                C06 = i,
                C07 = i,
                C08 = i,
                C09 = i,
                C10 = i,
                C11 = i,
                C12 = i,
                C13 = i,
                C14 = i,
                C15 = i,
            })
            .ToList();
    }

    [IterationSetup(Target = nameof(NarrowRows))]
    public void SetupNarrow()
    {
        _narrowDbPath = BenchmarkFiles.NewDbPath("savechanges_narrow");
        using var context = new WidthContext(_narrowDbPath);
        context.Database.EnsureCreated();
    }

    [IterationCleanup(Target = nameof(NarrowRows))]
    public void CleanupNarrow()
        => BenchmarkFiles.DeleteDb(_narrowDbPath);

    [IterationSetup(Target = nameof(WideRows))]
    public void SetupWide()
    {
        _wideDbPath = BenchmarkFiles.NewDbPath("savechanges_wide");
        using var context = new WidthContext(_wideDbPath);
        context.Database.EnsureCreated();
    }

    [IterationCleanup(Target = nameof(WideRows))]
    public void CleanupWide()
        => BenchmarkFiles.DeleteDb(_wideDbPath);

    [Benchmark(Baseline = true)]
    public int NarrowRows()
    {
        using var context = new WidthContext(_narrowDbPath);
        context.AddRange(_narrowRows);
        return context.SaveChanges();
    }

    [Benchmark]
    public int WideRows()
    {
        using var context = new WidthContext(_wideDbPath);
        context.AddRange(_wideRows);
        return context.SaveChanges();
    }

    private sealed class WidthContext(string dbPath) : DbContext
    {
        public DbSet<NarrowRow> Narrow => Set<NarrowRow>();
        public DbSet<WideRow> Wide => Set<WideRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB(
                "DataSource=" + dbPath,
                duckdb => duckdb.EnableBulkInsertBatching().MaxBatchSize(1_000));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NarrowRow>().Property(row => row.Id).ValueGeneratedNever();
            modelBuilder.Entity<WideRow>().Property(row => row.Id).ValueGeneratedNever();
        }
    }

    private sealed class NarrowRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public bool Active { get; set; }
    }

    private sealed class WideRow
    {
        public int Id { get; set; }
        public int C01 { get; set; }
        public int C02 { get; set; }
        public int C03 { get; set; }
        public int C04 { get; set; }
        public int C05 { get; set; }
        public int C06 { get; set; }
        public int C07 { get; set; }
        public int C08 { get; set; }
        public int C09 { get; set; }
        public int C10 { get; set; }
        public int C11 { get; set; }
        public int C12 { get; set; }
        public int C13 { get; set; }
        public int C14 { get; set; }
        public int C15 { get; set; }
    }
}

internal static class BenchmarkFiles
{
    internal static string NewDbPath(string prefix)
        => Path.Combine(Path.GetTempPath(), prefix + "_" + Guid.NewGuid().ToString("N") + ".db");

    internal static void DeleteDb(string path)
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
}