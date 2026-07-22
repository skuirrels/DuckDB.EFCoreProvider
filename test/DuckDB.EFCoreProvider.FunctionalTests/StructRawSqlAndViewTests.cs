using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Verifies that struct-mapped complex properties materialize correctly from unmapped raw SQL and from
///     explicitly created database views that return DuckDB <c>STRUCT</c> columns.
/// </summary>
public sealed class StructRawSqlAndViewTests : DuckDBTestBase
{
    [ConditionalFact]
    public void FromSqlRaw_struct_column_materializes_sub_fields()
    {
        using var context = CreateFromSqlContext();
        context.Database.EnsureCreated();

        var query = context.Customers.FromSqlRaw(
            """
            SELECT 1 AS "Id", STRUCT_PACK(City := 'NYC', Country := 'US') AS "Location"
            """);

        var result = query.Single();
        Assert.Equal("NYC", result.Location.City);
        Assert.Equal("US", result.Location.Country);
    }

    [ConditionalFact]
    public void Mapped_view_with_struct_complex_property_is_rejected_at_model_build()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
        {
            using var context = CreateViewContext();
            context.Database.EnsureCreated();
        });

        Assert.Contains("view", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("struct-mapped", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private FromSqlContext CreateFromSqlContext()
    {
        var options = new DbContextOptionsBuilder<FromSqlContext>()
            .UseDuckDB($"DataSource={DbPath}")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new FromSqlContext(options);
    }

    private ViewContext CreateViewContext()
    {
        var options = new DbContextOptionsBuilder<ViewContext>()
            .UseDuckDB($"DataSource={DbPath}")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new ViewContext(options);
    }

    private sealed class FromSqlContext(DbContextOptions<FromSqlContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(e =>
            {
                e.ToTable("from_sql_customers");
                e.Property(c => c.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Location).UseStructMapping();
            });
        }
    }

    private sealed class ViewContext(DbContextOptions<ViewContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(e =>
            {
                e.ToView("ReadOnlyCustomers");
                e.Property(c => c.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Location).UseStructMapping();
            });
        }
    }

    private sealed class Customer
    {
        public int Id { get; set; }
        public required Address Location { get; set; }
    }

    private sealed class Address
    {
        public string City { get; set; } = null!;
        public string Country { get; set; } = null!;
    }
}
