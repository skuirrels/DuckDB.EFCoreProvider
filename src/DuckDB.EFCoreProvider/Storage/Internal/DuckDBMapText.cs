using System.Globalization;
using System.Numerics;
using System.Text;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     Writes MAP parameter values in DuckDB's text form, <c>{'key'='value', ...}</c>, which DuckDB casts to the
///     target MAP type. Every non-null element is single-quoted with backslash escapes, so separators, quotes and
///     the word NULL inside a value are never parsed as structure.
/// </summary>
internal static class DuckDBMapText
{
    private static readonly HashSet<Type> SupportedElementTypes =
    [
        typeof(string),
        typeof(bool),
        typeof(sbyte),
        typeof(byte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(Guid),
        typeof(DateTime),
        typeof(DateOnly)
    ];

    /// <summary>Whether a provider element type can be written as MAP text and read back by DuckDB.NET.</summary>
    internal static bool IsSupportedElementType(Type providerType)
        => SupportedElementTypes.Contains(providerType.UnwrapNullableType());

    internal static void AppendElement(StringBuilder builder, object? value)
    {
        if (value is null)
        {
            builder.Append("NULL");
            return;
        }

        AppendQuoted(builder, Format(value));
    }

    private static string Format(object value)
        => value switch
        {
            string text => text,
            bool boolean => boolean ? "true" : "false",
            float single => FormatFloatingPoint(single),
            double number => FormatFloatingPoint(number),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => throw new NotSupportedException(
                $"DuckDB MAP parameters do not support elements of type '{value.GetType()}'.")
        };

    private static string FormatFloatingPoint<T>(T value)
        where T : struct, IFloatingPointIeee754<T>
        => T.IsNaN(value)
            ? "nan"
            : T.IsPositiveInfinity(value)
                ? "inf"
                : T.IsNegativeInfinity(value)
                    ? "-inf"
                    : value.ToString("R", CultureInfo.InvariantCulture);

    private static void AppendQuoted(StringBuilder builder, string text)
    {
        builder.Append('\'');
        foreach (var character in text)
        {
            if (character is '\'' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        builder.Append('\'');
    }
}