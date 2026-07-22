using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Verifies that tiered-storage operations reject struct-mapped entities instead of silently flattening
///     DuckDB STRUCT columns into scalar Parquet columns.
/// </summary>
public sealed class StructTieredStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "duckdb-struct-tier-" + Guid.NewGuid().ToString("N"));

    public StructTieredStorageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public async Task ArchiveTierAsync_rejects_struct_mapped_root()
    {
        var dbPath = Path.Combine(_root, "struct.duckdb");
        var archivePath = Path.Combine(_root, "struct-archive");
        using var context = new StructRootContext(dbPath, archivePath);

        context.Database.EnsureCreated();
        context.WeatherRecords.Add(new WeatherRecord
        {
            Id = 1,
            EffectiveAt = new DateTime(2024, 1, 10),
            Location = new Location { City = "NYC", Country = "US" },
        });
        context.SaveChanges();

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => context.Database.ArchiveTierAsync<WeatherRecord>(new DateTime(2024, 2, 1)));

        Assert.Contains("struct-mapped", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private interface IArchivePathContext
    {
        string ArchivePath { get; }
    }

    private sealed class StructRootContext(string dbPath, string archivePath) : DbContext, IArchivePathContext
    {
        public DbSet<WeatherRecord> WeatherRecords => Set<WeatherRecord>();

        public string ArchivePath => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseDuckDB($"Data Source={dbPath}")
                .ReplaceService<IModelCacheKeyFactory, ArchivePathModelCacheKeyFactory>()
                .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<WeatherRecord>(b =>
            {
                b.ToTable("weather_records");
                b.HasKey(r => r.Id);
                b.Property(r => r.Id).ValueGeneratedNever();
                b.ComplexProperty(r => r.Location).UseStructMapping();
            });

            modelBuilder.ToTieredStore<WeatherRecord>(r => r.EffectiveAt, archivePath)
                .WithTieredView();
        }
    }

    private sealed class ArchivePathModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), (context as IArchivePathContext)?.ArchivePath, designTime);
    }

    private sealed class WeatherRecord
    {
        public int Id { get; set; }
        public DateTime EffectiveAt { get; set; }
        public required Location Location { get; set; }
    }

    private sealed class Location
    {
        public string City { get; set; } = null!;
        public string Country { get; set; } = null!;
    }
}
