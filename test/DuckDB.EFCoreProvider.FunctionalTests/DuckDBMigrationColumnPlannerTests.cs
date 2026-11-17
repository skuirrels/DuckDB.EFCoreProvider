using DuckDB.EFCoreProvider.Migrations.Internal;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class DuckDBMigrationColumnPlannerTests
{
    [Theory]
    [InlineData("INTEGER", "INTEGER")]
    [InlineData("int", "INTEGER")]
    [InlineData("VARCHAR(50)", "VARCHAR(200)")]
    [InlineData("TEXT", "VARCHAR")]
    [InlineData("DECIMAL", "DECIMAL(18,3)")]
    [InlineData("NUMERIC(10, 2)", "DECIMAL(10,2)")]
    [InlineData("INTEGER[]", "integer[]")]
    public void Same_type_spellings_are_unchanged(string oldType, string newType)
        => Assert.Equal(DuckDBColumnTypeChange.Unchanged, DuckDBColumnTypePromotion.Classify(oldType, newType));

    [Theory]
    [InlineData("TINYINT", "SMALLINT")]
    [InlineData("INTEGER", "BIGINT")]
    [InlineData("BIGINT", "HUGEINT")]
    [InlineData("UTINYINT", "USMALLINT")]
    [InlineData("UINTEGER", "BIGINT")]
    [InlineData("INTEGER", "DOUBLE")]
    [InlineData("SMALLINT", "DECIMAL(9,2)")]
    [InlineData("INTEGER", "DECIMAL(18,2)")]
    [InlineData("FLOAT", "DOUBLE")]
    [InlineData("DECIMAL(10,2)", "DECIMAL(12,2)")]
    [InlineData("DECIMAL(9,2)", "DECIMAL(18,4)")]
    [InlineData("DECIMAL(18,2)", "DOUBLE")]
    [InlineData("BOOLEAN", "INTEGER")]
    [InlineData("DATE", "TIMESTAMP")]
    [InlineData("TIMESTAMP", "TIMESTAMPTZ")]
    public void DuckLake_widening_promotions_are_applied_in_place(string oldType, string newType)
        => Assert.Equal(DuckDBColumnTypeChange.Widening, DuckDBColumnTypePromotion.Classify(oldType, newType));

    [Theory]
    [InlineData("BIGINT", "INTEGER")]
    [InlineData("INTEGER", "UINTEGER")]
    [InlineData("UINTEGER", "INTEGER")]
    [InlineData("DOUBLE", "FLOAT")]
    [InlineData("INTEGER", "DECIMAL(9,2)")]
    [InlineData("DECIMAL(12,2)", "DECIMAL(12,3)")]
    [InlineData("DECIMAL(18,4)", "DECIMAL(38,2)")]
    [InlineData("VARCHAR", "INTEGER")]
    [InlineData("INTEGER", "VARCHAR")]
    [InlineData("TIMESTAMP", "DATE")]
    [InlineData("INTEGER[]", "BIGINT[]")]
    [InlineData("STRUCT(\"A\" INTEGER)", "STRUCT(\"A\" BIGINT)")]
    public void Narrowing_cross_type_and_nested_changes_are_conversions(string oldType, string newType)
        => Assert.Equal(DuckDBColumnTypeChange.Conversion, DuckDBColumnTypePromotion.Classify(oldType, newType));

    public static TheoryData<object, string> TextDefaults => new()
    {
        { true, "true" },
        { false, "false" },
        { "it's", "it's" },
        { 'x', "x" },
        { 1.5m, "1.5" },
        { 0.25d, "0.25" },
        { new DateTime(2026, 1, 2, 3, 4, 5), "2026-01-02 03:04:05" },
        { new DateTime(2026, 1, 2, 3, 4, 5, 120), "2026-01-02 03:04:05.12" },
        { new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), "2026-01-02 03:04:05+02:00" },
        { new DateOnly(2026, 1, 2), "2026-01-02" },
        { new TimeOnly(3, 4, 5), "03:04:05" },
        { TimeSpan.FromSeconds(90), "90000000 microseconds" },
        { Guid.Empty, "00000000-0000-0000-0000-000000000000" },
        { new byte[] { 0x0A, 0xFF }, "\\x0A\\xFF" }
    };

    [Theory]
    [MemberData(nameof(TextDefaults))]
    public void Non_integer_defaults_are_planned_as_text_literals(object value, string text)
    {
        var literal = DuckDBUntypedDefaultLiteral.Plan(value, "Items", "Value");

        Assert.Null(literal.IntegerValue);
        Assert.Equal(text, literal.Text);
    }

    [Theory]
    [InlineData(42, 42)]
    [InlineData(42L, 42L)]
    [InlineData((byte)7, (byte)7)]
    [InlineData(DayOfWeek.Friday, 5)]
    public void Integer_and_enum_defaults_keep_their_numeric_literal(object value, object integer)
    {
        var literal = DuckDBUntypedDefaultLiteral.Plan(value, "Items", "Value");

        Assert.Equal(integer, literal.IntegerValue);
        Assert.Null(literal.Text);
    }

    [Fact]
    public void Defaults_without_a_literal_form_are_rejected_with_the_column_name()
    {
        var exception = Assert.Throws<NotSupportedException>(
            () => DuckDBUntypedDefaultLiteral.Plan(new[] { 1, 2 }, "Items", "Tags"));

        Assert.Contains("'Items.Tags'", exception.Message);
        Assert.Contains("numeric and string default literals", exception.Message);
    }
}