using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;

// Run with: dotnet run --project samples/DuckLake
var root = Path.Combine(Environment.CurrentDirectory, "ducklake-sample");
var metadataPath = Path.Combine(root, "catalog", "analytics.ducklake");
var dataPath = Path.Combine(root, "data");

// EnsureDeleted is intentionally unavailable for DuckLake; this local sample owns both paths and removes them explicitly.
if (Directory.Exists(root))
{
    Directory.Delete(root, recursive: true);
}

Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
Directory.CreateDirectory(dataPath);

var options = new DbContextOptionsBuilder<AnalyticsContext>()
    .UseDuckLake(
        metadataPath,
        duckLake => duckLake
            .CatalogName("analytics")
            .DataPath(dataPath))
    .Options;

using var context = new AnalyticsContext(options);
context.Database.EnsureCreated();

var trackedId = Guid.NewGuid();
context.Measurements.Add(new Measurement
{
    Id = trackedId,
    Sensor = "tracked",
    Value = 10
});
context.SaveChanges();

context.BulkInsert(
[
    new Measurement { Id = Guid.NewGuid(), Sensor = "bulk-a", Value = 20 },
    new Measurement { Id = Guid.NewGuid(), Sensor = "bulk-b", Value = 30 }
]);

context.Upsert(
[
    new Measurement { Id = trackedId, Sensor = "tracked", Value = 15 },
    new Measurement { Id = Guid.NewGuid(), Sensor = "merged", Value = 40 }
]);

var summary = context.Measurements
    .GroupBy(measurement => measurement.Sensor)
    .Select(group => new { Sensor = group.Key, Count = group.Count(), Average = group.Average(row => row.Value) })
    .OrderBy(row => row.Sensor)
    .ToList();

foreach (var row in summary)
{
    Console.WriteLine($"{row.Sensor}: {row.Count} row(s), average {row.Average:N1}");
}

Console.WriteLine($"DuckLake metadata: {metadataPath}");
Console.WriteLine($"DuckLake data:     {dataPath}");

public sealed class AnalyticsContext(DbContextOptions<AnalyticsContext> options) : DbContext(options)
{
    public DbSet<Measurement> Measurements => Set<Measurement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Measurement>(entity =>
        {
            entity.HasKey(measurement => measurement.Id); // logical EF key; DuckLake does not enforce it physically
            entity.Property(measurement => measurement.Sensor).IsRequired();
        });
}

public sealed class Measurement
{
    public Guid Id { get; set; }
    public required string Sensor { get; set; }
    public double Value { get; set; }
}