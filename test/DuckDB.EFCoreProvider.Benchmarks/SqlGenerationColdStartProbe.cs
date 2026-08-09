using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;

namespace DuckDB.EFCoreProvider.Benchmarks;

internal static class SqlGenerationColdStartProbe
{
    public static void Run()
    {
        using var context = new ProbeContext(
            new DbContextOptionsBuilder<ProbeContext>()
                .UseDuckDB("DataSource=:memory:")
                .Options);
        var helper = context.GetService<ISqlGenerationHelper>();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var identifier = helper.DelimitIdentifier("select");
        stopwatch.Stop();

        Console.WriteLine(
            $"COLD_SQL elapsed_ns={stopwatch.Elapsed.TotalNanoseconds:F0} allocated_bytes={GC.GetAllocatedBytesForCurrentThread() - allocatedBefore} identifier={identifier}");
    }

    private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options);
}

internal static class ModelStartupProbe
{
    public static void Run()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        using var context = new ProbeContext(
            new DbContextOptionsBuilder<ProbeContext>()
                .UseDuckDB("DataSource=:memory:")
                .Options);
        var propertyCount = context.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()).Count();
        stopwatch.Stop();

        Console.WriteLine(
            $"MODEL_STARTUP elapsed_ns={stopwatch.Elapsed.TotalNanoseconds:F0} allocated_bytes={GC.GetAllocatedBytesForCurrentThread() - allocatedBefore} properties={propertyCount}");
    }

    private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProbeEntity>(entity =>
            {
                entity.HasKey(value => value.Id);
                entity.Property(value => value.Amount).HasColumnType("DECIMAL(12,2)");
                entity.Property(value => value.CapturedAt).HasColumnType("TIMESTAMP_NS");
                entity.Property(value => value.Ids).HasColumnType("INTEGER[]");
            });
        }
    }

    private sealed class ProbeEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime CapturedAt { get; set; }
        public List<int> Ids { get; set; } = [];
    }
}