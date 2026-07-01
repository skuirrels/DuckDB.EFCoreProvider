using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Translations.Temporal;

public class DateOnlyTranslationsDuckDBTest : DateOnlyTranslationsTestBase<BasicTypesQueryDuckDBFixture>
{
    public DateOnlyTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task DayNumber_subtraction()
    {
        await base.DayNumber_subtraction();

        AssertSql(
            """
            DayNumber='726775'

            SELECT b."Id", b."Bool", b."Byte", b."ByteArray", b."DateOnly", b."DateTime", b."DateTimeOffset", b."Decimal", b."Double", b."Enum", b."FlagsEnum", b."Float", b."Guid", b."Int", b."Long", b."Short", b."String", b."TimeOnly", b."TimeSpan"
            FROM "BasicTypesEntities" AS b
            WHERE (date_diff('day', '0001-01-01', b."DateOnly") - $DayNumber) = 5
            """);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromDateTime_compared_to_property()
    {
        return base.FromDateTime_compared_to_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ToDateTime_constant_DateTime_with_property_TimeOnly()
    {
        return base.ToDateTime_constant_DateTime_with_property_TimeOnly();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ToDateTime_property_with_constant_TimeOnly()
    {
        return base.ToDateTime_property_with_constant_TimeOnly();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ToDateTime_property_with_property_TimeOnly()
    {
        return base.ToDateTime_property_with_property_TimeOnly();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ToDateTime_with_complex_DateTime()
    {
        return base.ToDateTime_with_complex_DateTime();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task ToDateTime_with_complex_TimeOnly()
    {
        return base.ToDateTime_with_complex_TimeOnly();
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
