using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Translations;

public class MathTranslationsDuckDBTest : MathTranslationsTestBase<BasicTypesQueryDuckDBFixture>
{
    public MathTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}
