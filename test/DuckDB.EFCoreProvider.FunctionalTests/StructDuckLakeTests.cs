using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class StructDuckLakeTests
{
    [Fact]
    public void DuckLake_profile_supports_struct_query_insert_and_update()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ducklake_struct_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var metadataPath = Path.Combine(root, "catalog.ducklake");
        var dataPath = Path.Combine(root, "data");
        Directory.CreateDirectory(dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<DuckLakeStructContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .Options;
            using (var context = new DuckLakeStructContext(options))
            {
                context.Database.EnsureCreated();
                context.Entities.Add(new DuckLakeStructEntity
                {
                    Id = 1,
                    Location = new DuckLakeAddress { City = "NYC", Country = "US" }
                });
                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = new DuckLakeStructContext(options))
            {
                var entity = Assert.Single(
                    context.Entities.Where(value => value.Location.Country == "US"));
                Assert.Equal("NYC", entity.Location.City);
                entity.Location.City = "Boston";
                Assert.Equal(1, context.SaveChanges());
            }

            using var verificationContext = new DuckLakeStructContext(options);
            Assert.Equal("Boston", Assert.Single(verificationContext.Entities).Location.City);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DuckLake_profile_rejects_generated_column_inside_complex_property()
    {
        var metadataPath = Path.Combine(Path.GetTempPath(), $"ducklake_struct_validation_{Guid.NewGuid():N}.ducklake");
        var options = new DbContextOptionsBuilder<DuckLakeValidationContext>()
            .UseDuckLake(metadataPath)
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings =>
                warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        using var context = new DuckLakeValidationContext(options);

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("generated columns", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DuckLakeStructContext(DbContextOptions<DuckLakeStructContext> options)
        : DbContext(options)
    {
        public DbSet<DuckLakeStructEntity> Entities => Set<DuckLakeStructEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DuckLakeStructEntity>(entity =>
            {
                entity.ToTable("struct_entities");
                entity.HasKey(value => value.Id);
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Location).UseStructMapping();
            });
        }
    }

    private sealed class DuckLakeStructEntity
    {
        public int Id { get; set; }

        public required DuckLakeAddress Location { get; set; }
    }

    private sealed class DuckLakeAddress
    {
        public string City { get; set; } = null!;

        public string Country { get; set; } = null!;
    }

    private sealed class DuckLakeValidationContext(DbContextOptions<DuckLakeValidationContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<DuckLakeValidationEntity>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Details, complex =>
                    complex.Property(value => value.Computed)
                        .HasComputedColumnSql("\"Id\" + 1"));
            });
    }

    private sealed class DuckLakeValidationEntity
    {
        public int Id { get; set; }

        public required DuckLakeValidationDetails Details { get; set; }
    }

    private sealed class DuckLakeValidationDetails
    {
        public int Computed { get; set; }
    }
}