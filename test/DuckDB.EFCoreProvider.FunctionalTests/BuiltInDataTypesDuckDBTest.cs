using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class BuiltInDataTypesDuckDBTest : BuiltInDataTypesTestBase<BuiltInDataTypesDuckDBTest.BuiltInDataTypesDuckDBFixture>
{
    public BuiltInDataTypesDuckDBTest(BuiltInDataTypesDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = "DateTimeOffset with non-zero offset, https://github.com/dotnet/efcore/issues/26068")]
    public override Task Can_insert_and_read_back_all_non_nullable_data_types()
    {
        return base.Can_insert_and_read_back_all_non_nullable_data_types();
    }

    [ConditionalFact(Skip = "DateTimeOffset with non-zero offset, https://github.com/dotnet/efcore/issues/26068")]
    public override Task Can_insert_and_read_back_all_nullable_data_types_with_values_set_to_non_null()
    {
        return base.Can_insert_and_read_back_all_nullable_data_types_with_values_set_to_non_null();
    }

    [ConditionalFact(Skip = "DateTimeOffset with non-zero offset, https://github.com/dotnet/efcore/issues/26068")]
    public override Task Can_insert_and_read_back_non_nullable_backed_data_types()
    {
        return base.Can_insert_and_read_back_non_nullable_backed_data_types();
    }

    [ConditionalFact(Skip = "DateTimeOffset with non-zero offset, https://github.com/dotnet/efcore/issues/26068")]
    public override Task Can_insert_and_read_back_nullable_backed_data_types()
    {
        return base.Can_insert_and_read_back_nullable_backed_data_types();
    }

    [ConditionalFact(Skip = "DateTimeOffset with non-zero offset, https://github.com/dotnet/efcore/issues/26068")]
    public override Task Can_insert_and_read_back_object_backed_data_types()
    {
        return base.Can_insert_and_read_back_object_backed_data_types();
    }

    public class BuiltInDataTypesDuckDBFixture : BuiltInDataTypesFixtureBase, ITestSqlLoggerFactory
    {
        protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;

        public override bool StrictEquality { get; }

        public override bool SupportsAnsi => false;

        public override bool SupportsUnicodeToAnsiConversion => false;

        public override bool SupportsLargeStringComparisons { get; }

        public override bool SupportsBinaryKeys { get; }

        public override bool SupportsDecimalComparisons { get; }

        public override DateTime DefaultDateTime => new();

        public override bool PreservesDateTimeKind { get; }

        public TestSqlLoggerFactory TestSqlLoggerFactory => (TestSqlLoggerFactory)ListLoggerFactory;
    }
}
