using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Translations;

public class EnumTranslationsDuckDBTest : EnumTranslationsTestBase<BasicTypesQueryDuckDBFixture>
{
    public EnumTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}
