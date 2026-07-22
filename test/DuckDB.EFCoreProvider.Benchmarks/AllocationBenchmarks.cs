using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>
///     Focused allocation benchmarks for provider-owned hot paths.
/// </summary>
[MemoryDiagnoser]
public class AllocationBenchmarks
{
    private const int RowCount = 1_000;

    private List<AllocationRow> _rows = [];
    private List<AllocationRow> _upsertRows = [];
    private DerivedIntList _idList = [];

    private string _saveChangesDbPath = "";
    private string _saveChangesUpdateDbPath = "";
    private string _bulkInsertDbPath = "";
    private string _upsertDbPath = "";
    private string _arrayParameterDbPath = "";

    [GlobalSetup]
    public void GlobalSetup()
    {
        _rows = Enumerable.Range(1, RowCount)
            .Select(i => new AllocationRow
            {
                Id = i,
                Name = "row-" + i,
                Quantity = i,
                Price = i * 1.25m,
                Weight = i * 0.75,
                Active = (i & 1) == 0,
                CreatedAt = new DateTime(2026, 1, 1).AddMinutes(i)
            })
            .ToList();

        _upsertRows = Enumerable.Range(1, RowCount)
            .Select(i => new AllocationRow
            {
                Id = i,
                Name = "upsert-" + i,
                Quantity = i * 2,
                Price = i * 2.5m,
                Weight = i * 1.5,
                Active = (i & 1) == 1,
                CreatedAt = new DateTime(2026, 6, 1).AddMinutes(i)
            })
            .ToList();

        _idList = new DerivedIntList();
        _idList.AddRange(Enumerable.Range(1, 500).Where(i => (i & 1) == 0));

        _arrayParameterDbPath = NewDbPath("alloc_array");
        using var context = new AllocationContext(_arrayParameterDbPath, enableSaveChangesBatching: false);
        context.Database.EnsureCreated();
        context.BulkInsert(_rows);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        DeleteDb(_arrayParameterDbPath);
    }

    [IterationSetup(Target = nameof(SaveChangesInsertBatching))]
    public void SetupSaveChanges()
    {
        _saveChangesDbPath = NewDbPath("alloc_savechanges");
        using var context = new AllocationContext(_saveChangesDbPath, enableSaveChangesBatching: true);
        context.Database.EnsureCreated();
    }

    [IterationCleanup(Target = nameof(SaveChangesInsertBatching))]
    public void CleanupSaveChanges()
        => DeleteDb(_saveChangesDbPath);

    [IterationSetup(Target = nameof(SaveChangesUpdateBatching))]
    public void SetupSaveChangesUpdate()
    {
        _saveChangesUpdateDbPath = NewDbPath("alloc_savechanges_update");
        using var context = new AllocationContext(
            _saveChangesUpdateDbPath,
            enableSaveChangesBatching: false,
            enableUpdateBatching: true);
        context.Database.EnsureCreated();
        context.BulkInsert(_rows);
    }

    [IterationCleanup(Target = nameof(SaveChangesUpdateBatching))]
    public void CleanupSaveChangesUpdate()
        => DeleteDb(_saveChangesUpdateDbPath);

    [IterationSetup(Target = nameof(BulkInsertAppender))]
    public void SetupBulkInsert()
    {
        _bulkInsertDbPath = NewDbPath("alloc_bulk");
        using var context = new AllocationContext(_bulkInsertDbPath, enableSaveChangesBatching: false);
        context.Database.EnsureCreated();
    }

    [IterationCleanup(Target = nameof(BulkInsertAppender))]
    public void CleanupBulkInsert()
        => DeleteDb(_bulkInsertDbPath);

    [IterationSetup(Target = nameof(UpsertBatch))]
    public void SetupUpsert()
    {
        _upsertDbPath = NewDbPath("alloc_upsert");
        using var context = new AllocationContext(_upsertDbPath, enableSaveChangesBatching: false);
        context.Database.EnsureCreated();
        context.BulkInsert(_rows.Take(RowCount / 2));
    }

    [IterationCleanup(Target = nameof(UpsertBatch))]
    public void CleanupUpsert()
        => DeleteDb(_upsertDbPath);

    [Benchmark]
    public int SaveChangesInsertBatching()
    {
        using var context = new AllocationContext(_saveChangesDbPath, enableSaveChangesBatching: true);
        context.AddRange(_rows);
        return context.SaveChanges();
    }

    [Benchmark]
    public int SaveChangesUpdateBatching()
    {
        using var context = new AllocationContext(
            _saveChangesUpdateDbPath,
            enableSaveChangesBatching: false,
            enableUpdateBatching: true);
        foreach (var row in context.Rows)
        {
            row.Quantity += 1;
        }

        return context.SaveChanges();
    }

    [Benchmark]
    public int BulkInsertAppender()
    {
        using var context = new AllocationContext(_bulkInsertDbPath, enableSaveChangesBatching: false);
        return context.BulkInsert(_rows);
    }

    [Benchmark]
    public int UpsertBatch()
    {
        using var context = new AllocationContext(_upsertDbPath, enableSaveChangesBatching: false);
        return context.Upsert(_upsertRows);
    }

    [Benchmark]
    public int ArrayParameterEnumerable()
    {
        using var context = new AllocationContext(_arrayParameterDbPath, enableSaveChangesBatching: false);
        return context.Rows.Count(r => _idList.Contains(r.Id));
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

    private sealed class AllocationContext(
        string dbPath,
        bool enableSaveChangesBatching,
        bool enableUpdateBatching = false) : DbContext
    {
        public DbSet<AllocationRow> Rows => Set<AllocationRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB(
                "DataSource=" + dbPath,
                duckdb =>
                {
                    if (enableSaveChangesBatching)
                    {
                        duckdb.EnableBulkInsertBatching();
                    }

                    if (enableUpdateBatching)
                    {
                        duckdb.EnableBulkUpdateBatching();
                    }
                });

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<AllocationRow>().Property(e => e.Id).ValueGeneratedNever();
    }

    private sealed class AllocationRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public double Weight { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class DerivedIntList : List<int>;
}