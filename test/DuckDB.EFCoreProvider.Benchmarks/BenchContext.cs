using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;

namespace DuckDB.EFCoreProvider.Benchmarks;

internal sealed class BenchContext(string dbPath) : DbContext
{
    public DbSet<BenchRow> Rows => Set<BenchRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseDuckDB($"DataSource={dbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        // Explicit keys so SaveChanges and BulkInsert insert identical data (no store-generated values).
        => modelBuilder.Entity<BenchRow>().Property(e => e.Id).ValueGeneratedNever();
}

internal sealed class BenchRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double Value { get; set; }
    public bool Active { get; set; }
}
