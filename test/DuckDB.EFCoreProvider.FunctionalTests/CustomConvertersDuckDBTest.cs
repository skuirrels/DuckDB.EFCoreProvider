using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore;

public class CustomConvertersDuckDBTest : CustomConvertersTestBase<CustomConvertersDuckDBTest.CustomConvertersDuckDBFixture>
{
    public CustomConvertersDuckDBTest(CustomConvertersDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalFact(Skip = "DateTimeOffset with non-zero offset, https://github.com/dotnet/efcore/issues/26068")]
    public override async Task Can_insert_and_read_back_non_nullable_backed_data_types()
    {
        await base.Can_insert_and_read_back_non_nullable_backed_data_types();
    }

    [ConditionalFact(Skip = "DateTimeOffset with non-zero offset, https://github.com/dotnet/efcore/issues/26068")]
    public override async Task Can_insert_and_read_back_nullable_backed_data_types()
    {
        await base.Can_insert_and_read_back_nullable_backed_data_types();
    }

    [ConditionalFact(Skip = "DateTimeOffset with non-zero offset, https://github.com/dotnet/efcore/issues/26068")]
    public override async Task Can_insert_and_read_back_object_backed_data_types()
    {
        await base.Can_insert_and_read_back_object_backed_data_types();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_query_using_any_nullable_data_type_as_literal()
    {
        await base.Can_query_using_any_nullable_data_type_as_literal();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_and_read_back_with_case_insensitive_string_key()
    {
        await base.Can_insert_and_read_back_with_case_insensitive_string_key();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Infer_type_mapping_from_in_subquery_to_item()
    {
        base.Infer_type_mapping_from_in_subquery_to_item();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Value_conversion_on_enum_collection_contains()
    {
        base.Value_conversion_on_enum_collection_contains();
    }

    public class CustomConvertersDuckDBFixture : CustomConvertersFixtureBase, ITestSqlLoggerFactory
    {
        public override bool StrictEquality
            => false;

        public override bool SupportsAnsi
            => false;

        public override bool SupportsUnicodeToAnsiConversion
            => true;

        public override bool SupportsLargeStringComparisons
            => true;

        public override bool SupportsDecimalComparisons
            => false;

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        public override bool SupportsBinaryKeys
            => true;

        public override DateTime DefaultDateTime
            => new();

        public override bool PreservesDateTimeKind
            => true;
    }
}
