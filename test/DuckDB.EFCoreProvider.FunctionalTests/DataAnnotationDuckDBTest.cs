using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore;

public class DataAnnotationDuckDBTest : DataAnnotationRelationalTestBase<DataAnnotationDuckDBTest.DataAnnotationDuckDBFixture>
{
    public DataAnnotationDuckDBTest(DataAnnotationDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Table_can_configure_TPT_with_Owned()
    {
        await base.Table_can_configure_TPT_with_Owned();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ForeignKey_to_ForeignKey()
    {
        base.ForeignKey_to_ForeignKey();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ForeignKey_to_ForeignKey_same_name()
    {
        base.ForeignKey_to_ForeignKey_same_name();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ForeignKey_to_ForeignKey_same_name_one_shadow()
    {
        base.ForeignKey_to_ForeignKey_same_name_one_shadow();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ForeignKeyAttribute_configures_two_self_referencing_relationships()
    {
        base.ForeignKeyAttribute_configures_two_self_referencing_relationships();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void ForeignKeyAttribute_creates_two_relationships_if_applied_on_navigations_on_both_sides_and_values_do_not_match()
    {
        base.ForeignKeyAttribute_creates_two_relationships_if_applied_on_navigations_on_both_sides_and_values_do_not_match();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task MaxLengthAttribute_throws_while_inserting_value_longer_than_max_length()
    {
        return base.MaxLengthAttribute_throws_while_inserting_value_longer_than_max_length();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void NotMapped_on_base_class_property_discovered_through_navigation_ignores_it()
    {
        base.NotMapped_on_base_class_property_discovered_through_navigation_ignores_it();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void NotMapped_on_overridden_property_is_ignored()
    {
        base.NotMapped_on_overridden_property_is_ignored();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Required_and_ForeignKey_to_Required_and_ForeignKey_can_be_overridden()
    {
        base.Required_and_ForeignKey_to_Required_and_ForeignKey_can_be_overridden();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task RequiredAttribute_for_navigation_throws_while_inserting_null_value()
    {
        return base.RequiredAttribute_for_navigation_throws_while_inserting_null_value();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task StringLengthAttribute_throws_while_inserting_value_longer_than_max_length()
    {
        return base.StringLengthAttribute_throws_while_inserting_value_longer_than_max_length();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task TimestampAttribute_throws_if_value_in_database_changed()
    {
        return base.TimestampAttribute_throws_if_value_in_database_changed();
    }

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());

    protected override TestHelpers TestHelpers
        => DuckDBTestHelpers.Instance;

    public class DataAnnotationDuckDBFixture : DataAnnotationRelationalFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;
    }
}
