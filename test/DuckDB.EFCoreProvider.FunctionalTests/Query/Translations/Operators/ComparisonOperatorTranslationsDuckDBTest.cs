using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Translations.Operators;

public class ComparisonOperatorTranslationsDuckDBTest : ComparisonOperatorTranslationsTestBase<BasicTypesQueryDuckDBFixture>
{
    public ComparisonOperatorTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}