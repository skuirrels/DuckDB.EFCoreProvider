using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>Measures explicit non-executing command extraction for queryable and scalar terminal shapes.</summary>
[MemoryDiagnoser]
public class CommandPlanExtractionBenchmarks
{
    private PlanContext _context = null!;
    private IQueryable<PlanRow> _filtered = null!;
    private IQueryable<decimal> _amounts = null!;
    private IQueryable<int> _quantities = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var options = new DbContextOptionsBuilder<PlanContext>()
            .UseDuckDB("DataSource=:memory:")
            .Options;
        _context = new PlanContext(options);
        _filtered = _context.Rows.Where(row => row.Id > 10);
        _amounts = _filtered.Select(row => row.Amount);
        _quantities = _filtered.Select(row => row.Quantity);

        _ = Count();
        _ = LongCount();
        _ = Any();
        _ = Min();
        _ = Max();
        _ = Sum();
        _ = Average();
        _ = PromotedAverage();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
        => _context.Dispose();

    [Benchmark(Baseline = true)]
    public DuckDBCommandPlan Count()
        => _context.Database.GetDuckDBCountCommandPlan(_filtered);

    [Benchmark]
    public DuckDBCommandPlan LongCount()
        => _context.Database.GetDuckDBLongCountCommandPlan(_filtered);

    [Benchmark]
    public DuckDBCommandPlan Any()
        => _context.Database.GetDuckDBAnyCommandPlan(_filtered);

    [Benchmark]
    public DuckDBCommandPlan Min()
        => _context.Database.GetDuckDBMinCommandPlan(_amounts);

    [Benchmark]
    public DuckDBCommandPlan Max()
        => _context.Database.GetDuckDBMaxCommandPlan(_amounts);

    [Benchmark]
    public DuckDBCommandPlan Sum()
        => _context.Database.GetDuckDBSumCommandPlan(_amounts);

    [Benchmark]
    public DuckDBCommandPlan Average()
        => _context.Database.GetDuckDBAverageCommandPlan(_amounts);

    [Benchmark]
    public DuckDBCommandPlan PromotedAverage()
        => _context.Database.GetDuckDBAverageCommandPlan(_quantities);

    private sealed class PlanContext(DbContextOptions<PlanContext> options) : DbContext(options)
    {
        public DbSet<PlanRow> Rows => Set<PlanRow>();
    }

    private sealed class PlanRow
    {
        public int Id { get; set; }
        public int Quantity { get; set; }
        public decimal Amount { get; set; }
    }
}

/// <summary>Measures shared provider paths touched by the LINQ-provider feature work.</summary>
[MemoryDiagnoser]
public class ParameterPathBenchmarks
{
    private string _dbPath = "";
    private ParameterContext _context = null!;
    private RelationalTypeMapping _intMapping = null!;
    private DbCommand _parameterFactoryCommand = null!;

    [Params(1, 5, 20)]
    public int ParameterCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"linq_provider_parameter_{Guid.NewGuid():N}.db");
        _context = new ParameterContext(_dbPath);
        _context.Database.EnsureCreated();
        _context.Rows.Add(new ParameterRow
        {
            Id = 1,
            Value01 = 1,
            Value02 = 2,
            Value03 = 3,
            Value04 = 4,
            Value05 = 5,
            Value06 = 6,
            Value07 = 7,
            Value08 = 8,
            Value09 = 9,
            Value10 = 10,
            Value11 = 11,
            Value12 = 12,
            Value13 = 13,
            Value14 = 14,
            Value15 = 15,
            Value16 = 16,
            Value17 = 17,
            Value18 = 18,
            Value19 = 19,
            Value20 = 20
        });
        _context.SaveChanges();

        _intMapping = (RelationalTypeMapping)_context.GetService<IRelationalTypeMappingSource>()
            .FindMapping(typeof(int))!;
        _parameterFactoryCommand = _context.Database.GetDbConnection().CreateCommand();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _parameterFactoryCommand.Dispose();
        _context.Dispose();
        DeleteDatabase(_dbPath);
    }

    [Benchmark]
    public DbParameter CreateParameters()
    {
        DbParameter? parameter = null;
        for (var index = 0; index < ParameterCount; index++)
        {
            parameter = _intMapping.CreateParameter(
                _parameterFactoryCommand,
                "$p" + index,
                index,
                nullable: false);
        }

        return parameter!;
    }

    [Benchmark]
    public int ExecuteParameterizedQuery()
        => ParameterCount switch
        {
            1 => Query1(),
            5 => Query5(),
            20 => Query20(),
            _ => throw new InvalidOperationException()
        };

    private int Query1()
    {
        var p01 = 1;
        return _context.Rows.Count(row => row.Value01 >= p01);
    }

    private int Query5()
    {
        var p01 = 1;
        var p02 = 2;
        var p03 = 3;
        var p04 = 4;
        var p05 = 5;
        return _context.Rows.Count(row =>
            row.Value01 >= p01
            && row.Value02 >= p02
            && row.Value03 >= p03
            && row.Value04 >= p04
            && row.Value05 >= p05);
    }

    private int Query20()
    {
        var p01 = 1;
        var p02 = 2;
        var p03 = 3;
        var p04 = 4;
        var p05 = 5;
        var p06 = 6;
        var p07 = 7;
        var p08 = 8;
        var p09 = 9;
        var p10 = 10;
        var p11 = 11;
        var p12 = 12;
        var p13 = 13;
        var p14 = 14;
        var p15 = 15;
        var p16 = 16;
        var p17 = 17;
        var p18 = 18;
        var p19 = 19;
        var p20 = 20;
        return _context.Rows.Count(row =>
            row.Value01 >= p01
            && row.Value02 >= p02
            && row.Value03 >= p03
            && row.Value04 >= p04
            && row.Value05 >= p05
            && row.Value06 >= p06
            && row.Value07 >= p07
            && row.Value08 >= p08
            && row.Value09 >= p09
            && row.Value10 >= p10
            && row.Value11 >= p11
            && row.Value12 >= p12
            && row.Value13 >= p13
            && row.Value14 >= p14
            && row.Value15 >= p15
            && row.Value16 >= p16
            && row.Value17 >= p17
            && row.Value18 >= p18
            && row.Value19 >= p19
            && row.Value20 >= p20);
    }

    internal static void DeleteDatabase(string path)
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

    private sealed class ParameterContext(string dbPath) : DbContext
    {
        public DbSet<ParameterRow> Rows => Set<ParameterRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB("DataSource=" + dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ParameterRow>().Property(row => row.Id).ValueGeneratedNever();
    }

    private sealed class ParameterRow
    {
        public int Id { get; set; }
        public int Value01 { get; set; }
        public int Value02 { get; set; }
        public int Value03 { get; set; }
        public int Value04 { get; set; }
        public int Value05 { get; set; }
        public int Value06 { get; set; }
        public int Value07 { get; set; }
        public int Value08 { get; set; }
        public int Value09 { get; set; }
        public int Value10 { get; set; }
        public int Value11 { get; set; }
        public int Value12 { get; set; }
        public int Value13 { get; set; }
        public int Value14 { get; set; }
        public int Value15 { get; set; }
        public int Value16 { get; set; }
        public int Value17 { get; set; }
        public int Value18 { get; set; }
        public int Value19 { get; set; }
        public int Value20 { get; set; }
    }
}

[MemoryDiagnoser]
public class SaveChangesParameterBenchmarks
{
    private const int RowCount = 100;
    private string _dbPath = "";
    private SaveContext _context = null!;

    [IterationSetup]
    public void IterationSetup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"linq_provider_save_{Guid.NewGuid():N}.db");
        _context = new SaveContext(_dbPath);
        _context.Database.EnsureCreated();
        _context.Rows.AddRange(Enumerable.Range(1, RowCount).Select(index => new SaveRow
        {
            Id = index,
            Name = "row-" + index,
            Quantity = index,
            Price = index * 1.25m,
            Weight = index * 0.75,
            Active = (index & 1) == 0
        }));
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _context.Dispose();
        ParameterPathBenchmarks.DeleteDatabase(_dbPath);
    }

    [Benchmark]
    [InvocationCount(1)]
    public int SaveChanges()
        => _context.SaveChanges();

    private sealed class SaveContext(string dbPath) : DbContext
    {
        public DbSet<SaveRow> Rows => Set<SaveRow>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options.UseDuckDB("DataSource=" + dbPath);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<SaveRow>().Property(row => row.Id).ValueGeneratedNever();
    }

    private sealed class SaveRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public double Weight { get; set; }
        public bool Active { get; set; }
    }
}

[MemoryDiagnoser]
public class SqlGenerationPathBenchmarks
{
    private ISqlGenerationHelper _helper = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var options = new DbContextOptionsBuilder<SqlGenerationContext>()
            .UseDuckDB("DataSource=:memory:")
            .Options;
        var context = new SqlGenerationContext(options);
        _helper = context.GetService<ISqlGenerationHelper>();
        _ = _helper.DelimitIdentifier("select");
    }

    [Benchmark]
    public string DelimitWarmIdentifier()
        => _helper.DelimitIdentifier("length");

    private sealed class SqlGenerationContext(DbContextOptions<SqlGenerationContext> options) : DbContext(options);
}