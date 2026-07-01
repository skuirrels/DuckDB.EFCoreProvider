using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Translations.Temporal;

public class TimeSpanTranslationsDuckDBTest : TimeSpanTranslationsTestBase<BasicTypesQueryDuckDBFixture>
{
    public TimeSpanTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Milliseconds()
    {
        return base.Milliseconds();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nanoseconds()
    {
        return base.Nanoseconds();
    }
}
