using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace DuckDB.EFCoreProvider.FunctionalTests;

/// <summary>
///     Tests for <see cref="DuckDBStructFieldConvention" /> struct field metadata inference
///     and the opt-in mechanism via <c>[UseStructMapping]</c> and fluent API.
/// </summary>
public class DuckDBStructFieldConventionTest
{
    // ─── Model types for tests ──────────────────────────────────────────

    private sealed class CustomerWithAttribute
    {
        public int Id { get; set; }
        [UseStructMapping]
        public required CustomerLocation Location { get; set; }
    }

    private sealed class CustomerWithFluent
    {
        public int Id { get; set; }
        public required CustomerLocation Location { get; set; }
    }

    private sealed class CustomerNoMarker
    {
        public int Id { get; set; }
        public required CustomerLocation Location { get; set; }
    }

    private sealed class CustomerLocation
    {
        public required string City { get; set; }
        public required string Country { get; set; }
    }

    private sealed class OrderWithNestedStruct
    {
        public int Id { get; set; }
        [UseStructMapping]
        public required OrderShipping Shipping { get; set; }
    }

    private sealed class OrderShipping
    {
        public required string Method { get; set; }
        public required OrderAddress Address { get; set; }
    }

    private sealed class OrderAddress
    {
        public required string Street { get; set; }
        public required string Zip { get; set; }
    }

    // ── Mixed complex properties: one struct-mapped, one not ──────────

    private sealed class EntityWithMixedComplexProperties
    {
        public int Id { get; set; }

        [UseStructMapping]
        public required CustomerLocation StructMapped { get; set; }

        public required CustomerLocation NotStructMapped { get; set; }
    }

    // ─── Helper: build and finalize a model with DuckDB conventions ─────

    /// <summary>Navigates an entity → complex property → complex type → sub-property.</summary>
    private static IProperty GetComplexScalar(IModel model, Type entityType,
        string complexName, string propName)
    {
        var entity = model.FindEntityType(entityType);
        Assert.NotNull(entity);
        var cp = entity.FindComplexProperty(complexName);
        Assert.NotNull(cp);
        var prop = cp.ComplexType.FindProperty(propName);
        Assert.NotNull(prop);
        return prop;
    }

    /// <summary>Gets the struct field info annotation from a property.</summary>
    private static DuckDBStructFieldInfo? GetStructFieldInfo(IProperty property)
        => property.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value as DuckDBStructFieldInfo;

    /// <summary>A context type that ensures unique model cache keys per test.</summary>
    private sealed class TestDbContext<T> : DbContext
        where T : class
    {
        private readonly Action<ModelBuilder> _configure;
        public TestDbContext(DbContextOptions<TestDbContext<T>> options, Action<ModelBuilder> configure)
            : base(options) => _configure = configure;
        protected override void OnModelCreating(ModelBuilder modelBuilder) => _configure(modelBuilder);
    }

    /// <summary>Builds a finalized model. Service provider caching is disabled so each
    ///     test gets a fresh model regardless of context type.</summary>
    private static IModel BuildModel<T>(Action<ModelBuilder> configure)
        where T : class
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext<T>>();
        optionsBuilder
            .UseDuckDB("DataSource=:memory:")
            .EnableServiceProviderCaching(false);
        using var ctx = new TestDbContext<T>(optionsBuilder.Options, configure);
        var model = ctx.Model;
        return model;
    }

    // ─── Tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Attribute_opt_in_enables_struct_field_inference()
    {
        var model = BuildModel<CustomerWithAttribute>(mb =>
            mb.Entity<CustomerWithAttribute>().ComplexProperty(c => c.Location));

        var cityProp = GetComplexScalar(model, typeof(CustomerWithAttribute), "Location", "City");
        Assert.NotNull(cityProp);

        Assert.Equal("city", cityProp.GetColumnName());
        var info = GetStructFieldInfo(cityProp);
        Assert.NotNull(info);
        Assert.Equal("Location", info.StructColumnName);
        Assert.Empty(info.NestedFieldNames);
    }

    [Fact]
    public void Fluent_opt_in_enables_struct_field_inference()
    {
        var model = BuildModel<CustomerWithFluent>(mb =>
            mb.Entity<CustomerWithFluent>(e =>
                e.ComplexProperty(c => c.Location).UseStructMapping()));

        var cityProp = GetComplexScalar(model, typeof(CustomerWithFluent), "Location", "City");
        Assert.Equal("city", cityProp.GetColumnName());
        var info = GetStructFieldInfo(cityProp);
        Assert.NotNull(info);
        Assert.Equal("Location", info.StructColumnName);
    }

    [Fact]
    public void No_marker_skips_struct_field_inference()
    {
        var model = BuildModel<CustomerNoMarker>(mb =>
            mb.Entity<CustomerNoMarker>().ComplexProperty(c => c.Location));

        var cityProp = GetComplexScalar(model, typeof(CustomerNoMarker), "Location", "City");

        Assert.Null(GetStructFieldInfo(cityProp));
        // EF Core defaults column name to property name — the convention didn't override it.
        Assert.Equal("City", cityProp.GetColumnName());
    }

    [Fact]
    public void Nested_struct_gets_correct_field_path()
    {
        var model = BuildModel<OrderWithNestedStruct>(mb =>
            mb.Entity<OrderWithNestedStruct>().ComplexProperty(o => o.Shipping));

        var entity = model.FindEntityType(typeof(OrderWithNestedStruct))!;
        var shippingProp = entity.FindComplexProperty("Shipping")!;
        var shippingType = shippingProp.ComplexType;

        // Top-level scalar under Shipping: Method
        var methodProp = shippingType.FindProperty("Method")!;
        Assert.Equal("method", methodProp.GetColumnName());
        var methodInfo = GetStructFieldInfo(methodProp);
        Assert.NotNull(methodInfo);
        Assert.Equal("Shipping", methodInfo.StructColumnName);
        Assert.Empty(methodInfo.NestedFieldNames);

        // Nested complex: Shipping → Address → Street
        var addressComplex = shippingType.FindComplexProperty("Address")!;
        var addressType = addressComplex.ComplexType;

        var streetProp = addressType.FindProperty("Street")!;
        Assert.Equal("street", streetProp.GetColumnName());
        var streetInfo = GetStructFieldInfo(streetProp);
        Assert.NotNull(streetInfo);
        Assert.Equal("Shipping", streetInfo.StructColumnName);
        Assert.Equal(["address"], streetInfo.NestedFieldNames);

        var zipProp = addressType.FindProperty("Zip")!;
        Assert.Equal("zip", zipProp.GetColumnName());
        var zipInfo = GetStructFieldInfo(zipProp);
        Assert.NotNull(zipInfo);
        Assert.Equal("Shipping", zipInfo.StructColumnName);
        Assert.Equal(["address"], zipInfo.NestedFieldNames);
    }

    [Fact]
    public void Explicit_HasStructField_overrides_convention()
    {
        var model = BuildModel<CustomerWithAttribute>(mb =>
            mb.Entity<CustomerWithAttribute>(e =>
                e.ComplexProperty(c => c.Location, loc =>
                    loc.Property(l => l.City).HasStructField("CustomerLocation"))));

        var cityProp = GetComplexScalar(model, typeof(CustomerWithAttribute), "Location", "City");
        var info = GetStructFieldInfo(cityProp);
        Assert.NotNull(info);
        Assert.Equal("CustomerLocation", info.StructColumnName);
    }

    [Fact]
    public void Explicit_ColumnName_overrides_convention_inferred_name()
    {
        var model = BuildModel<CustomerWithAttribute>(mb =>
            mb.Entity<CustomerWithAttribute>(e =>
                e.ComplexProperty(c => c.Location, loc =>
                    loc.Property(l => l.City).HasColumnName("city_name"))));

        var cityProp = GetComplexScalar(model, typeof(CustomerWithAttribute), "Location", "City");
        Assert.Equal("city_name", cityProp.GetColumnName());
        Assert.NotNull(GetStructFieldInfo(cityProp));
    }

    [Fact]
    public void Already_lowercase_property_is_preserved()
    {
        // When a complex-type property is already camelCase, ToCamelCase is a no-op.
        var model = BuildModel<CustomerWithAttribute>(mb =>
            mb.Entity<CustomerWithAttribute>().ComplexProperty(c => c.Location));

        var countryProp = GetComplexScalar(model, typeof(CustomerWithAttribute), "Location", "Country");
        Assert.Equal("country", countryProp.GetColumnName());
        Assert.NotNull(GetStructFieldInfo(countryProp));
    }

    [Fact]
    public void ComplexTypePropertyBuilder_HasStructField_overload_configures_annotation()
    {
        // Verifies the ComplexTypePropertyBuilder<T> overload of HasStructField works
        // for type-safe nested property configuration.
        var model = BuildModel<OrderWithNestedStruct>(mb =>
            mb.Entity<OrderWithNestedStruct>(e =>
                e.ComplexProperty(o => o.Shipping, shipping =>
                    shipping.ComplexProperty(s => s.Address, address =>
                        address.Property(a => a.Street)
                            .HasStructField("ShippingInfo", "addr")))));

        // Navigate: OrderWithNestedStruct → Shipping (complex) → Address (complex) → Street
        var entity = model.FindEntityType(typeof(OrderWithNestedStruct));
        Assert.NotNull(entity);
        var shipping = entity.FindComplexProperty("Shipping");
        Assert.NotNull(shipping);
        var address = shipping.ComplexType.FindComplexProperty("Address");
        Assert.NotNull(address);
        var streetProp = address.ComplexType.FindProperty("Street");
        Assert.NotNull(streetProp);

        var info = GetStructFieldInfo(streetProp);
        Assert.NotNull(info);
        Assert.Equal("ShippingInfo", info.StructColumnName);
        Assert.Equal(["addr"], info.NestedFieldNames);
    }

    [Fact]
    public void Only_marked_complex_property_gets_struct_annotations()
    {
        // When an entity has two complex properties but only one is marked
        // [UseStructMapping], only the marked one gets struct field annotations.
        var model = BuildModel<EntityWithMixedComplexProperties>(mb =>
        {
            mb.Entity<EntityWithMixedComplexProperties>(e =>
            {
                e.ComplexProperty(c => c.StructMapped);
                e.ComplexProperty(c => c.NotStructMapped);
            });
        });

        // StructMapped.City -> should have struct annotation
        var structCityProp = GetComplexScalar(model, typeof(EntityWithMixedComplexProperties),
            "StructMapped", "City");
        Assert.Equal("city", structCityProp.GetColumnName());
        var structInfo = GetStructFieldInfo(structCityProp);
        Assert.NotNull(structInfo);
        Assert.Equal("StructMapped", structInfo.StructColumnName);

        // NotStructMapped.City -> should NOT have struct annotation
        var notStructCityProp = GetComplexScalar(model, typeof(EntityWithMixedComplexProperties),
            "NotStructMapped", "City");
        Assert.Null(GetStructFieldInfo(notStructCityProp));
        // EF Core defaults column name to property name — the convention didn't override it.
        Assert.Equal("City", notStructCityProp.GetColumnName());
    }
}
