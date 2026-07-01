using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Translations.Operators;

public class MiscellaneousOperatorTranslationsDuckDBTest : MiscellaneousOperatorTranslationsTestBase<BasicTypesQueryDuckDBFixture>
{
    public MiscellaneousOperatorTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}
