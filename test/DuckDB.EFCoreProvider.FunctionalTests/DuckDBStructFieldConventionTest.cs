using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;
using System.Data;
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

    // ── Collision scenario: two struct properties with same leaf names ─

    private sealed class EntityWithCollidingStructLeaves
    {
        public int Id { get; set; }

        [UseStructMapping]
        public required CustomerLocation Billing { get; set; }

        [UseStructMapping]
        public required CustomerLocation Shipping { get; set; }
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

        Assert.Equal("location_city", cityProp.GetColumnName());
        var info = GetStructFieldInfo(cityProp);
        Assert.NotNull(info);
        Assert.Equal("Location", info.StructColumnName);
        Assert.Equal("city", info.LeafFieldName);
        Assert.Empty(info.NestedFieldNames);
    }

    [Fact]
    public void Fluent_opt_in_enables_struct_field_inference()
    {
        var model = BuildModel<CustomerWithFluent>(mb =>
            mb.Entity<CustomerWithFluent>(e =>
                e.ComplexProperty(c => c.Location).UseStructMapping()));

        var cityProp = GetComplexScalar(model, typeof(CustomerWithFluent), "Location", "City");
        Assert.Equal("location_city", cityProp.GetColumnName());
        var info = GetStructFieldInfo(cityProp);
        Assert.NotNull(info);
        Assert.Equal("Location", info.StructColumnName);
        Assert.Equal("city", info.LeafFieldName);
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
    public void Struct_field_expression_quote_recreates_expression()
    {
        var info = new DuckDBStructFieldInfo("Location", ["address"], "city");
        var expression = new DuckDBStructFieldExpression(
            "c",
            "Location",
            info,
            typeof(string));

        var quoted = expression.Quote();
        var recreated = Expression.Lambda<Func<DuckDBStructFieldExpression>>(quoted).Compile()();

        Assert.Equal(expression.TableAlias, recreated.TableAlias);
        Assert.Equal(expression.StructColumnName, recreated.StructColumnName);
        Assert.Equal(expression.StructFieldInfo.StructColumnName, recreated.StructFieldInfo.StructColumnName);
        Assert.Equal(expression.StructFieldInfo.NestedFieldNames, recreated.StructFieldInfo.NestedFieldNames);
        Assert.Equal(expression.StructFieldInfo.LeafFieldName, recreated.StructFieldInfo.LeafFieldName);
        Assert.Equal(expression.Type, recreated.Type);
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
        Assert.Equal("shipping_method", methodProp.GetColumnName());
        var methodInfo = GetStructFieldInfo(methodProp);
        Assert.NotNull(methodInfo);
        Assert.Equal("Shipping", methodInfo.StructColumnName);
        Assert.Equal("method", methodInfo.LeafFieldName);
        Assert.Empty(methodInfo.NestedFieldNames);

        // Nested complex: Shipping → Address → Street
        var addressComplex = shippingType.FindComplexProperty("Address")!;
        var addressType = addressComplex.ComplexType;

        var streetProp = addressType.FindProperty("Street")!;
        Assert.Equal("shipping_address_street", streetProp.GetColumnName());
        var streetInfo = GetStructFieldInfo(streetProp);
        Assert.NotNull(streetInfo);
        Assert.Equal("Shipping", streetInfo.StructColumnName);
        Assert.Equal("street", streetInfo.LeafFieldName);
        Assert.Equal(["address"], streetInfo.NestedFieldNames);

        var zipProp = addressType.FindProperty("Zip")!;
        Assert.Equal("shipping_address_zip", zipProp.GetColumnName());
        var zipInfo = GetStructFieldInfo(zipProp);
        Assert.NotNull(zipInfo);
        Assert.Equal("Shipping", zipInfo.StructColumnName);
        Assert.Equal("zip", zipInfo.LeafFieldName);
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
        // Column name is still set by the convention (not overridden by HasStructField).
        Assert.Equal("location_city", cityProp.GetColumnName());
        var info = GetStructFieldInfo(cityProp);
        Assert.NotNull(info);
        Assert.Equal("CustomerLocation", info.StructColumnName);
        // LeafFieldName is set by the convention since HasStructField didn't provide one.
        Assert.Equal("city", info.LeafFieldName);
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
        Assert.Equal("location_country", countryProp.GetColumnName());
        var info = GetStructFieldInfo(countryProp);
        Assert.NotNull(info);
        Assert.Equal("country", info.LeafFieldName);
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

        Assert.Equal("shipping_address_street", streetProp.GetColumnName());
        var info = GetStructFieldInfo(streetProp);
        Assert.NotNull(info);
        Assert.Equal("ShippingInfo", info.StructColumnName);
        Assert.Equal("street", info.LeafFieldName);
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
        Assert.Equal("structMapped_city", structCityProp.GetColumnName());
        var structInfo = GetStructFieldInfo(structCityProp);
        Assert.NotNull(structInfo);
        Assert.Equal("StructMapped", structInfo.StructColumnName);
        Assert.Equal("city", structInfo.LeafFieldName);

        // NotStructMapped.City -> should NOT have struct annotation
        var notStructCityProp = GetComplexScalar(model, typeof(EntityWithMixedComplexProperties),
            "NotStructMapped", "City");
        Assert.Null(GetStructFieldInfo(notStructCityProp));
        // EF Core defaults column name to property name — the convention didn't override it.
        Assert.Equal("City", notStructCityProp.GetColumnName());
    }

    // ─── Models for deeply nested subquery struct access test ──────────

    private sealed class NestedCustomer
    {
        public int Id { get; set; }
        [UseStructMapping]
        public required NestedLocation Location { get; set; }
        public List<NestedOrder> Orders { get; set; } = [];
    }

    private sealed class NestedOrder
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        [UseStructMapping]
        public required NestedShipping Shipping { get; set; }
        public NestedCustomer? Customer { get; set; }
    }

    private sealed class NestedLocation
    {
        public required string City { get; set; }
        public required string Country { get; set; }
    }

    private sealed class NestedShipping
    {
        public double Cost { get; set; }
        public required string Method { get; set; }
        public required NestedAddress Address { get; set; }
    }

    private sealed class NestedAddress
    {
        public required string Zip { get; set; }
    }

    private sealed class NestedQueryContext(DbContextOptions<NestedQueryContext> options)
        : DbContext(options)
    {
        public DbSet<NestedCustomer> Customers => Set<NestedCustomer>();
        public DbSet<NestedOrder> Orders => Set<NestedOrder>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NestedCustomer>(e =>
            {
                e.ToTable("Customers");
                e.ComplexProperty(c => c.Location);
                e.HasMany(c => c.Orders)
                    .WithOne(o => o.Customer)
                    .HasForeignKey(o => o.CustomerId);
            });

            modelBuilder.Entity<NestedOrder>(e =>
            {
                e.ToTable("Orders");
                e.ComplexProperty(o => o.Shipping);
            });
        }
    }

    [Fact]
    public void Deeply_nested_subquery_with_struct_fields_does_not_throw_binder_error()
    {
        // Exercises struct field access at 3+ levels of nesting. Previously:
        // 1. Struct access was emitted on subquery aliases (causing DuckDB
        //    "Referenced table not found" binder errors) — fixed by
        //    IsDirectTableColumn() guard in VisitColumn.
        // 2. Projection alias AS clauses were skipped because
        //    column.Name == projection.Alias matched, but the actual DuckDB
        //    SQL output name (leaf field) differed — fixed by overriding
        //    VisitProjection to force AS for struct columns.

        var builder = new DbContextOptionsBuilder<NestedQueryContext>()
            .UseDuckDB("DataSource=:memory:")
            .EnableServiceProviderCaching(false);
        using var context = new NestedQueryContext(builder.Options);

        // Use the underlying DuckDB connection directly for DDL/DML to avoid
        // string.Format issues with DuckDB struct literals ({...}).
        var conn = context.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            conn.Open();
        }
        using var cmd = conn.CreateCommand();

        cmd.CommandText = """
            CREATE TABLE Customers (
                Id INTEGER,
                Location STRUCT(City VARCHAR, Country VARCHAR)
            )
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            CREATE TABLE Orders (
                Id INTEGER,
                CustomerId INTEGER,
                Shipping STRUCT(Cost DOUBLE, Method VARCHAR, Address STRUCT(Zip VARCHAR))
            )
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            INSERT INTO Customers VALUES
            (1, {City: 'NYC', Country: 'US'}),
            (2, {City: 'London', Country: 'UK'})
            """;
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            INSERT INTO Orders VALUES
            (10, 1, {Cost: 9.99, Method: 'air', Address: {Zip: '10001'}}),
            (11, 1, {Cost: 4.50, Method: 'ground', Address: {Zip: '10002'}}),
            (20, 2, {Cost: 12.00, Method: 'air', Address: {Zip: 'SW1A'}})
            """;
        cmd.ExecuteNonQuery();

        var query = context.Customers
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                City = c.Location.City,
                Country = c.Location.Country,
                Orders = c.Orders.OrderBy(o => o.Id).Select(o => new
                {
                    o.Id,
                    Cost = o.Shipping.Cost,
                    Method = o.Shipping.Method,
                    Zip = o.Shipping.Address.Zip,
                    DeeperOrders = o.Customer!.Orders.OrderBy(o2 => o2.Id).Select(o2 => new
                    {
                        o2.Id,
                        o2.Shipping.Cost,
                        CustomerCity = o2.Customer!.Location.City
                    }).ToList()
                }).ToList()
            });

        var results = query.ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("NYC", results[0].City);
        Assert.Equal("US", results[0].Country);
        Assert.Equal(2, results[0].Orders.Count);

        Assert.Equal(10, results[0].Orders[0].Id);
        Assert.Equal(9.99, results[0].Orders[0].Cost);
        Assert.Equal("air", results[0].Orders[0].Method);
        Assert.Equal("10001", results[0].Orders[0].Zip);
        Assert.Equal(2, results[0].Orders[0].DeeperOrders.Count);
        Assert.Equal(10, results[0].Orders[0].DeeperOrders[0].Id);
        Assert.Equal("NYC", results[0].Orders[0].DeeperOrders[0].CustomerCity);
        Assert.Equal(11, results[0].Orders[0].DeeperOrders[1].Id);
        Assert.Equal("NYC", results[0].Orders[0].DeeperOrders[1].CustomerCity);

        Assert.Equal(11, results[0].Orders[1].Id);
        Assert.Equal("ground", results[0].Orders[1].Method);

        Assert.Equal(2, results[1].Id);
        Assert.Equal("London", results[1].City);
        Assert.Single(results[1].Orders);
        Assert.Equal(20, results[1].Orders[0].Id);
        Assert.Equal("SW1A", results[1].Orders[0].Zip);
        Assert.Single(results[1].Orders[0].DeeperOrders);
        Assert.Equal(20, results[1].Orders[0].DeeperOrders[0].Id);
        Assert.Equal("London", results[1].Orders[0].DeeperOrders[0].CustomerCity);
    }

    [Fact]
    public void Same_leaf_name_under_different_struct_columns_gets_unique_ef_column_names()
    {
        // Billing.City and Shipping.City share the same CLR property name "City".
        // The convention must assign unique EF column names (e.g. "billing_city" and
        // "shipping_city") so EF's relational model treats them as distinct columns,
        // while both get the correct LeafFieldName "city" for DuckDB SQL generation.
        var model = BuildModel<EntityWithCollidingStructLeaves>(mb =>
        {
            mb.Entity<EntityWithCollidingStructLeaves>(e =>
            {
                e.ComplexProperty(c => c.Billing);
                e.ComplexProperty(c => c.Shipping);
            });
        });

        var billingCity = GetComplexScalar(model, typeof(EntityWithCollidingStructLeaves),
            "Billing", "City");
        var shippingCity = GetComplexScalar(model, typeof(EntityWithCollidingStructLeaves),
            "Shipping", "City");

        // Distinct EF column names — no collision.
        Assert.Equal("billing_city", billingCity.GetColumnName());
        Assert.Equal("shipping_city", shippingCity.GetColumnName());

        // Both correctly report the same DuckDB leaf field name.
        var billingInfo = GetStructFieldInfo(billingCity);
        Assert.NotNull(billingInfo);
        Assert.Equal("Billing", billingInfo.StructColumnName);
        Assert.Equal("city", billingInfo.LeafFieldName);

        var shippingInfo = GetStructFieldInfo(shippingCity);
        Assert.NotNull(shippingInfo);
        Assert.Equal("Shipping", shippingInfo.StructColumnName);
        Assert.Equal("city", shippingInfo.LeafFieldName);
    }
}