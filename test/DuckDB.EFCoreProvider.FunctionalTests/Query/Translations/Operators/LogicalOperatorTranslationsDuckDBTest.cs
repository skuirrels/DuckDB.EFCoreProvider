using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Translations.Operators;

public class LogicalOperatorTranslationsDuckDBTest : LogicalOperatorTranslationsTestBase<BasicTypesQueryDuckDBFixture>
{
    public LogicalOperatorTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}
