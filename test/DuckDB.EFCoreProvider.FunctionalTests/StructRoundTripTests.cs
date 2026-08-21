using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.NET.Data;
using System.Text.Json;
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

    private StructContext CreateBatchingContext()
        => new(FileOptions<StructContext>(duckdb => duckdb.EnableBulkUpdateBatching()));

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
    public void Struct_complex_property_materializes_mixed_leaf_types()
    {
        var timestamp = new DateTime(2026, 8, 17, 13, 25, 42, DateTimeKind.Unspecified);
        var timestampWithTimeZone = new DateTimeOffset(timestamp, TimeSpan.Zero);
        var identifier = Guid.NewGuid();

        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new MixedTypeEntity
            {
                Id = 1,
                Values = new MixedTypeValues
                {
                    Timestamp = timestamp,
                    TimestampWithTimeZone = timestampWithTimeZone,
                    Payload = [1, 2, 3, 4],
                    Identifier = identifier,
                    Amount = 123.45m,
                    Date = new DateOnly(2026, 8, 17),
                    Time = new TimeOnly(13, 25, 42),
                    Numbers = [4, 8, 15]
                }
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var values = context.Set<MixedTypeEntity>()
                .Select(entity => entity.Values)
                .Single();

            Assert.Equal(timestamp, values.Timestamp);
            Assert.Equal(timestampWithTimeZone, values.TimestampWithTimeZone);
            Assert.Equal([1, 2, 3, 4], values.Payload);
            Assert.Equal(identifier, values.Identifier);
            Assert.Equal(123.45m, values.Amount);
            Assert.Equal(new DateOnly(2026, 8, 17), values.Date);
            Assert.Equal(new TimeOnly(13, 25, 42), values.Time);
            Assert.Equal([4, 8, 15], values.Numbers);
        }
    }

    [ConditionalFact]
    public void Struct_complex_property_materializes_json_leaf_types()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("""
            INSERT INTO "JsonTypeEntity" ("Id", "Details")
            VALUES (
                1,
                STRUCT_PACK(
                    Document := '{{"name":"document"}}'::JSON,
                    Element := '{{"name":"element"}}'::JSON))
            """);

        var values = context.Set<JsonTypeEntity>()
            .Select(entity => entity.Details)
            .Single();

        Assert.Equal("document", values.Document.RootElement.GetProperty("name").GetString());
        Assert.Equal("element", values.Element.GetProperty("name").GetString());
    }

    [ConditionalFact]
    public void Struct_complex_property_preserves_same_type_value_converter()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("""
            INSERT INTO "ConvertedStringEntity" ("Id", "Details")
            VALUES (1, STRUCT_PACK(City := 'db:NYC'))
            """);

        var values = context.Set<ConvertedStringEntity>()
            .Select(entity => entity.Details)
            .Single();

        Assert.Equal("NYC", values.City);
    }

    [ConditionalFact]
    public void Struct_complex_property_returns_defaults_for_null_customized_leaves()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("""
            INSERT INTO "JsonTypeEntity" ("Id", "Details")
            VALUES (
                1,
                STRUCT_PACK(
                    Document := NULL::JSON,
                    Element := NULL::JSON))
            """);

        var values = context.Set<JsonTypeEntity>()
            .Select(entity => entity.Details)
            .Single();

        Assert.Null(values.Document);
        Assert.Equal(JsonValueKind.Undefined, values.Element.ValueKind);
    }

    [ConditionalFact]
    public void Optional_struct_root_null_round_trips_and_queries_as_null()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new OptionalRootEntity { Id = 1, Location = null });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            // The physical root is nullable and the write emits SQL NULL, so the whole-complex
            // null check must match the saved row.
            var count = context.Set<OptionalRootEntity>().Count(c => c.Location == null);
            Assert.Equal(1, count);

            var entity = context.Set<OptionalRootEntity>().Single(c => c.Id == 1);
            Assert.Null(entity.Location);
        }
    }

    [ConditionalFact]
    public void Struct_null_check_targets_configured_root_not_overridden_leaf_root()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var sql = context.Set<OverriddenRootEntity>()
            .Where(c => c.Location == null)
            .ToQueryString();

        // The null check must target the configured "Location" root, not the overridden leaf root.
        Assert.Contains("\"Location\" IS NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"CustomerLocation\" IS NULL", sql, StringComparison.OrdinalIgnoreCase);
    }

    [ConditionalFact]
    public void EnsureCreated_uses_one_physical_struct_column()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.Database.OpenConnection();

        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT column_name, data_type
            FROM duckdb_columns()
            WHERE database_name = current_database() AND table_name = 'Customer'
            ORDER BY column_index
            """;
        using var reader = command.ExecuteReader();
        var columns = new List<(string Name, string StoreType)>();
        while (reader.Read())
        {
            columns.Add((reader.GetString(0), reader.GetString(1)));
        }

        Assert.Contains(
            columns,
            column => column.Name == "Location"
                && column.StoreType.StartsWith("STRUCT", StringComparison.Ordinal));
        Assert.DoesNotContain(columns, column => column.Name is "location_city" or "location_country");
    }

    [ConditionalFact]
    public void Struct_query_has_exact_physical_sql_shape()
    {
        using var context = CreateContext();
        var sql = context.Set<Customer>()
            .Where(customer => customer.Location.Country == "US")
            .Select(customer => customer.Location.City)
            .ToQueryString();

        Assert.Equal(
            """
            SELECT c."Location".city AS location_city
            FROM "Customer" AS c
            WHERE c."Location".country = 'US'
            """,
            sql);
    }

    [ConditionalFact]
    public void Correlated_struct_reference_uses_physical_field_path()
    {
        using var context = CreateContext();
        var sql = context.Set<Customer>()
            .Where(customer => context.Set<Customer>().Any(other =>
                other.Id != customer.Id
                && other.Location.City == customer.Location.City))
            .ToQueryString();

        Assert.DoesNotContain(".location_city", sql, StringComparison.Ordinal);
        Assert.True(sql.Split("\"Location\".city", StringSplitOptions.None).Length - 1 >= 2);
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
    public void Struct_partial_null_leaf_update_preserves_unchanged_siblings()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new NullableLeafCustomer
            {
                Id = 1,
                Location = new NullableLeafAddress { City = "NYC", Country = "US" }
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var customer = context.Set<NullableLeafCustomer>().Single();
            customer.Location.City = null;
            context.SaveChanges();
        }

        using var verificationContext = CreateContext();
        var location = verificationContext.Set<NullableLeafCustomer>().Single().Location;
        Assert.Null(location.City);
        Assert.Equal("US", location.Country);
    }

    [ConditionalFact]
    public void Struct_insert_distinguishes_null_root_from_present_all_null_root()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();
        context.AddRange(
            new OptionalCustomer { Id = 1, Location = null },
            new OptionalCustomer { Id = 2, Location = new OptionalAddress { Marker = "present" } });
        context.SaveChanges();
        context.Database.OpenConnection();

        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT "Location" IS NULL, "Location".city IS NULL
            FROM "OptionalCustomer"
            ORDER BY "Id"
            """;
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.Read());
        Assert.False(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
    }

    [ConditionalFact]
    public void Struct_bulk_update_separates_null_root_state()
    {
        using (var context = CreateBatchingContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                new OptionalCustomer
                {
                    Id = 1,
                    Location = new OptionalAddress { Marker = "present", City = "NYC", Country = "US" }
                },
                new OptionalCustomer
                {
                    Id = 2,
                    Location = new OptionalAddress { Marker = "present", City = "LDN", Country = "UK" }
                });
            context.SaveChanges();
        }

        using (var context = CreateBatchingContext())
        {
            var customers = context.Set<OptionalCustomer>().OrderBy(customer => customer.Id).ToArray();
            customers[0].Location = new OptionalAddress { Marker = "present" };
            customers[1].Location = null;
            context.SaveChanges();
        }

        using var verificationContext = CreateContext();
        verificationContext.Database.OpenConnection();
        using var command = verificationContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT "Location" IS NULL, "Location".city IS NULL, "Location".country IS NULL
            FROM "OptionalCustomer"
            ORDER BY "Id"
            """;
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.False(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.Read());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
    }

    [ConditionalFact]
    public void Struct_bulk_update_batching_works()
    {
        using (var context = CreateBatchingContext())
        {
            context.Database.EnsureCreated();
            context.AddRange(
                Enumerable.Range(1, 10)
                    .Select(id => new Customer
                    {
                        Id = id,
                        Location = new Address { City = $"City-{id}", Country = "US" }
                    }));
            context.SaveChanges();
        }

        using (var context = CreateBatchingContext())
        {
            foreach (var customer in context.Set<Customer>().OrderBy(customer => customer.Id))
            {
                customer.Location.City = $"Updated-{customer.Id}";
            }

            context.SaveChanges();
        }

        using (var context = CreateBatchingContext())
        {
            var customers = context.Set<Customer>()
                .OrderBy(customer => customer.Id)
                .ToList();

            Assert.Equal(10, customers.Count);
            Assert.All(customers, customer =>
                Assert.Equal($"Updated-{customer.Id}", customer.Location.City));
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
    public void Struct_field_names_with_quotes_round_trip_and_update()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new EscapedNameItem
            {
                Id = 1,
                Details = new EscapedNameDetails { Value = "initial" }
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var item = context.Set<EscapedNameItem>()
                .Single(item => item.Details.Value == "initial");
            item.Details.Value = "updated";
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(
                "updated",
                context.Set<EscapedNameItem>()
                    .Select(item => item.Details.Value)
                    .Single());
        }
    }

    [ConditionalFact]
    public void Nested_struct_field_names_with_quotes_round_trip_and_update()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new EscapedNestedItem
            {
                Id = 1,
                Container = new EscapedNestedContainer
                {
                    Details = new EscapedNameDetails { Value = "initial" }
                }
            });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var item = context.Set<EscapedNestedItem>()
                .Single(item => item.Container.Details.Value == "initial");
            item.Container.Details.Value = "updated";
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(
                "updated",
                context.Set<EscapedNestedItem>()
                    .Select(item => item.Container.Details.Value)
                    .Single());
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
    public void Struct_set_operation_and_compiled_query_work()
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
                .Where(customer => customer.Location.Country == "US")
                .Select(customer => customer.Location.City)
                .Union(
                    context.Set<Customer>()
                        .Where(customer => customer.Location.Country == "UK")
                        .Select(customer => customer.Location.City))
                .OrderBy(city => city)
                .ToList();
            Assert.Equal(["LDN", "NYC"], cities);
        }

        var compiled = EF.CompileQuery(
            (StructContext context, int id) =>
                context.Set<Customer>().Single(customer => customer.Id == id).Location.City);
        using var compiledContext = CreateContext();
        Assert.Equal("NYC", compiled(compiledContext, 1));
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

        var exception = Assert.Throws<NotSupportedException>(() =>
            context.BulkInsert(customers));
        Assert.Equal(
            "Bulk insert into 'Customer' is not supported for entities with DuckDB STRUCT mappings. "
            + "Use SaveChanges instead.",
            exception.Message);
    }

    [ConditionalFact]
    public void Upsert_with_struct_throws()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var customer = new Customer { Id = 1, Location = new Address { City = "NYC", Country = "US" } };

        var exception = Assert.Throws<NotSupportedException>(() =>
            context.Upsert(new[] { customer }));
        Assert.Equal(
            "Upsert does not support entity 'Customer' because it contains struct-mapped complex properties. "
            + "STRUCT columns are consolidated at the physical layer and cannot be staged via the DuckDB Appender API. "
            + "Use SaveChanges instead.",
            exception.Message);
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

            modelBuilder.Entity<NullableLeafCustomer>(e =>
            {
                e.Property(p => p.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Location);
            });

            modelBuilder.Entity<OptionalCustomer>(e =>
            {
                e.Property(p => p.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Location);
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

            modelBuilder.Entity<MixedTypeEntity>(e =>
            {
                e.Property(p => p.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Values);
            });

            modelBuilder.Entity<JsonTypeEntity>(e =>
            {
                e.Property(p => p.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Details);
            });

            modelBuilder.Entity<ConvertedStringEntity>(e =>
            {
                e.Property(p => p.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Details, details =>
                {
                    details.Property(value => value.City)
                        .HasConversion(
                            value => "db:" + value,
                            value => value.Substring(3));
                });
            });

            modelBuilder.Entity<OptionalRootEntity>(e =>
            {
                e.Property(p => p.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Location, b => b.IsRequired(false));
            });

            modelBuilder.Entity<OverriddenRootEntity>(e =>
            {
                e.Property(p => p.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Location, b =>
                {
                    b.IsRequired(false);
                    b.Property(a => a.Country)
                        .HasColumnName("country")
                        .HasStructField("CustomerLocation")
                        .HasStructFieldName("country");
                });
            });

            modelBuilder.Entity<LabeledItem>(e =>
            {
                e.Property(p => p.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Tags, b =>
                {
                    b.Property(t => t.Category)
                        .HasColumnName("cat")
                        .HasStructField("category")
                        .HasStructFieldName("cat");
                    b.Property(t => t.Label)
                        .HasColumnName("lbl")
                        .HasStructField("label")
                        .HasStructFieldName("lbl");
                });
            });

            modelBuilder.Entity<EscapedNameItem>(entity =>
            {
                entity.Property(item => item.Id).ValueGeneratedNever();
                entity.ComplexProperty(item => item.Details, details =>
                    details.Property(detail => detail.Value)
                        .HasColumnName("customer's \"city\"")
                        .HasStructFieldName("customer's \"city\""));
            });

            modelBuilder.Entity<EscapedNestedItem>(entity =>
            {
                entity.Property(item => item.Id).ValueGeneratedNever();
                entity.ComplexProperty(item => item.Container, container =>
                    container.ComplexProperty(value => value.Details, details =>
                        details.Property(detail => detail.Value)
                            .HasColumnName("select value")
                            .HasStructField("Container", "customer's \"details\"")
                            .HasStructFieldName("select value")));
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

    private sealed class NullableLeafCustomer
    {
        public int Id { get; set; }
        [UseStructMapping]
        public required NullableLeafAddress Location { get; set; }
    }

    private sealed class NullableLeafAddress
    {
        public string? City { get; set; }
        public string? Country { get; set; }
    }

    private sealed class OptionalCustomer
    {
        public int Id { get; set; }
        [UseStructMapping]
        public OptionalAddress? Location { get; set; }
    }

    private sealed class OptionalAddress
    {
        public required string Marker { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
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

    private sealed class MixedTypeEntity
    {
        public int Id { get; set; }

        [UseStructMapping]
        public required MixedTypeValues Values { get; set; }
    }

    private sealed class MixedTypeValues
    {
        public DateTime Timestamp { get; set; }
        public DateTimeOffset TimestampWithTimeZone { get; set; }
        public byte[] Payload { get; set; } = [];
        public Guid Identifier { get; set; }
        public decimal Amount { get; set; }
        public DateOnly Date { get; set; }
        public TimeOnly Time { get; set; }
        public List<int> Numbers { get; set; } = [];
    }

    private sealed class JsonTypeEntity
    {
        public int Id { get; set; }

        [UseStructMapping]
        public required JsonTypeValues Details { get; set; }
    }

    private sealed class JsonTypeValues
    {
        public JsonDocument Document { get; set; } = null!;
        public JsonElement Element { get; set; }
    }

    private sealed class ConvertedStringEntity
    {
        public int Id { get; set; }

        [UseStructMapping]
        public required ConvertedStringValues Details { get; set; }
    }

    private sealed class ConvertedStringValues
    {
        public string City { get; set; } = null!;
    }

    private sealed class OptionalRootEntity
    {
        public int Id { get; set; }

        [UseStructMapping]
        public OptionalRootAddress? Location { get; set; }
    }

    private sealed class OptionalRootAddress
    {
        public string City { get; set; } = null!;
        public string Country { get; set; } = null!;
    }

    private sealed class OverriddenRootEntity
    {
        public int Id { get; set; }

        [UseStructMapping]
        public OverriddenAddress? Location { get; set; }
    }

    private sealed class OverriddenAddress
    {
        public string City { get; set; } = null!;
        public string Country { get; set; } = null!;
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

    private sealed class EscapedNameItem
    {
        public int Id { get; set; }

        [UseStructMapping]
        public required EscapedNameDetails Details { get; set; }
    }

    private sealed class EscapedNameDetails
    {
        public required string Value { get; set; }
    }

    private sealed class EscapedNestedItem
    {
        public int Id { get; set; }

        [UseStructMapping]
        public required EscapedNestedContainer Container { get; set; }
    }

    private sealed class EscapedNestedContainer
    {
        public required EscapedNameDetails Details { get; set; }
    }
}
