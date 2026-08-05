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
    public void Has_struct_foreign_key_reuses_existing_complex_leaf_mapping()
    {
        using var context = CreateContext(modelBuilder =>
        {
            modelBuilder.Entity<StructPrincipal>(entity =>
            {
                entity.FromParquet("principals.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<StructDependent>(entity =>
            {
                entity.FromParquet("dependents.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Relationship, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.ParentId).HasStructFieldName("parent_id");
                });
                entity.HasOne(value => value.Principal)
                    .WithMany(value => value.Dependents)
                    .HasStructForeignKey(value => value.Relationship.ParentId);
            });
        });

        var dependent = context.Model.FindEntityType(typeof(StructDependent));
        var foreignKey = Assert.Single(dependent!.GetForeignKeys());
        var property = Assert.Single(foreignKey.Properties);
        var leaf = dependent.FindComplexProperty(nameof(StructDependent.Relationship))!
            .ComplexType
            .FindProperty(nameof(StructRelationshipPath.ParentId))!;

        Assert.StartsWith("__DuckDBStructForeignKey_", property.Name, StringComparison.Ordinal);
        Assert.Equal(leaf.GetColumnName(), property.GetColumnName());
        Assert.Null(property.GetStructFieldInfo());
        Assert.Equal("Relationship", leaf.GetStructFieldInfo()!.StructColumnName);
        Assert.Equal("parent_id", leaf.GetStructFieldInfo()!.LeafFieldName);
    }

    [Fact]
    public void Struct_foreign_key_requiredness_is_inferred_from_leaf_nullability()
    {
        using var context = CreateContext(modelBuilder =>
        {
            modelBuilder.Entity<StructPrincipal>(entity =>
            {
                entity.FromParquet("principals.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<StructDependent>(entity =>
            {
                entity.FromParquet("dependents.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Relationship, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.ParentId).HasStructFieldName("parent_id");
                });
                entity.HasOne(value => value.Principal)
                    .WithMany(value => value.Dependents)
                    .HasStructForeignKey(value => value.Relationship.ParentId);
            });
            modelBuilder.Entity<StructRequiredLeafDependent>(entity =>
            {
                entity.FromParquet("required_dependents.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Relationship, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.ParentId).HasStructFieldName("parent_id");
                });
                entity.HasOne(value => value.Principal)
                    .WithMany()
                    .HasStructForeignKey(value => value.Relationship.ParentId);
            });
        });

        var nullableLeafForeignKey = Assert.Single(
            context.Model.FindEntityType(typeof(StructDependent))!.GetForeignKeys());
        Assert.False(nullableLeafForeignKey.IsRequired);

        var nonNullableLeafForeignKey = Assert.Single(
            context.Model.FindEntityType(typeof(StructRequiredLeafDependent))!.GetForeignKeys());
        Assert.True(nonNullableLeafForeignKey.IsRequired);
    }

    [Fact]
    public void Explicit_requiredness_override_wins_over_leaf_inference()
    {
        using var context = CreateContext(modelBuilder =>
        {
            modelBuilder.Entity<StructPrincipal>(entity =>
            {
                entity.FromParquet("principals.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<StructDependent>(entity =>
            {
                entity.FromParquet("dependents.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Relationship, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.ParentId).HasStructFieldName("parent_id");
                });
                entity.HasOne(value => value.Principal)
                    .WithMany(value => value.Dependents)
                    .HasStructForeignKey(value => value.Relationship.ParentId)
                    .IsRequired(true);
            });
            modelBuilder.Entity<StructRequiredLeafDependent>(entity =>
            {
                entity.FromParquet("required_dependents.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Relationship, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.ParentId).HasStructFieldName("parent_id");
                });
                entity.HasOne(value => value.Principal)
                    .WithMany()
                    .HasStructForeignKey(value => value.Relationship.ParentId)
                    .IsRequired(false);
            });
        });

        var explicitlyRequiredForeignKey = Assert.Single(
            context.Model.FindEntityType(typeof(StructDependent))!.GetForeignKeys());
        Assert.True(explicitlyRequiredForeignKey.IsRequired);

        var explicitlyOptionalForeignKey = Assert.Single(
            context.Model.FindEntityType(typeof(StructRequiredLeafDependent))!.GetForeignKeys());
        Assert.False(explicitlyOptionalForeignKey.IsRequired);
    }

    [Fact]
    public void Has_struct_foreign_key_supports_one_to_one_relationships()
    {
        using var context = CreateContext(modelBuilder =>
        {
            modelBuilder.Entity<StructOnePrincipal>(entity =>
            {
                entity.FromParquet("principals.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<StructOneDependent>(entity =>
            {
                entity.FromParquet("dependents.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Relationship, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.ParentId).HasStructFieldName("parent_id");
                });
                entity.HasOne(value => value.Principal)
                    .WithOne(value => value.Dependent)
                    .HasStructForeignKey(value => value.Relationship.ParentId);
            });
        });

        var dependent = context.Model.FindEntityType(typeof(StructOneDependent));
        var foreignKey = Assert.Single(dependent!.GetForeignKeys());

        Assert.StartsWith(
            "__DuckDBStructForeignKey_",
            Assert.Single(foreignKey.Properties).Name,
            StringComparison.Ordinal);
        Assert.True(foreignKey.IsUnique);
    }

    [Fact]
    public void Has_struct_foreign_key_supports_one_to_one_from_principal_side()
    {
        using var context = CreateContext(modelBuilder =>
        {
            modelBuilder.Entity<StructOnePrincipal>(entity =>
            {
                entity.FromParquet("principals.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.HasOne(value => value.Dependent)
                    .WithOne(value => value.Principal)
                    .HasStructForeignKey(value => value.Relationship.ParentId);
            });
            modelBuilder.Entity<StructOneDependent>(entity =>
            {
                entity.FromParquet("dependents.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Relationship, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.ParentId).HasStructFieldName("parent_id");
                });
            });
        });

        var dependent = context.Model.FindEntityType(typeof(StructOneDependent));
        var foreignKey = Assert.Single(dependent!.GetForeignKeys());

        Assert.StartsWith(
            "__DuckDBStructForeignKey_",
            Assert.Single(foreignKey.Properties).Name,
            StringComparison.Ordinal);
        Assert.True(foreignKey.IsUnique);
    }

    [Fact]
    public void Distinct_struct_foreign_key_paths_do_not_share_a_shadow_property()
    {
        using var context = CreateContext(modelBuilder =>
        {
            modelBuilder.Entity<StructCollisionPrincipalA>(entity =>
            {
                entity.FromParquet("principals_a.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<StructCollisionPrincipalB>(entity =>
            {
                entity.FromParquet("principals_b.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<StructCollisionDependent>(entity =>
            {
                entity.FromParquet("collision_dependents.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Relationship_, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.B).HasStructFieldName("b");
                });
                entity.ComplexProperty(value => value.Relationship, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value._B).HasStructFieldName("_b");
                });
                entity.HasOne(value => value.PrincipalA)
                    .WithMany()
                    .HasStructForeignKey(value => value.Relationship_.B);
                entity.HasOne(value => value.PrincipalB)
                    .WithMany()
                    .HasStructForeignKey(value => value.Relationship._B);
            });
        });

        var dependent = context.Model.FindEntityType(typeof(StructCollisionDependent))!;
        var foreignKeys = dependent.GetForeignKeys().ToArray();
        Assert.Equal(2, foreignKeys.Length);

        var properties = foreignKeys
            .Select(foreignKey => Assert.Single(foreignKey.Properties))
            .ToArray();
        Assert.NotEqual(properties[0].Name, properties[1].Name);

        // Each relationship must join through its own leaf: the paths 'Relationship_.B' and
        // 'Relationship._B' used to collapse onto one shadow property.
        var firstLeaf = dependent.FindComplexProperty(nameof(StructCollisionDependent.Relationship_))!
            .ComplexType
            .FindProperty(nameof(StructCollisionRootA.B))!;
        var secondLeaf = dependent.FindComplexProperty(nameof(StructCollisionDependent.Relationship))!
            .ComplexType
            .FindProperty(nameof(StructCollisionRootB._B))!;

        var columns = properties.Select(property => property.GetColumnName()).ToArray();
        Assert.Contains(firstLeaf.GetColumnName(), columns);
        Assert.Contains(secondLeaf.GetColumnName(), columns);
    }

    [Fact]
    public void Rejects_struct_foreign_key_on_physical_table()
    {
        using var context = CreateContext(modelBuilder =>
        {
            modelBuilder.Entity<StructPrincipal>(entity =>
                entity.Property(value => value.Id).ValueGeneratedNever());
            modelBuilder.Entity<StructDependent>(entity =>
            {
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Relationship, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.ParentId).HasStructFieldName("parent_id");
                });
                entity.HasOne(value => value.Principal)
                    .WithMany(value => value.Dependents)
                    .HasStructForeignKey(value => value.Relationship.ParentId);
            });
        });

        var exception = Assert.Throws<NotSupportedException>(() => _ = context.Model);
        Assert.Contains("only between DuckDB file-backed entities", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_struct_foreign_key_without_a_struct_mapping()
    {
        using var context = CreateContext(modelBuilder =>
        {
            modelBuilder.Entity<StructPrincipal>(entity =>
                entity.Property(value => value.Id).ValueGeneratedNever());
            modelBuilder.Entity<StructDependent>(entity =>
            {
                entity.FromParquet("dependents.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Relationship);
                entity.HasOne(value => value.Principal)
                    .WithMany(value => value.Dependents)
                    .HasStructForeignKey(value => value.Relationship.ParentId);
            });
        });

        var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
        Assert.Contains("does not resolve to a mapped DuckDB STRUCT leaf", exception.Message, StringComparison.Ordinal);
        Assert.Contains("UseStructMapping", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Struct_foreign_key_selector_must_end_at_a_nested_member()
    {
        using var context = CreateContext(modelBuilder =>
        {
            modelBuilder.Entity<StructPrincipal>(entity =>
                entity.Property(value => value.Id).ValueGeneratedNever());
            modelBuilder.Entity<StructDependent>(entity =>
            {
                entity.FromParquet("dependents.parquet");
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.ComplexProperty(value => value.Relationship).UseStructMapping();
                entity.HasOne(value => value.Principal)
                    .WithMany(value => value.Dependents)
                    .HasStructForeignKey(value => value.Id);
            });
        });

        var exception = Assert.Throws<ArgumentException>(() => _ = context.Model);
        Assert.Contains("nested member path", exception.Message, StringComparison.Ordinal);
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

    private sealed class StructPrincipal
    {
        public int Id { get; set; }

        public List<StructDependent> Dependents { get; set; } = [];
    }

    private sealed class StructDependent
    {
        public int Id { get; set; }

        public required StructRelationshipPath Relationship { get; set; }

        public StructPrincipal? Principal { get; set; }
    }

    private sealed class StructRelationshipPath
    {
        public int? ParentId { get; set; }
    }

    private sealed class StructRequiredLeafDependent
    {
        public int Id { get; set; }

        public required StructRequiredLeafPath Relationship { get; set; }

        public StructPrincipal? Principal { get; set; }
    }

    private sealed class StructRequiredLeafPath
    {
        public int ParentId { get; set; }
    }

    private sealed class StructOnePrincipal
    {
        public int Id { get; set; }

        public StructOneDependent? Dependent { get; set; }
    }

    private sealed class StructOneDependent
    {
        public int Id { get; set; }

        public required StructRelationshipPath Relationship { get; set; }

        public StructOnePrincipal? Principal { get; set; }
    }

    private sealed class StructCollisionPrincipalA
    {
        public int Id { get; set; }
    }

    private sealed class StructCollisionPrincipalB
    {
        public int Id { get; set; }
    }

    private sealed class StructCollisionDependent
    {
        public int Id { get; set; }

        public required StructCollisionRootA Relationship_ { get; set; }

        public required StructCollisionRootB Relationship { get; set; }

        public StructCollisionPrincipalA? PrincipalA { get; set; }

        public StructCollisionPrincipalB? PrincipalB { get; set; }
    }

    private sealed class StructCollisionRootA
    {
        public int? B { get; set; }
    }

    private sealed class StructCollisionRootB
    {
        public int? _B { get; set; }
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