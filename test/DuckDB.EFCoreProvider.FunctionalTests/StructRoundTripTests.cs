using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     End-to-end integration tests for DuckDB STRUCT column support: creates physical tables via
///     EnsureCreated, inserts via SaveChanges, and queries through EF Core LINQ to verify the data
///     round-trips correctly through the DDL consolidation + write-pipeline struct literal path.
/// </summary>
public class StructRoundTripTests : DuckDBTestBase
{
    private StructContext CreateContext()
        => new(FileOptions<StructContext>());

    [ConditionalFact]
    public void Struct_complex_property_inserts_and_reads_back()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Customer
            {
                Id = 1,
                Location = new Address { City = "NYC", Country = "US" }
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var customer = context.Set<Customer>().Single(x => x.Id == 1);
            Assert.Equal("NYC", customer.Location.City);
            Assert.Equal("US", customer.Location.Country);
        }
    }

    [ConditionalFact]
    public void Struct_sub_field_projection_works()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new Customer { Id = 1, Location = new Address { City = "NYC", Country = "US" } },
                new Customer { Id = 2, Location = new Address { City = "LDN", Country = "UK" } });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var cities = context.Set<Customer>()
                .Select(c => c.Location.City)
                .OrderBy(c => c)
                .ToList();
            Assert.Equal(["LDN", "NYC"], cities);
        }
    }

    [ConditionalFact]
    public void Struct_sub_field_filter_works()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new Customer { Id = 1, Location = new Address { City = "NYC", Country = "US" } },
                new Customer { Id = 2, Location = new Address { City = "LDN", Country = "UK" } },
                new Customer { Id = 3, Location = new Address { City = "LA", Country = "US" } });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var usCities = context.Set<Customer>()
                .Where(c => c.Location.Country == "US")
                .Select(c => c.Location.City)
                .OrderBy(c => c)
                .ToList();
            Assert.Equal(["LA", "NYC"], usCities);
        }
    }

    [ConditionalFact]
    public void Struct_sub_field_update_works()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Customer
            {
                Id = 1,
                Location = new Address { City = "NYC", Country = "US" }
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var customer = context.Set<Customer>().Single(x => x.Id == 1);
            customer.Location.City = "Boston";
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var customer = context.Set<Customer>().Single(x => x.Id == 1);
            Assert.Equal("Boston", customer.Location.City);
            Assert.Equal("US", customer.Location.Country);
        }
    }

    [ConditionalFact]
    public void Multiple_struct_columns_round_trip()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Account
            {
                Id = 1,
                Billing = new Address { City = "Seattle", Country = "US" },
                Shipping = new Address { City = "Portland", Country = "US" }
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var account = context.Set<Account>().Single(x => x.Id == 1);
            Assert.Equal("Seattle", account.Billing.City);
            Assert.Equal("Portland", account.Shipping.City);
        }
    }

    [ConditionalFact]
    public void Nested_struct_round_trips()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Order
            {
                Id = 1,
                Shipping = new Shipping
                {
                    Method = "Express",
                    Address = new ShippingAddress
                    {
                        Street = "123 Main St",
                        Zip = "98001"
                    }
                }
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var order = context.Set<Order>().Single(x => x.Id == 1);
            Assert.Equal("Express", order.Shipping.Method);
            Assert.Equal("123 Main St", order.Shipping.Address.Street);
            Assert.Equal("98001", order.Shipping.Address.Zip);
        }
    }

    [ConditionalFact]
    public void Nested_struct_sub_field_update_works()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Order
            {
                Id = 1,
                Shipping = new Shipping
                {
                    Method = "Express",
                    Address = new ShippingAddress
                    {
                        Street = "123 Main St",
                        Zip = "98001"
                    }
                }
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var order = context.Set<Order>().Single(x => x.Id == 1);
            order.Shipping.Address.Street = "456 Oak Ave";
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var order = context.Set<Order>().Single(x => x.Id == 1);
            Assert.Equal("Express", order.Shipping.Method);
            Assert.Equal("456 Oak Ave", order.Shipping.Address.Street);
            Assert.Equal("98001", order.Shipping.Address.Zip);
        }
    }

    [ConditionalFact]
    public void Non_struct_complex_property_still_round_trips()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Customer
            {
                Id = 1,
                Location = new Address { City = "NYC", Country = "US" },
                Contact = new ContactInfo { Email = "test@example.com", Phone = "555-1234" }
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var customer = context.Set<Customer>().Single(x => x.Id == 1);
            Assert.Equal("test@example.com", customer.Contact!.Email);
            Assert.Equal("555-1234", customer.Contact!.Phone);
        }
    }

    [ConditionalFact]
    public void Struct_multiple_entities_round_trip()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new Customer { Id = 1, Location = new Address { City = "NYC", Country = "US" } },
                new Customer { Id = 2, Location = new Address { City = "LDN", Country = "UK" } },
                new Customer { Id = 3, Location = new Address { City = "Tokyo", Country = "JP" } });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var all = context.Set<Customer>().OrderBy(c => c.Id).ToList();
            Assert.Equal(3, all.Count);
            Assert.Equal("NYC", all[0].Location.City);
            Assert.Equal("LDN", all[1].Location.City);
            Assert.Equal("Tokyo", all[2].Location.City);
        }
    }

    [ConditionalFact]
    public void Struct_orderby_sub_field_works()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new Customer { Id = 1, Location = new Address { City = "Zeta", Country = "US" } },
                new Customer { Id = 2, Location = new Address { City = "Alpha", Country = "US" } },
                new Customer { Id = 3, Location = new Address { City = "Mid", Country = "US" } });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var cities = context.Set<Customer>()
                .OrderBy(c => c.Location.City)
                .Select(c => c.Location.City)
                .ToList();
            Assert.Equal(["Alpha", "Mid", "Zeta"], cities);
        }
    }

    [ConditionalFact]
    public void Duplicate_leaf_names_projection_works()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new Account
            {
                Id = 1,
                Billing = new Address { City = "Seattle", Country = "US" },
                Shipping = new Address { City = "Portland", Country = "US" }
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var result = context.Set<Account>()
                            .Select(a => new { BillingCity = a.Billing.City, ShippingCity = a.Shipping.City })
                            .Single();
                        Assert.Equal("Seattle", result.BillingCity);
                        Assert.Equal("Portland", result.ShippingCity);
        }
    }

    [ConditionalFact]
    public void Explicit_naming_round_trips()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new LabeledItem
            {
                Id = 1,
                Tags = new Tag { Category = "electronics", Label = "gadget" }
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var item = context.Set<LabeledItem>().Single(x => x.Id == 1);
            Assert.Equal("electronics", item.Tags.Category);
            Assert.Equal("gadget", item.Tags.Label);
        }
    }

    [ConditionalFact]
    public void Explicit_naming_filter_works()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new LabeledItem { Id = 1, Tags = new Tag { Category = "books", Label = "novel" } },
                new LabeledItem { Id = 2, Tags = new Tag { Category = "electronics", Label = "gadget" } });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var items = context.Set<LabeledItem>()
                .Where(i => i.Tags.Category == "electronics")
                .Select(i => i.Tags.Label)
                .ToList();
            Assert.Equal(["gadget"], items);
        }
    }

    [ConditionalFact]
    public void Struct_sub_field_subquery_works()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new Customer { Id = 1, Location = new Address { City = "NYC", Country = "US" } },
                new Customer { Id = 2, Location = new Address { City = "LDN", Country = "UK" } },
                new Customer { Id = 3, Location = new Address { City = "LA", Country = "US" } },
                new Customer { Id = 4, Location = new Address { City = "Paris", Country = "FR" } });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            // Subquery: find cities of customers in US (the subquery should flatten struct
            // field access into a regular column projection).
            var usCities = context.Set<Customer>()
                .Where(c => context.Set<Customer>()
                    .Where(c2 => c2.Location.Country == "US")
                    .Select(c2 => c2.Location.City)
                    .Contains(c.Location.City))
                .Select(c => c.Location.City)
                .OrderBy(c => c)
                .ToList();
            // NYC and LA are US cities; both are in the US subquery result.
            Assert.Equal(["LA", "NYC"], usCities);
        }
    }

    [ConditionalFact]
    public void Struct_join_works()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new Customer { Id = 1, Location = new Address { City = "NYC", Country = "US" } },
                new Customer { Id = 2, Location = new Address { City = "LDN", Country = "UK" } });
            context.AddRange(
                new Order { Id = 101, CustomerId = 1, Shipping = new Shipping { Method = "Express", Address = new ShippingAddress { Street = "5th Ave", Zip = "10001" } } },
                new Order { Id = 102, CustomerId = 2, Shipping = new Shipping { Method = "Standard", Address = new ShippingAddress { Street = "Oxford St", Zip = "SW1" } } });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var results = (from c in context.Set<Customer>()
                           join o in context.Set<Order>() on c.Id equals o.CustomerId
                           orderby o.Id
                           select new { c.Location.City, o.Shipping.Method }).ToList();
            Assert.Equal(2, results.Count);
            Assert.Equal("NYC", results[0].City);
            Assert.Equal("Express", results[0].Method);
            Assert.Equal("LDN", results[1].City);
            Assert.Equal("Standard", results[1].Method);
        }
    }

    [ConditionalFact]
    public void Bulk_insert_with_struct_throws()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var customers = new[]
        {
            new Customer { Id = 1, Location = new Address { City = "NYC", Country = "US" } },
            new Customer { Id = 2, Location = new Address { City = "LDN", Country = "UK" } }
        };

        Assert.Throws<NotSupportedException>(() =>
            context.BulkInsert(customers));
    }

    [ConditionalFact]
    public void Upsert_with_struct_throws()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var customer = new Customer { Id = 1, Location = new Address { City = "NYC", Country = "US" } };

        Assert.Throws<NotSupportedException>(() =>
            context.Upsert(new[] { customer }));
    }

    // ─── Model ──────────────────────────────────────────────────────

    private sealed class StructContext(DbContextOptions<StructContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(e =>
            {
                e.Property(p => p.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Location);
                e.ComplexProperty(c => c.Contact);
            });

            modelBuilder.Entity<Account>(e =>
            {
                e.Property(p => p.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Billing);
                e.ComplexProperty(c => c.Shipping);
            });

            modelBuilder.Entity<Order>(e =>
            {
                e.Property(p => p.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Shipping);
            });

            modelBuilder.Entity<LabeledItem>(e =>
            {
        e.Property(p => p.Id).ValueGeneratedNever();
        e.ComplexProperty(c => c.Tags, b =>
        {
            b.Property(t => t.Category).HasColumnName("cat").HasStructField("category");
            b.Property(t => t.Label).HasColumnName("lbl").HasStructField("label");
        });
            });
        }
    }

    private sealed class Customer
    {
        public int Id { get; set; }
        [UseStructMapping]
        public required Address Location { get; set; }
        public ContactInfo? Contact { get; set; }
    }

    private sealed class Address
    {
        public required string City { get; set; }
        public required string Country { get; set; }
    }

    private sealed class ContactInfo
    {
        public required string Email { get; set; }
        public required string Phone { get; set; }
    }

    private sealed class Account
    {
        public int Id { get; set; }
        [UseStructMapping]
        public required Address Billing { get; set; }
        [UseStructMapping]
        public required Address Shipping { get; set; }
    }

    private sealed class Order
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        [UseStructMapping]
        public required Shipping Shipping { get; set; }
    }

    private sealed class Shipping
    {
        public required string Method { get; set; }
        [UseStructMapping]
        public required ShippingAddress Address { get; set; }
    }

    private sealed class ShippingAddress
    {
        public required string Street { get; set; }
        public required string Zip { get; set; }
    }

    private sealed class LabeledItem
    {
        public int Id { get; set; }
        [UseStructMapping]
        public required Tag Tags { get; set; }
    }

    private sealed class Tag
    {
        public required string Category { get; set; }
        public required string Label { get; set; }
    }
}