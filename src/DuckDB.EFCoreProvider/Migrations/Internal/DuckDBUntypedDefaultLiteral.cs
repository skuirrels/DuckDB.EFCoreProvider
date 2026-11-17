using System.Globalization;

namespace DuckDB.EFCoreProvider.Migrations.Internal;

/// <summary>How a column default value is written for an engine that accepts only untyped default literals.</summary>
/// <param name="IntegerValue">
///     The value as a CLR integer, when its own type mapping already renders a plain numeric literal.
/// </param>
/// <param name="Text">
///     Otherwise, the invariant text form of the value, rendered as a string literal that the engine casts to the
///     column type.
/// </param>
internal readonly record struct DuckDBUntypedDefaultLiteral(object? IntegerValue, string? Text)
{
    /// <summary>
    ///     Plans the literal for a column default. DuckLake accepts integer and string literals as column defaults but
    ///     rejects typed literals such as <c>false</c>, <c>TIMESTAMP '...'</c> or <c>CAST(...)</c> as "non-literal"; a
    ///     string literal in DuckDB's text form is cast to the column type instead.
    /// </summary>
    internal static DuckDBUntypedDefaultLiteral Plan(object value, string? table, string? column)
    {
        if (value.GetType().IsEnum)
        {
            value = Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture);
        }

        return value switch
        {
            sbyte or byte or short or ushort or int or uint or long or ulong => new(value, null),
            string text => Text(text),
            char character => Text(character.ToString()),
            bool boolean => Text(boolean ? "true" : "false"),
            decimal number => Text(number.ToString(CultureInfo.InvariantCulture)),
            double number => Text(number.ToString("R", CultureInfo.InvariantCulture)),
            float number => Text(number.ToString("R", CultureInfo.InvariantCulture)),
            Guid guid => Text(guid.ToString("D")),
            DateTime dateTime => Text(dateTime.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture)),
            DateTimeOffset dateTimeOffset => Text(
                dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture)),
            DateOnly date => Text(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            TimeOnly time => Text(time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture)),
            TimeSpan span => Text(
                (span.Ticks / TimeSpan.TicksPerMicrosecond).ToString(CultureInfo.InvariantCulture) + " microseconds"),
            byte[] bytes => Text(string.Concat(bytes.Select(b => "\\x" + b.ToString("X2", CultureInfo.InvariantCulture)))),
            _ => throw new NotSupportedException(
                $"The default value {(column is null ? "" : $"for column '{table}.{column}' ")}has type "
                + $"'{value.GetType().Name}', which the configured "
                + "DuckDB engine cannot store as a column default: it accepts only numeric and string default literals. "
                + "Remove the default, or assign the value in the application.")
        };

        static DuckDBUntypedDefaultLiteral Text(string text) => new(null, text);
    }
}
