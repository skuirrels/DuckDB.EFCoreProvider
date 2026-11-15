#if NET11_0_OR_GREATER
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query.Translations;

public class JsonTranslationsDuckDBTest : JsonTranslationsRelationalTestBase<JsonTranslationsDuckDBTest.JsonTranslationsQueryDuckDBFixture>
{
    public JsonTranslationsDuckDBTest(JsonTranslationsQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    // EF.Functions.JsonPathExists is new in EF Core 11 and has no DuckDB translation yet.
    [ConditionalFact(Skip = DuckDBSkipReasons.NewEF11Coverage)]
    public override Task JsonPathExists_on_scalar_string_column()
        => base.JsonPathExists_on_scalar_string_column();

    // EF.Functions.JsonPathExists is new in EF Core 11 and has no DuckDB translation yet.
    [ConditionalFact(Skip = DuckDBSkipReasons.NewEF11Coverage)]
    public override Task JsonPathExists_on_complex_property()
        => base.JsonPathExists_on_complex_property();

    // EF.Functions.JsonPathExists is new in EF Core 11 and has no DuckDB translation yet.
    [ConditionalFact(Skip = DuckDBSkipReasons.NewEF11Coverage)]
    public override Task JsonPathExists_on_owned_entity()
        => base.JsonPathExists_on_owned_entity();

    public class JsonTranslationsQueryDuckDBFixture : JsonTranslationsQueryFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        // A JSON merge patch with a null value removes the property.
        protected override string RemoveJsonProperty(string column, string property)
            => $"json_merge_patch({column}, json_object('{property}', NULL))";
    }
}
#endif