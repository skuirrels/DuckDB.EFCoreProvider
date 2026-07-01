using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Translations.Temporal;

public class DateTimeOffsetTranslationsDuckDBTest : DateTimeOffsetTranslationsTestBase<BasicTypesQueryDuckDBFixture>
{
    public DateTimeOffsetTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task AddDays()
    {
        return base.AddDays();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task AddHours()
    {
        return base.AddHours();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task AddMilliseconds()
    {
        return base.AddMilliseconds();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task AddMinutes()
    {
        return base.AddMinutes();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task AddMonths()
    {
        return base.AddMonths();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task AddSeconds()
    {
        return base.AddSeconds();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task AddYears()
    {
        return base.AddYears();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Date()
    {
        return base.Date();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task DayOfYear()
    {
        return base.DayOfYear();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Hour()
    {
        return base.Hour();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Day()
    {
        return base.Day();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Microsecond()
    {
        return base.Microsecond();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Millisecond()
    {
        return base.Millisecond();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nanosecond()
    {
        return base.Nanosecond();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Now()
    {
        return base.Now();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task TimeOfDay()
    {
        return base.TimeOfDay();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ToUnixTimeMilliseconds()
    {
        return base.ToUnixTimeMilliseconds();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Minute()
    {
        return base.Minute();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ToUnixTimeSecond()
    {
        return base.ToUnixTimeSecond();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task UtcNow()
    {
        return base.UtcNow();
    }
}