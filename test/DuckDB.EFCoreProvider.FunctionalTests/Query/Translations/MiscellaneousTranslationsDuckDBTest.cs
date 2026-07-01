using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Translations;

public class MiscellaneousTranslationsDuckDBTest : MiscellaneousTranslationsRelationalTestBase<BasicTypesQueryDuckDBFixture>
{
    public MiscellaneousTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}
