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
    public void Allows_optional_struct_root()
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

        // Optional (nullable) STRUCT roots are supported so whole-complex null comparisons can be
        // rewritten to struct-itself IS NULL / IS NOT NULL checks.
        var location = context.Model.FindEntityType(typeof(OptionalStructEntity))!
            .FindComplexProperty("Location")!;
        Assert.True(location.IsNullable);
        Assert.NotNull(location.GetStructMapping());
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

    [Fact]
    public void Rejects_scalar_leaf_and_nested_struct_path_collision()
    {
        using var context = CreateContext(modelBuilder =>
            modelBuilder.Entity<PathCollisionEntity>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Location, complex =>
                {
                    complex.UseStructMapping();
                    complex.ComplexProperty(value => value.Details, nested =>
                        nested.HasStructFieldName("address"));
                });
            }));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("conflicting DuckDB STRUCT paths", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Location", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Location.address", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Location.address.country", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_deep_scalar_and_nested_struct_path_collision()
    {
        using var context = CreateContext(modelBuilder =>
            modelBuilder.Entity<DeepCollisionEntity>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Location, complex =>
                {
                    complex.UseStructMapping();
                    complex.ComplexProperty(value => value.Shallow, shallow =>
                    {
                        shallow.HasStructFieldName("branch");
                        shallow.Property(value => value.Value).HasStructFieldName("deep");
                    });
                    complex.ComplexProperty(value => value.Deep, deep =>
                    {
                        deep.HasStructFieldName("branch");
                        deep.ComplexProperty(value => value.Inner, inner =>
                        {
                            inner.HasStructFieldName("deep");
                            inner.Property(value => value.Value);
                        });
                    });
                });
            }));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("conflicting DuckDB STRUCT paths", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Location.branch.deep", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Location.branch.deep.value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_case_insensitive_struct_path_collision()
    {
        using var context = CreateContext(modelBuilder =>
            modelBuilder.Entity<PathCollisionEntity>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Location, complex =>
                {
                    complex.UseStructMapping();
                    complex.ComplexProperty(value => value.Details, nested =>
                        nested.HasStructFieldName("Address"));
                });
            }));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("conflicting DuckDB STRUCT paths", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_collision_via_struct_field_name_override()
    {
        using var context = CreateContext(modelBuilder =>
            modelBuilder.Entity<FieldOverrideEntity>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Location, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.Street).HasStructFieldName("address");
                    complex.ComplexProperty(value => value.Details, nested =>
                        nested.HasStructFieldName("address"));
                });
            }));

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("conflicting DuckDB STRUCT paths", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Location.address", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Location.address.country", exception.Message, StringComparison.Ordinal);
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

        private sealed class PathCollisionEntity
        {
            public int Id { get; set; }

            public required PathCollisionAddress Location { get; set; }
        }

        private sealed class PathCollisionAddress
        {
            public string Address { get; set; } = null!;

            public required PathCollisionDetails Details { get; set; }
        }

        private sealed class PathCollisionDetails
        {
            public string Country { get; set; } = null!;
        }

        private sealed class FieldOverrideEntity
        {
            public int Id { get; set; }

            public required FieldOverrideAddress Location { get; set; }
        }

        private sealed class FieldOverrideAddress
        {
            public string Street { get; set; } = null!;

            public required FieldOverrideDetails Details { get; set; }
        }

        private sealed class FieldOverrideDetails
        {
            public string Country { get; set; } = null!;
        }

        private sealed class DeepCollisionEntity
        {
            public int Id { get; set; }

            public required DeepCollisionRoot Location { get; set; }
        }

        private sealed class DeepCollisionRoot
        {
            public required ShallowBranch Shallow { get; set; }

            public required DeepBranch Deep { get; set; }
        }

        private sealed class ShallowBranch
        {
            public string Value { get; set; } = null!;
        }

        private sealed class DeepBranch
        {
            public required DeepBranchInner Inner { get; set; }
        }

        private sealed class DeepBranchInner
        {
            public string Value { get; set; } = null!;
        }
    }
