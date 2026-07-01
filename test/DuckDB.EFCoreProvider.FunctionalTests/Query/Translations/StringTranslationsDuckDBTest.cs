using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Translations;

public class StringTranslationsDuckDBTest : StringTranslationsRelationalTestBase<BasicTypesQueryDuckDBFixture>
{
    public StringTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}
