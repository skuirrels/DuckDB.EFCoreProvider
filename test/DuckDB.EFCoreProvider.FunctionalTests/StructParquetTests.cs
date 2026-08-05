using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     End-to-end integration tests for DuckDB STRUCT columns backed by physical Parquet files.
///     The Parquet files contain actual nested STRUCT columns, and the EF Core read path uses
///     <c>read_parquet</c> via <see cref="DuckDBEntityTypeExtensions.FromParquet{TEntity}" />.
/// </summary>
public sealed class StructParquetTests : DuckDBTestBase
{
    [ConditionalFact]
    public void Struct_sub_field_projection_from_parquet()
    {
        var path = ParquetPath();
        try
        {
            WriteStructParquet(path, """
                CREATE TABLE t (Id INTEGER, Location STRUCT(city VARCHAR, country VARCHAR));
                INSERT INTO t VALUES
                    (1, {'city': 'NYC', 'country': 'US'}),
                    (2, {'city': 'LDN', 'country': 'UK'})
                """);

            using var context = CreateCustomerContext<ProjectionTag>(path);
            var cities = context.Customers
                .Select(c => c.Location.City)
                .OrderBy(c => c)
                .ToList();

            Assert.Equal(["LDN", "NYC"], cities);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [ConditionalFact]
    public void Struct_selective_projection_from_parquet_ignores_missing_fields()
    {
        var path = ParquetPath();
        try
        {
            WriteStructParquet(path, """
                CREATE TABLE t (Uid INTEGER, Attributes STRUCT(shorttext VARCHAR));
                INSERT INTO t VALUES
                    (1, {'shorttext': 'NYC'})
                """);

            using var context = CreateSparseCustomerContext<SelectiveProjectionTag>(path);
            var query = context.Systems
                .Select(system => new SparseSystem
                {
                    Uid = system.Uid,
                    Attributes = system.Attributes != null
                        ? new SparseAttributes { Shorttext = system.Attributes.Shorttext }
                        : null
                });
            var sql = query.ToQueryString();
            var result = query.Single();

            Assert.Equal(1, result.Uid);
            Assert.Equal("NYC", result.Attributes!.Shorttext);
            Assert.DoesNotContain("xedrresponsibility", sql, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [ConditionalFact]
    public void Struct_selective_projection_nested_nullable_complex_property_from_parquet()
    {
        var path = ParquetPath();
        try
        {
            WriteStructParquet(path, """
                CREATE TABLE t (
                    Uid INTEGER,
                    Attributes STRUCT(shorttext VARCHAR, details STRUCT(city VARCHAR))
                );
                INSERT INTO t VALUES
                    (1, {'shorttext': 'NYC', 'details': {'city': 'Paris'}})
                """);

            using var context = CreateNestedSparseCustomerContext(path);
            var query = context.Systems
                .Select(system => new NestedSparseSystem
                {
                    Uid = system.Uid,
                    Attributes = system.Attributes != null
                        ? new NestedSparseAttributes
                        {
                            Shorttext = system.Attributes.Shorttext,
                            Details = system.Attributes.Details != null
                                ? new NestedSparseDetails { City = system.Attributes.Details.City }
                                : null
                        }
                        : null
                });
            var sql = query.ToQueryString();
            var result = query.Single();

            Assert.Equal(1, result.Uid);
            Assert.Equal("NYC", result.Attributes!.Shorttext);
            Assert.Equal("Paris", result.Attributes!.Details!.City);
            Assert.DoesNotContain("xedrresponsibility", sql, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [ConditionalFact]
    public void Struct_selective_projection_via_attribute_from_parquet()
    {
        var path = ParquetPath();
        try
        {
            WriteStructParquet(path, """
                CREATE TABLE t (Uid INTEGER, Attributes STRUCT(shorttext VARCHAR));
                INSERT INTO t VALUES
                    (1, {'shorttext': 'NYC'})
                """);

            using var context = CreateAttributeSelectiveContext(path);
            var query = context.Systems
                .Select(system => new AttributeSelectiveSystem
                {
                    Uid = system.Uid,
                    Attributes = system.Attributes != null
                        ? new SparseAttributes { Shorttext = system.Attributes.Shorttext }
                        : null
                });
            var sql = query.ToQueryString();
            var result = query.Single();

            Assert.Equal(1, result.Uid);
            Assert.Equal("NYC", result.Attributes!.Shorttext);
            Assert.DoesNotContain("xedrresponsibility", sql, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [ConditionalFact]
    public void Struct_sub_field_filter_from_parquet()
    {
        var path = ParquetPath();
        try
        {
            WriteStructParquet(path, """
                CREATE TABLE t (Id INTEGER, Location STRUCT(city VARCHAR, country VARCHAR));
                INSERT INTO t VALUES
                    (1, {'city': 'NYC', 'country': 'US'}),
                    (2, {'city': 'LDN', 'country': 'UK'}),
                    (3, {'city': 'LA', 'country': 'US'})
                """);

            using var context = CreateCustomerContext<FilterTag>(path);
            var result = context.Customers
                .Where(c => c.Location.Country == "US")
                .Select(c => c.Location.City)
                .OrderBy(c => c)
                .ToList();

            Assert.Equal(["LA", "NYC"], result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [ConditionalFact]
    public void Struct_sub_field_order_by_from_parquet()
    {
        var path = ParquetPath();
        try
        {
            WriteStructParquet(path, """
                CREATE TABLE t (Id INTEGER, Location STRUCT(city VARCHAR, country VARCHAR));
                INSERT INTO t VALUES
                    (1, {'city': 'Zeta', 'country': 'US'}),
                    (2, {'city': 'Alpha', 'country': 'US'}),
                    (3, {'city': 'Mid', 'country': 'US'})
                """);

            using var context = CreateCustomerContext<OrderByTag>(path);
            var result = context.Customers
                .OrderBy(c => c.Location.City)
                .Select(c => c.Id)
                .ToList();

            Assert.Equal([2, 3, 1], result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [ConditionalFact]
    public void Duplicate_leaf_names_from_parquet_projection()
    {
        var path = ParquetPath();
        try
        {
            WriteStructParquet(path, """
                CREATE TABLE t (
                    Id INTEGER,
                    Billing STRUCT(city VARCHAR, country VARCHAR),
                    Shipping STRUCT(city VARCHAR, country VARCHAR)
                );
                INSERT INTO t VALUES
                    (1, {'city': 'Seattle', 'country': 'US'}, {'city': 'Portland', 'country': 'US'}),
                    (2, {'city': 'Austin', 'country': 'US'}, {'city': 'Denver', 'country': 'US'})
                """);

            using var context = CreateAccountContext<DuplicateLeavesTag>(path);
            var result = context.Accounts
                .Select(a => new { Billing = a.Billing.City, Shipping = a.Shipping.City })
                .OrderBy(a => a.Billing)
                .ToList();

            Assert.Equal(2, result.Count);
            Assert.Equal(("Austin", "Denver"), (result[0].Billing, result[0].Shipping));
            Assert.Equal(("Seattle", "Portland"), (result[1].Billing, result[1].Shipping));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [ConditionalFact]
    public void Struct_join_between_parquet_and_table()
    {
        var path = ParquetPath();
        try
        {
            WriteStructParquet(path, """
                CREATE TABLE t (Id INTEGER, Location STRUCT(city VARCHAR, country VARCHAR));
                INSERT INTO t VALUES
                    (1, {'city': 'NYC', 'country': 'US'}),
                    (2, {'city': 'LDN', 'country': 'UK'})
                """);

            using var context = CreateJoinContext<JoinTag>(path);
            context.Database.EnsureCreated();
            context.Orders.AddRange(
                new Order { Id = 101, CustomerId = 1, Method = "air" },
                new Order { Id = 102, CustomerId = 2, Method = "ground" });
            context.SaveChanges();

            var results = (from c in context.Customers
                           join o in context.Orders on c.Id equals o.CustomerId
                           orderby o.Id
                           select new { c.Location.City, o.Method })
                .ToList();

            Assert.Equal(2, results.Count);
            Assert.Equal("NYC", results[0].City);
            Assert.Equal("air", results[0].Method);
            Assert.Equal("LDN", results[1].City);
            Assert.Equal("ground", results[1].Method);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [ConditionalFact]
    public void Explicit_naming_from_parquet_round_trips()
    {
        var path = ParquetPath();
        try
        {
            WriteStructParquet(path, """
                CREATE TABLE t (Id INTEGER, Location STRUCT(city_name VARCHAR, country VARCHAR));
                INSERT INTO t VALUES
                    (1, {'city_name': 'NYC', 'country': 'US'}),
                    (2, {'city_name': 'LDN', 'country': 'UK'})
                """);

            using var context = CreateExplicitNamingContext<ExplicitNamingTag>(path);
            var customer = context.Customers.Single(c => c.Location.City == "LDN");

            Assert.Equal("LDN", customer.Location.City);
            Assert.Equal("UK", customer.Location.Country);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [ConditionalFact]
    public void Struct_selective_projection_where_presence_check_rewritten_from_parquet()
    {
        var path = ParquetPath();
        try
        {
            WriteStructParquet(path, """
                CREATE TABLE t (Uid INTEGER, Attributes STRUCT(shorttext VARCHAR));
                INSERT INTO t VALUES
                    (1, {'shorttext': 'NYC'})
                """);

            using var context = CreateSparseCustomerContext<PresenceCheckTag>(path);
            var query = context.Systems
                .Where(system => system.Attributes != null)
                .Select(system => system.Attributes!.Shorttext);
            var sql = query.ToQueryString();
            var result = query.Single();

            Assert.Equal("NYC", result);
            Assert.DoesNotContain("xedrresponsibility", sql, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [ConditionalFact]
    public void Struct_selective_projection_where_null_presence_check_rewritten_from_parquet()
    {
        var path = ParquetPath();
        try
        {
            WriteStructParquet(path, """
                CREATE TABLE t (Uid INTEGER, Attributes STRUCT(shorttext VARCHAR));
                INSERT INTO t VALUES
                    (1, {'shorttext': 'NYC'})
                """);

            using var context = CreateSparseCustomerContext<NullPresenceCheckTag>(path);
            var query = context.Systems
                .Where(system => system.Attributes == null)
                .Select(system => system.Attributes!.Shorttext);
            var sql = query.ToQueryString();
            var result = query.ToList();

            Assert.Empty(result);
            Assert.DoesNotContain("xedrresponsibility", sql, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [ConditionalFact]
    public void Struct_selective_projection_presence_check_uses_all_projected_fields_from_parquet()
    {
        var path = ParquetPath();
        try
        {
            WriteStructParquet(path, """
                CREATE TABLE t (Uid INTEGER, Attributes STRUCT(shorttext VARCHAR, other VARCHAR));
                INSERT INTO t VALUES
                    (1, {'shorttext': NULL, 'other': 'Paris'}),
                    (2, {'shorttext': NULL, 'other': NULL})
                """);

            using var context = CreateTwoFieldSparseCustomerContext(path);

            // IS NOT NULL polarity: presence check is rebuilt as OR over every projected field,
            // so row 1 (only 'other' set) must materialize as non-null and row 2 as null.
            var notNullQuery = context.Systems
                .Select(system => new TwoFieldSparseSystem
                {
                    Uid = system.Uid,
                    Attributes = system.Attributes != null
                        ? new TwoFieldSparseAttributes
                        {
                            Shorttext = system.Attributes.Shorttext,
                            Other = system.Attributes.Other
                        }
                        : null
                })
                .OrderBy(system => system.Uid);
            var notNullSql = notNullQuery.ToQueryString();
            var notNullResults = notNullQuery.ToList();

            Assert.NotNull(notNullResults[0].Attributes);
            Assert.Equal("Paris", notNullResults[0].Attributes!.Other);
            Assert.Null(notNullResults[1].Attributes);
            Assert.DoesNotContain("xedrresponsibility", notNullSql, StringComparison.OrdinalIgnoreCase);

            // IS NULL polarity: presence check is rebuilt as AND over every projected field.
            var isNullResults = context.Systems
                .OrderBy(system => system.Uid)
                .Select(system => system.Attributes == null
                    ? null
                    : new TwoFieldSparseAttributes
                    {
                        Shorttext = system.Attributes.Shorttext,
                        Other = system.Attributes.Other
                    })
                .ToList();

            Assert.NotNull(isNullResults[0]);
            Assert.Equal("Paris", isNullResults[0]!.Other);
            Assert.Null(isNullResults[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string ParquetPath()
        => Path.Combine(Path.GetTempPath(), $"struct_parquet_{Guid.NewGuid():N}.parquet");

    private static void WriteStructParquet(string path, string setupSql)
    {
        using var connection = new DuckDBConnection("DataSource=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        var escapedPath = path.Replace("\\", "\\\\").Replace("'", "''");
        command.CommandText = $"{setupSql.Trim().TrimEnd(';')}; COPY (SELECT * FROM t) TO '{escapedPath}' (FORMAT PARQUET);";
        command.ExecuteNonQuery();
    }

    private CustomerContext<TTag> CreateCustomerContext<TTag>(string parquetPath)
        where TTag : class
    {
        var options = new DbContextOptionsBuilder<CustomerContext<TTag>>()
            .UseDuckDB($"DataSource={DbPath}")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new CustomerContext<TTag>(options, parquetPath);
    }

    private AccountContext<TTag> CreateAccountContext<TTag>(string parquetPath)
        where TTag : class
    {
        var options = new DbContextOptionsBuilder<AccountContext<TTag>>()
            .UseDuckDB($"DataSource={DbPath}")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new AccountContext<TTag>(options, parquetPath);
    }

    private JoinContext<TTag> CreateJoinContext<TTag>(string parquetPath)
        where TTag : class
    {
        var options = new DbContextOptionsBuilder<JoinContext<TTag>>()
            .UseDuckDB($"DataSource={DbPath}")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new JoinContext<TTag>(options, parquetPath);
    }

    private ExplicitNamingContext<TTag> CreateExplicitNamingContext<TTag>(string parquetPath)
        where TTag : class
    {
        var options = new DbContextOptionsBuilder<ExplicitNamingContext<TTag>>()
            .UseDuckDB($"DataSource={DbPath}")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new ExplicitNamingContext<TTag>(options, parquetPath);
    }

    // Tag types give each test its own DbContext type so EF Core's model cache is not
    // shared across tests with different Parquet paths.
    private SparseCustomerContext<TTag> CreateSparseCustomerContext<TTag>(string parquetPath)
        where TTag : class
    {
        var options = new DbContextOptionsBuilder<SparseCustomerContext<TTag>>()
            .UseDuckDB($"DataSource={DbPath}")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new SparseCustomerContext<TTag>(options, parquetPath);
    }

    private NestedSparseCustomerContext CreateNestedSparseCustomerContext(string parquetPath)
    {
        var options = new DbContextOptionsBuilder<NestedSparseCustomerContext>()
            .UseDuckDB($"DataSource={DbPath}")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new NestedSparseCustomerContext(options, parquetPath);
    }

    private AttributeSelectiveContext CreateAttributeSelectiveContext(string parquetPath)
    {
        var options = new DbContextOptionsBuilder<AttributeSelectiveContext>()
            .UseDuckDB($"DataSource={DbPath}")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new AttributeSelectiveContext(options, parquetPath);
    }

    private TwoFieldSparseCustomerContext CreateTwoFieldSparseCustomerContext(string parquetPath)
    {
        var options = new DbContextOptionsBuilder<TwoFieldSparseCustomerContext>()
            .UseDuckDB($"DataSource={DbPath}")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new TwoFieldSparseCustomerContext(options, parquetPath);
    }

    private sealed class ProjectionTag;
    private sealed class FilterTag;
    private sealed class OrderByTag;
    private sealed class DuplicateLeavesTag;
    private sealed class JoinTag;
    private sealed class ExplicitNamingTag;
    private sealed class SelectiveProjectionTag;
    private sealed class PresenceCheckTag;
    private sealed class NullPresenceCheckTag;

    private sealed class CustomerContext<TTag>(DbContextOptions<CustomerContext<TTag>> options, string parquetPath) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(e =>
            {
                e.FromParquet(parquetPath);
                e.ComplexProperty(c => c.Location).UseStructMapping();
            });
        }
    }

    private sealed class SparseCustomerContext<TTag>(DbContextOptions<SparseCustomerContext<TTag>> options, string parquetPath)
        : DbContext(options)
        where TTag : class
    {
        public DbSet<SparseSystem> Systems => Set<SparseSystem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SparseSystem>(entity =>
            {
                entity.FromParquet(parquetPath);
                entity.HasKey(system => system.Uid);
                entity.ComplexProperty(system => system.Attributes).UseStructMapping(true);
            });
        }
    }

    private sealed class NestedSparseCustomerContext(DbContextOptions<NestedSparseCustomerContext> options, string parquetPath)
        : DbContext(options)
    {
        public DbSet<NestedSparseSystem> Systems => Set<NestedSparseSystem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NestedSparseSystem>(entity =>
            {
                entity.FromParquet(parquetPath);
                entity.HasKey(system => system.Uid);
                entity.ComplexProperty(system => system.Attributes).UseStructMapping(true);
            });
        }
    }

    private sealed class AttributeSelectiveContext(DbContextOptions<AttributeSelectiveContext> options, string parquetPath)
        : DbContext(options)
    {
        public DbSet<AttributeSelectiveSystem> Systems => Set<AttributeSelectiveSystem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AttributeSelectiveSystem>(entity =>
            {
                entity.FromParquet(parquetPath);
                entity.HasKey(system => system.Uid);
                entity.ComplexProperty(system => system.Attributes);
            });
        }
    }

    private sealed class TwoFieldSparseCustomerContext(DbContextOptions<TwoFieldSparseCustomerContext> options, string parquetPath)
        : DbContext(options)
    {
        public DbSet<TwoFieldSparseSystem> Systems => Set<TwoFieldSparseSystem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TwoFieldSparseSystem>(entity =>
            {
                entity.FromParquet(parquetPath);
                entity.HasKey(system => system.Uid);
                entity.ComplexProperty(system => system.Attributes).UseStructMapping(true);
            });
        }
    }

    private sealed class AccountContext<TTag>(DbContextOptions<AccountContext<TTag>> options, string parquetPath) : DbContext(options)
    {
        public DbSet<Account> Accounts => Set<Account>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>(e =>
            {
                e.FromParquet(parquetPath);
                e.ComplexProperty(c => c.Billing).UseStructMapping();
                e.ComplexProperty(c => c.Shipping).UseStructMapping();
            });
        }
    }

    private sealed class JoinContext<TTag>(DbContextOptions<JoinContext<TTag>> options, string parquetPath) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Order> Orders => Set<Order>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(e =>
            {
                e.FromParquet(parquetPath);
                e.ComplexProperty(c => c.Location).UseStructMapping();
            });
            modelBuilder.Entity<Order>(e => e.Property(o => o.Id).ValueGeneratedNever());
        }
    }

    private sealed class ExplicitNamingContext<TTag>(DbContextOptions<ExplicitNamingContext<TTag>> options, string parquetPath) : DbContext(options)
    {
        public DbSet<ExplicitCustomer> Customers => Set<ExplicitCustomer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExplicitCustomer>(e =>
            {
                e.FromParquet(parquetPath);
                e.Property(c => c.Id).ValueGeneratedNever();
                e.ComplexProperty(c => c.Location, loc =>
                {
                    loc.UseStructMapping();
                    loc.Property(l => l.City)
                        .HasColumnName("city_name")
                        .HasStructFieldName("city_name");
                });
            });
        }
    }

    private sealed class Customer
    {
        public int Id { get; set; }
        [UseStructMapping]
        public required Address Location { get; set; }
    }

    private sealed class Account
    {
        public int Id { get; set; }
        [UseStructMapping]
        public required Address Billing { get; set; }
        [UseStructMapping]
        public required Address Shipping { get; set; }
    }

    private sealed class ExplicitCustomer
    {
        public int Id { get; set; }
        [UseStructMapping]
        public required Address Location { get; set; }
    }

    private sealed class SparseSystem
    {
        public int Uid { get; set; }
        [UseStructMapping]
        public SparseAttributes? Attributes { get; set; }
    }

    private sealed class SparseAttributes
    {
        public string? Shorttext { get; set; }
        public string XEdrResponsibility { get; set; } = null!;
        public string XEdrSoftware { get; set; } = null!;
        public string XNacZone { get; set; } = null!;
        public string XSource { get; set; } = null!;
    }

    private sealed class TwoFieldSparseSystem
    {
        public int Uid { get; set; }
        [UseStructMapping]
        public TwoFieldSparseAttributes? Attributes { get; set; }
    }

    private sealed class TwoFieldSparseAttributes
    {
        public string? Shorttext { get; set; }
        public string? Other { get; set; }
    }

    private sealed class NestedSparseSystem
    {
        public int Uid { get; set; }
        [UseStructMapping]
        public NestedSparseAttributes? Attributes { get; set; }
    }

    private sealed class NestedSparseAttributes
    {
        public string? Shorttext { get; set; }
        public string XEdrResponsibility { get; set; } = null!;
        public NestedSparseDetails? Details { get; set; }
    }

    private sealed class NestedSparseDetails
    {
        public string City { get; set; } = null!;
    }

    private sealed class AttributeSelectiveSystem
    {
        public int Uid { get; set; }
        [UseStructMapping(true)]
        public SparseAttributes? Attributes { get; set; }
    }

    private sealed class Address
    {
        public string City { get; set; } = null!;
        public string Country { get; set; } = null!;
    }

    private sealed class Order
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public required string Method { get; set; }
    }
}