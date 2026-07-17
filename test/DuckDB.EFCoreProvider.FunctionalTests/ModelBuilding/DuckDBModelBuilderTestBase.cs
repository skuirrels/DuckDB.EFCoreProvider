using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.ModelBuilding;

public class DuckDBModelBuilderTestBase : RelationalModelBuilderTest
{
    public abstract class DuckDBNonRelationship(DuckDBModelBuilderFixture fixture)
        : RelationalNonRelationshipTestBase(fixture), IClassFixture<DuckDBModelBuilderFixture>
    {
        [ConditionalFact]
        public void UseAutoincrement_sets_value_generation_strategy()
        {
            var modelBuilder = CreateModelBuilder();

            var propertyBuilder = modelBuilder
                .Entity<Customer>()
                .Property(e => e.Id);

            propertyBuilder.UseAutoIncrement();

            Assert.Equal(DuckDBValueGenerationStrategy.AutoIncrement, propertyBuilder.Metadata.GetValueGenerationStrategy());
        }

        [ConditionalFact]
        public void UseAutoincrement_sets_value_generated_on_add()
        {
            var modelBuilder = CreateModelBuilder();

            var property = modelBuilder
                .Entity<Customer>()
                .Property(e => e.OtherId)
                .UseAutoIncrement()
                .Metadata;

            Assert.Equal(ValueGenerated.OnAdd, property.ValueGenerated);
            Assert.Equal(DuckDBValueGenerationStrategy.AutoIncrement, property.GetValueGenerationStrategy());
        }

        [ConditionalFact]
        public void Generic_UseAutoincrement_sets_value_generation_strategy()
        {
            var modelBuilder = CreateModelBuilder();

            var propertyBuilder = modelBuilder
                .Entity<Customer>()
                .Property<int>(e => e.Id);

            propertyBuilder.UseAutoIncrement();

            Assert.Equal(DuckDBValueGenerationStrategy.AutoIncrement, propertyBuilder.Metadata.GetValueGenerationStrategy());
        }

        [ConditionalFact]
        public void Default_value_generation_strategy_for_integer_primary_key()
        {
            var modelBuilder = CreateModelBuilder();

            var property = modelBuilder
                .Entity<Customer>()
                .Property(e => e.Id)
                .Metadata;

            var model = modelBuilder.FinalizeModel();

            // With conventions, integer primary keys should get autoincrement
            Assert.Equal(DuckDBValueGenerationStrategy.AutoIncrement, property.GetValueGenerationStrategy());
        }

        [ConditionalFact]
        public void No_autoincrement_for_non_primary_key()
        {
            var modelBuilder = CreateModelBuilder();

            var property = modelBuilder
                .Entity<Customer>()
                .Property(e => e.OtherId)
                .Metadata;

            var model = modelBuilder.FinalizeModel();

            Assert.Equal(DuckDBValueGenerationStrategy.None, property.GetValueGenerationStrategy());
        }

        [ConditionalFact]
        public void No_autoincrement_for_non_integer_primary_key()
        {
            var modelBuilder = CreateModelBuilder();

            var property = modelBuilder
                .Entity<CustomerWithStringKey>()
                .Property(e => e.Id)
                .Metadata;

            var model = modelBuilder.FinalizeModel();

            Assert.Equal(DuckDBValueGenerationStrategy.None, property.GetValueGenerationStrategy());
        }

        [ConditionalFact]
        public void No_autoincrement_for_composite_primary_key()
        {
            var modelBuilder = CreateModelBuilder();

            modelBuilder
                .Entity<CustomerWithCompositeKey>(b =>
                {
                    b.HasKey(e => new { e.Id1, e.Id2 });
                });

            var property1 = modelBuilder.Entity<CustomerWithCompositeKey>().Property(e => e.Id1).Metadata;
            var property2 = modelBuilder.Entity<CustomerWithCompositeKey>().Property(e => e.Id2).Metadata;

            var model = modelBuilder.FinalizeModel();

            Assert.Equal(DuckDBValueGenerationStrategy.None, property1.GetValueGenerationStrategy());
            Assert.Equal(DuckDBValueGenerationStrategy.None, property2.GetValueGenerationStrategy());
        }

        [ConditionalFact]
        public void No_autoincrement_when_default_value_set()
        {
            var modelBuilder = CreateModelBuilder();

            var property = modelBuilder
                .Entity<Customer>()
                .Property(e => e.Id)
                .HasDefaultValue(42)
                .Metadata;

            var model = modelBuilder.FinalizeModel();

            Assert.Equal(DuckDBValueGenerationStrategy.None, property.GetValueGenerationStrategy());
        }

        [ConditionalFact]
        public void No_autoincrement_when_default_value_sql_set()
        {
            var modelBuilder = CreateModelBuilder();

            var property = modelBuilder
                .Entity<Customer>()
                .Property(e => e.Id)
                .HasDefaultValueSql("1")
                .Metadata;

            var model = modelBuilder.FinalizeModel();

            Assert.Equal(DuckDBValueGenerationStrategy.None, property.GetValueGenerationStrategy());
        }

        [ConditionalFact]
        public void No_autoincrement_when_computed_column_sql_set()
        {
            var modelBuilder = CreateModelBuilder();

            var property = modelBuilder
                .Entity<Customer>()
                .Property(e => e.Id)
                .HasComputedColumnSql("1")
                .Metadata;

            var model = modelBuilder.FinalizeModel();

            Assert.Equal(DuckDBValueGenerationStrategy.None, property.GetValueGenerationStrategy());
        }

        [ConditionalFact]
        public void No_autoincrement_when_property_is_foreign_key()
        {
            var modelBuilder = CreateModelBuilder();

            modelBuilder.Entity<Order>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.GroupId);
                b.HasOne<Customer>()
                    .WithMany()
                    .HasForeignKey(e => e.GroupId);
            });

            var property = modelBuilder.Entity<Order>().Property(e => e.GroupId).Metadata;

            var model = modelBuilder.FinalizeModel();

            Assert.Equal(DuckDBValueGenerationStrategy.None, property.GetValueGenerationStrategy());
        }

        private class Customer
        {
            public int Id { get; set; }
            public int OtherId { get; set; }
            public string? Name { get; set; }
        }

        private class CustomerWithStringKey
        {
            public string Id { get; set; } = null!;
            public string? Name { get; set; }
        }

        private class CustomerWithCompositeKey
        {
            public int Id1 { get; set; }
            public int Id2 { get; set; }
            public string? Name { get; set; }
        }

        private class Order
        {
            public int Id { get; set; }
            public int GroupId { get; set; }
        }
    }

    public abstract class DuckDBComplexType(DuckDBModelBuilderFixture fixture)
        : RelationalComplexTypeTestBase(fixture), IClassFixture<DuckDBModelBuilderFixture>;

    public abstract class DuckDBComplexCollection(DuckDBModelBuilderFixture fixture)
        : RelationalComplexCollectionTestBase(fixture), IClassFixture<DuckDBModelBuilderFixture>;

    public abstract class DuckDBInheritance(DuckDBModelBuilderFixture fixture)
        : RelationalInheritanceTestBase(fixture), IClassFixture<DuckDBModelBuilderFixture>;

    public abstract class DuckDBOneToMany(DuckDBModelBuilderFixture fixture)
        : RelationalOneToManyTestBase(fixture), IClassFixture<DuckDBModelBuilderFixture>;

    public abstract class DuckDBManyToOne(DuckDBModelBuilderFixture fixture)
        : RelationalManyToOneTestBase(fixture), IClassFixture<DuckDBModelBuilderFixture>;

    public abstract class DuckDBOneToOne(DuckDBModelBuilderFixture fixture)
        : RelationalOneToOneTestBase(fixture), IClassFixture<DuckDBModelBuilderFixture>;

    public abstract class DuckDBManyToMany(DuckDBModelBuilderFixture fixture)
        : RelationalManyToManyTestBase(fixture), IClassFixture<DuckDBModelBuilderFixture>;

    public abstract class DuckDBOwnedTypes(DuckDBModelBuilderFixture fixture)
        : RelationalOwnedTypesTestBase(fixture), IClassFixture<DuckDBModelBuilderFixture>
    {
        // public override void Can_use_sproc_mapping_with_owned_reference()
        //     => Assert.Equal(
        //         DuckDBStrings.StoredProceduresNotSupported("Book.Label#BookLabel"),
        //         Assert.Throws<InvalidOperationException>(base.Can_use_sproc_mapping_with_owned_reference).Message);
    }

    public class DuckDBModelBuilderFixture : RelationalModelBuilderFixture
    {
        public override TestHelpers TestHelpers
            => DuckDBTestHelpers.Instance;
    }
}
