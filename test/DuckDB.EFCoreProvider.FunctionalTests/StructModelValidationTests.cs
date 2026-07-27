using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class StructModelValidationTests
{
    [Fact]
    public void Rejects_struct_concurrency_token()
    {
        using var context = CreateContext(modelBuilder =>
            modelBuilder.Entity<ValidatedEntity>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Location, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.City).IsConcurrencyToken();
                });
            }));

        var exception = Assert.Throws<NotSupportedException>(() => _ = context.Model);
        Assert.Contains("cannot be used for concurrency tokens", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_struct_default_value()
    {
        using var context = CreateContext(modelBuilder =>
            modelBuilder.Entity<ValidatedEntity>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Location, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.City).HasDefaultValue("unknown");
                });
            }));

        var exception = Assert.Throws<NotSupportedException>(() => _ = context.Model);
        Assert.Contains("cannot be used for default values", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_optional_struct_root()
    {
        using var context = CreateContext(modelBuilder =>
            modelBuilder.Entity<OptionalStructEntity>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Location, complex =>
                {
                    complex.UseStructMapping();
                    complex.IsRequired(false);
                });
            }));

        var exception = Assert.Throws<NotSupportedException>(() => _ = context.Model);
        Assert.Contains("must be required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_scalar_and_struct_using_same_physical_column()
    {
        using var context = CreateContext(modelBuilder =>
            modelBuilder.Entity<ValidatedEntity>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.Property(value => value.Collision).HasColumnName("Location");
                entity.ComplexProperty(value => value.Location).UseStructMapping();
            }));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("same physical column 'Location'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_case_only_scalar_and_struct_physical_column_collision()
    {
        using var context = CreateContext(modelBuilder =>
            modelBuilder.Entity<ValidatedEntity>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.Property(value => value.Collision).HasColumnName("location");
                entity.ComplexProperty(value => value.Location).UseStructMapping();
            }));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("same physical column 'location'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_complex_scalar_and_struct_using_same_physical_column()
    {
        using var context = CreateContext(modelBuilder =>
            modelBuilder.Entity<ComplexCollisionEntity>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Location).UseStructMapping();
                entity.ComplexProperty(value => value.Secondary, complex =>
                    complex.Property(value => value.City).HasColumnName("Location"));
            }));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("same physical column 'Location'", exception.Message, StringComparison.Ordinal);
    }

    private static ValidationContext CreateContext(Action<ModelBuilder> configure)
    {
        var options = new DbContextOptionsBuilder<ValidationContext>()
            .UseDuckDB("DataSource=:memory:")
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new ValidationContext(options, configure);
    }

    private sealed class ValidationContext(
        DbContextOptions<ValidationContext> options,
        Action<ModelBuilder> configure)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => configure(modelBuilder);
    }

    private sealed class ValidatedEntity
    {
        public int Id { get; set; }

        public string Collision { get; set; } = null!;

        public required ValidationAddress Location { get; set; }
    }

    private sealed class ComplexCollisionEntity
    {
        public int Id { get; set; }

        public required ValidationAddress Location { get; set; }

        public required ValidationAddress Secondary { get; set; }
    }

    private sealed class OptionalStructEntity
    {
        public int Id { get; set; }

        public ValidationAddress? Location { get; set; }
    }

    private sealed class ValidationAddress
    {
        public string City { get; set; } = null!;

        public string? Region { get; set; }
    }
}
