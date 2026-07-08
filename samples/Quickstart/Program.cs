using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;

// DuckDB.EFCoreProvider — Quickstart
//
// Run with:  dotnet run --project samples/Quickstart
//
// Creates a local file database (quickstart.duckdb), then demonstrates the four
// things you will reach for most: SaveChanges with DuckDB-generated keys, the
// appender-backed BulkInsert fast path, columnar LINQ analytics, and Upsert.

const string connectionString = "Data Source=quickstart.duckdb";

// Start from a clean database each run so the sample is repeatable.
using (var setup = new QuickstartContext(connectionString))
{
    setup.Database.EnsureDeleted();
    setup.Database.EnsureCreated();
}

// 1) Write with SaveChanges — change tracking plus a DuckDB-generated key read
//    back through RETURNING.
using (var context = new QuickstartContext(connectionString))
{
    context.Blogs.Add(new Blog { Title = "Hello DuckDB" });
    context.Blogs.Add(new Blog { Title = "Columnar all the things" });
    context.SaveChanges();

    foreach (var blog in context.Blogs.OrderBy(b => b.Id))
    {
        Console.WriteLine($"Blog #{blog.Id}: {blog.Title}");
    }
}

// 2) BulkInsert — appender-backed fast path (~1M rows/s). It bypasses the change
//    tracker and store-generated values, so supply every mapped column value.
using (var context = new QuickstartContext(connectionString))
{
    var measurements = Enumerable.Range(1, 100_000)
        .Select(i => new Measurement
        {
            Id = i,
            Sensor = $"sensor-{i % 10}",
            Value = i * 0.5,
        })
        .ToList();

    var inserted = context.BulkInsert(measurements);
    Console.WriteLine($"Bulk-inserted {inserted:N0} measurements.");
}

// 3) LINQ analytics — DuckDB's columnar engine aggregates these in place.
using (var context = new QuickstartContext(connectionString))
{
    var perSensor = context.Measurements
        .GroupBy(m => m.Sensor)
        .Select(g => new { Sensor = g.Key, Average = g.Average(m => m.Value), Count = g.Count() })
        .OrderBy(x => x.Sensor)
        .ToList();

    foreach (var row in perSensor)
    {
        Console.WriteLine($"{row.Sensor}: avg={row.Average:N1} over {row.Count:N0} rows");
    }
}

// 4) Upsert — insert new rows and update existing ones by primary key using
//    appender-staged batches plus INSERT ... ON CONFLICT DO UPDATE.
using (var context = new QuickstartContext(connectionString))
{
    var processed = context.Upsert(new[]
    {
        new Measurement { Id = 1, Sensor = "sensor-1", Value = -1.0 },        // updates existing row 1
        new Measurement { Id = 999_999, Sensor = "sensor-new", Value = 42 },  // inserts a brand-new row
    });
    Console.WriteLine($"Upserted {processed:N0} measurements.");
}

Console.WriteLine("Done. Database written to quickstart.duckdb");

/// <summary>
///     A minimal context wiring DuckDB into EF Core. The opt-in batching options merge consecutive
///     <see cref="DbContext.SaveChanges()" /> writes into single multi-row statements.
/// </summary>
public class QuickstartContext(string connectionString) : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();

    public DbSet<Measurement> Measurements => Set<Measurement>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseDuckDB(
            connectionString,
            duckdb => duckdb
                .EnableBulkInsertBatching()    // merge SaveChanges inserts into one statement
                .EnableBulkUpdateBatching()    // merge SaveChanges updates
                .EnableBulkDeleteBatching());  // merge SaveChanges deletes

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // DuckDB-backed generated integer key (a sequence, with the value read back via RETURNING).
        modelBuilder.Entity<Blog>().Property(b => b.Id).UseAutoIncrement();

        // BulkInsert and Upsert require caller-supplied keys, so generate nothing for Measurement.
        modelBuilder.Entity<Measurement>().Property(m => m.Id).ValueGeneratedNever();
    }
}

public class Blog
{
    public int Id { get; set; }

    public required string Title { get; set; }
}

public class Measurement
{
    public int Id { get; set; }

    public required string Sensor { get; set; }

    public double Value { get; set; }
}
