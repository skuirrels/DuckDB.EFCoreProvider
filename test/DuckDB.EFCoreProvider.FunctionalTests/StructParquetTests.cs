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
    public void Required_navigation_joins_through_struct_foreign_key_between_parquet_files()
    {
        var principalsPath = ParquetPath();
        var dependentsPath = ParquetPath();
        try
        {
            WriteStructParquet(principalsPath, """
                CREATE TABLE t (Id INTEGER, Name VARCHAR);
                INSERT INTO t VALUES
                    (1, 'North'),
                    (2, 'South')
                """);
            WriteStructParquet(dependentsPath, """
                CREATE TABLE t (
                    Id INTEGER,
                    Relationship STRUCT(parent_id INTEGER, label VARCHAR)
                );
                INSERT INTO t VALUES
                    (10, {'parent_id': 2, 'label': 'second'}),
                    (11, {'parent_id': 1, 'label': 'first'})
                """);

            using var context = CreateStructRequiredRelationshipContext<RequiredRelationshipTag>(
                principalsPath,
                dependentsPath,
                required: null);
            Assert.True(
                context.Model.FindEntityType(typeof(StructRequiredDependent))!
                    .FindNavigation(nameof(StructRequiredDependent.Principal))!
                    .ForeignKey.IsRequired);

            var query = context.Dependents
                .OrderBy(dependent => dependent.Id)
                .Select(dependent => new
                {
                    dependent.Id,
                    ParentId = dependent.Relationship.ParentId,
                    PrincipalName = dependent.Principal!.Name
                });

            var sql = query.ToQueryString();
            var results = query.ToList();

            Assert.Contains("INNER JOIN", sql, StringComparison.Ordinal);
            Assert.Contains(".\"Relationship\".parent_id", sql, StringComparison.Ordinal);
            Assert.Equal(
                [
                    new { Id = 10, ParentId = 2, PrincipalName = "South" },
                    new { Id = 11, ParentId = 1, PrincipalName = "North" }
                ],
                results);

            var north = context.Principals
                .Include(principal => principal.Dependents)
                .Single(principal => principal.Id == 1);

            Assert.Equal("North", north.Name);
            Assert.Equal(11, Assert.Single(north.Dependents).Id);
        }
        finally
        {
            File.Delete(principalsPath);
            File.Delete(dependentsPath);
        }
    }

    [ConditionalFact]
    public void Optional_navigation_left_joins_through_nullable_struct_foreign_key_between_parquet_files()
    {
        var principalsPath = ParquetPath();
        var dependentsPath = ParquetPath();
        try
        {
            WriteStructParquet(principalsPath, """
                CREATE TABLE t (Id INTEGER, Name VARCHAR);
                INSERT INTO t VALUES
                    (1, 'North'),
                    (2, 'South')
                """);
            WriteStructParquet(dependentsPath, """
                CREATE TABLE t (
                    Id INTEGER,
                    Relationship STRUCT(parent_id INTEGER, label VARCHAR)
                );
                INSERT INTO t VALUES
                    (20, {'parent_id': 1, 'label': 'linked'}),
                    (21, {'parent_id': NULL, 'label': 'unlinked'})
                """);

            using var context = CreateStructRelationshipContext<OptionalRelationshipTag>(
                principalsPath,
                dependentsPath,
                required: null);
            Assert.True(
                context.Model.FindEntityType(typeof(StructDependent))!
                    .GetForeignKeys()
                    .Single()
                    .Properties
                    .Single()
                    .IsNullable);
            Assert.False(
                context.Model.FindEntityType(typeof(StructDependent))!
                    .FindNavigation(nameof(StructDependent.Principal))!
                    .ForeignKey.IsRequired);

            var query = context.Dependents
                .OrderBy(dependent => dependent.Id)
                .Select(dependent => new
                {
                    dependent.Id,
                    ParentId = dependent.Relationship.ParentId,
                    PrincipalName = dependent.Principal == null ? null : dependent.Principal.Name
                });

            var sql = query.ToQueryString();
            var results = query.ToList();

            Assert.Contains("LEFT JOIN", sql, StringComparison.Ordinal);
            Assert.Contains(".\"Relationship\".parent_id", sql, StringComparison.Ordinal);
            Assert.Equal(
                [
                    new { Id = 20, ParentId = (int?)1, PrincipalName = (string?)"North" },
                    new { Id = 21, ParentId = (int?)null, PrincipalName = (string?)null }
                ],
                results);
        }
        finally
        {
            File.Delete(principalsPath);
            File.Delete(dependentsPath);
        }
    }

    [ConditionalFact]
    public void Required_override_wins_over_nullable_struct_leaf()
    {
        var principalsPath = ParquetPath();
        var dependentsPath = ParquetPath();
        try
        {
            WriteStructParquet(principalsPath, """
                CREATE TABLE t (Id INTEGER, Name VARCHAR);
                INSERT INTO t VALUES
                    (1, 'North'),
                    (2, 'South')
                """);
            WriteStructParquet(dependentsPath, """
                CREATE TABLE t (
                    Id INTEGER,
                    Relationship STRUCT(parent_id INTEGER, label VARCHAR)
                );
                INSERT INTO t VALUES
                    (30, {'parent_id': 2, 'label': 'second'}),
                    (31, {'parent_id': 1, 'label': 'first'})
                """);

            // A nullable leaf would be inferred as optional, but an explicit IsRequired(true)
            // must win and produce an INNER JOIN.
            using var context = CreateStructRelationshipContext<RequiredOverrideTag>(
                principalsPath,
                dependentsPath,
                required: true);
            Assert.True(
                context.Model.FindEntityType(typeof(StructDependent))!
                    .FindNavigation(nameof(StructDependent.Principal))!
                    .ForeignKey.IsRequired);

            var query = context.Dependents
                .OrderBy(dependent => dependent.Id)
                .Select(dependent => new
                {
                    dependent.Id,
                    ParentId = dependent.Relationship.ParentId,
                    PrincipalName = dependent.Principal!.Name
                });

            var sql = query.ToQueryString();
            var results = query.ToList();

            Assert.Contains("INNER JOIN", sql, StringComparison.Ordinal);
            Assert.Equal(
                [
                    new { Id = 30, ParentId = (int?)2, PrincipalName = "South" },
                    new { Id = 31, ParentId = (int?)1, PrincipalName = "North" }
                ],
                results);
        }
        finally
        {
            File.Delete(principalsPath);
            File.Delete(dependentsPath);
        }
    }

    [ConditionalFact]
    public void Optional_override_wins_over_non_nullable_struct_leaf()
    {
        var principalsPath = ParquetPath();
        var dependentsPath = ParquetPath();
        try
        {
            WriteStructParquet(principalsPath, """
                CREATE TABLE t (Id INTEGER, Name VARCHAR);
                INSERT INTO t VALUES
                    (1, 'North'),
                    (2, 'South')
                """);
            WriteStructParquet(dependentsPath, """
                CREATE TABLE t (
                    Id INTEGER,
                    Relationship STRUCT(parent_id INTEGER, label VARCHAR)
                );
                INSERT INTO t VALUES
                    (40, {'parent_id': 1, 'label': 'linked'})
                """);

            // A non-nullable leaf would be inferred as required, but an explicit IsRequired(false)
            // must win and produce a LEFT JOIN.
            using var context = CreateStructRequiredRelationshipContext<OptionalOverrideTag>(
                principalsPath,
                dependentsPath,
                required: false);
            Assert.False(
                context.Model.FindEntityType(typeof(StructRequiredDependent))!
                    .FindNavigation(nameof(StructRequiredDependent.Principal))!
                    .ForeignKey.IsRequired);

            var query = context.Dependents
                .OrderBy(dependent => dependent.Id)
                .Select(dependent => new
                {
                    dependent.Id,
                    ParentId = dependent.Relationship.ParentId,
                    PrincipalName = dependent.Principal == null ? null : dependent.Principal.Name
                });

            var sql = query.ToQueryString();
            var results = query.ToList();

            Assert.Contains("LEFT JOIN", sql, StringComparison.Ordinal);
            Assert.Equal(
                [
                    new { Id = 40, ParentId = 1, PrincipalName = (string?)"North" }
                ],
                results);
        }
        finally
        {
            File.Delete(principalsPath);
            File.Delete(dependentsPath);
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

    private StructRelationshipContext<TTag> CreateStructRelationshipContext<TTag>(
        string principalsPath,
        string dependentsPath,
        bool? required)
        where TTag : class
    {
        var options = new DbContextOptionsBuilder<StructRelationshipContext<TTag>>()
            .UseDuckDB($"DataSource={DbPath}")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new StructRelationshipContext<TTag>(options, principalsPath, dependentsPath, required);
    }

    private StructRequiredRelationshipContext<TTag> CreateStructRequiredRelationshipContext<TTag>(
        string principalsPath,
        string dependentsPath,
        bool? required)
        where TTag : class
    {
        var options = new DbContextOptionsBuilder<StructRequiredRelationshipContext<TTag>>()
            .UseDuckDB($"DataSource={DbPath}")
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;
        return new StructRequiredRelationshipContext<TTag>(options, principalsPath, dependentsPath, required);
    }

    // Tag types give each test its own DbContext type so EF Core's model cache is not
    // shared across tests with different Parquet paths.
    private sealed class ProjectionTag;
    private sealed class FilterTag;
    private sealed class OrderByTag;
    private sealed class DuplicateLeavesTag;
    private sealed class JoinTag;
    private sealed class ExplicitNamingTag;
    private sealed class RequiredRelationshipTag;
    private sealed class OptionalRelationshipTag;
    private sealed class RequiredOverrideTag;
    private sealed class OptionalOverrideTag;

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

    private sealed class StructRelationshipContext<TTag>(
        DbContextOptions<StructRelationshipContext<TTag>> options,
        string principalsPath,
        string dependentsPath,
        bool? required)
        : DbContext(options)
        where TTag : class
    {
        public DbSet<StructPrincipal> Principals => Set<StructPrincipal>();
        public DbSet<StructDependent> Dependents => Set<StructDependent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<StructPrincipal>(entity =>
            {
                entity.FromParquet(principalsPath);
                entity.Property(principal => principal.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<StructDependent>(entity =>
            {
                entity.FromParquet(dependentsPath);
                entity.Property(dependent => dependent.Id).ValueGeneratedNever();
                entity.ComplexProperty(dependent => dependent.Relationship, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.ParentId).HasStructFieldName("parent_id");
                });
                var relationship = entity.HasOne(dependent => dependent.Principal)
                    .WithMany(principal => principal.Dependents)
                    .HasStructForeignKey(dependent => dependent.Relationship.ParentId);
                if (required is { } r)
                {
                    relationship.IsRequired(r);
                }
            });
        }
    }

    private sealed class StructRequiredRelationshipContext<TTag>(
        DbContextOptions<StructRequiredRelationshipContext<TTag>> options,
        string principalsPath,
        string dependentsPath,
        bool? required)
        : DbContext(options)
        where TTag : class
    {
        public DbSet<StructRequiredPrincipal> Principals => Set<StructRequiredPrincipal>();
        public DbSet<StructRequiredDependent> Dependents => Set<StructRequiredDependent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<StructRequiredPrincipal>(entity =>
            {
                entity.FromParquet(principalsPath);
                entity.Property(principal => principal.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<StructRequiredDependent>(entity =>
            {
                entity.FromParquet(dependentsPath);
                entity.Property(dependent => dependent.Id).ValueGeneratedNever();
                entity.ComplexProperty(dependent => dependent.Relationship, complex =>
                {
                    complex.UseStructMapping();
                    complex.Property(value => value.ParentId).HasStructFieldName("parent_id");
                });
                var relationship = entity.HasOne(dependent => dependent.Principal)
                    .WithMany(principal => principal.Dependents)
                    .HasStructForeignKey(dependent => dependent.Relationship.ParentId);
                if (required is { } r)
                {
                    relationship.IsRequired(r);
                }
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

    private sealed class StructPrincipal
    {
        public int Id { get; set; }
        public required string Name { get; set; }
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

    private sealed class StructRequiredPrincipal
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public List<StructRequiredDependent> Dependents { get; set; } = [];
    }

    private sealed class StructRequiredDependent
    {
        public int Id { get; set; }
        public required StructRequiredRelationshipPath Relationship { get; set; }
        public StructRequiredPrincipal? Principal { get; set; }
    }

    private sealed class StructRequiredRelationshipPath
    {
        public int ParentId { get; set; }
    }
}