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

    public override async Task JsonPathExists_on_scalar_string_column()
    {
        await base.JsonPathExists_on_scalar_string_column();

        AssertSql(
            """
SELECT j."Id", j."JsonString", j."JsonComplexType", j."JsonOwnedType"
FROM "JsonEntities" AS j
WHERE json_type(j."JsonString", '$.OptionalInt') IS NOT NULL
""");
    }

    public override async Task JsonPathExists_on_complex_property()
    {
        await base.JsonPathExists_on_complex_property();

        AssertSql(
            """
SELECT j."Id", j."JsonString", j."JsonComplexType", j."JsonOwnedType"
FROM "JsonEntities" AS j
WHERE json_type(j."JsonComplexType", '$.OptionalInt') IS NOT NULL
""");
    }

    public override async Task JsonPathExists_on_owned_entity()
    {
        await base.JsonPathExists_on_owned_entity();

        AssertSql(
            """
SELECT j."Id", j."JsonString", j."JsonComplexType", j."JsonOwnedType"
FROM "JsonEntities" AS j
WHERE json_type(j."JsonOwnedType", '$.OptionalInt') IS NOT NULL
""");
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

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