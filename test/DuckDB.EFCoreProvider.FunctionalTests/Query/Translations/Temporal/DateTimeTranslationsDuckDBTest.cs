using Xunit;
using Xunit.Abstractions;
using System.Globalization;

namespace Microsoft.EntityFrameworkCore.Query.Translations.Temporal;

public class DateTimeTranslationsDuckDBTest : DateTimeTranslationsTestBase<BasicTypesQueryDuckDBFixture>
{
    public DateTimeTranslationsDuckDBTest(BasicTypesQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task DayOfYear()
    {
        await base.DayOfYear();

        AssertSql(
            """
            SELECT b."Id", b."Bool", b."Byte", b."ByteArray", b."DateOnly", b."DateTime", b."DateTimeOffset", b."Decimal", b."Double", b."Enum", b."FlagsEnum", b."Float", b."Guid", b."Int", b."Long", b."Short", b."String", b."TimeOnly", b."TimeSpan"
            FROM "BasicTypesEntities" AS b
            WHERE dayofyear(b."DateTime") = 124
            """);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Millisecond()
    {
        return base.Millisecond();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task subtract_and_TotalDays()
    {
        return base.subtract_and_TotalDays();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task TimeOfDay()
    {
        return base.TimeOfDay();
    }

    public override async Task Parse_with_constant()
    {
        using var _ = new CurrentCultureScope("en-US");

        await base.Parse_with_constant();
    }

    public override async Task Parse_with_parameter()
    {
        using var _ = new CurrentCultureScope("en-US");

        await base.Parse_with_parameter();
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

    private sealed class CurrentCultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture;
        private readonly CultureInfo _originalUICulture;

        public CurrentCultureScope(string cultureName)
        {
            _originalCulture = CultureInfo.CurrentCulture;
            _originalUICulture = CultureInfo.CurrentUICulture;

            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUICulture;
        }
    }
}
