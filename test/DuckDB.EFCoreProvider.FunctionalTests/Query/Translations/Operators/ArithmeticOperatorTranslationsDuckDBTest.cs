using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Translations.Operators;

public class ArithmeticOperatorTranslationsDuckDBTest : ArithmeticOperatorTranslationsTestBase<BasicTypesQueryDuckDBFixture>
{
    public ArithmeticOperatorTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}
