using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Translations.Temporal;

public class TimeOnlyTranslationsDuckDBTest : TimeOnlyTranslationsTestBase<BasicTypesQueryDuckDBFixture>
{
    public TimeOnlyTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_TimeSpan()
    {
        return base.Add_TimeSpan();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task AddHours()
    {
        return base.AddHours();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task AddMinutes()
    {
        return base.AddMinutes();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromDateTime_compared_to_property()
    {
        return base.FromDateTime_compared_to_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromDateTime_compared_to_constant()
    {
        return base.FromDateTime_compared_to_constant();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromDateTime_compared_to_parameter()
    {
        return base.FromDateTime_compared_to_parameter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromTimeSpan_compared_to_parameter()
    {
        return base.FromTimeSpan_compared_to_parameter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromTimeSpan_compared_to_property()
    {
        return base.FromTimeSpan_compared_to_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task IsBetween()
    {
        return base.IsBetween();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Millisecond()
    {
        return base.Millisecond();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Microsecond()
    {
        return base.Microsecond();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nanosecond()
    {
        return base.Nanosecond();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Order_by_FromTimeSpan()
    {
        return base.Order_by_FromTimeSpan();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Subtract()
    {
        return base.Subtract();
    }
}
