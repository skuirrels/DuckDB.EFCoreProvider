using System.Collections;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>Expands ADO parameters into DuckDB literals for Quack's query-text-only prepare request.</summary>
internal static class QuackSqlTextBuilder
{
    internal static string ExpandParameters(string commandText, DbParameterCollection parameters)
    {
        if (parameters.Count == 0)
        {
            return commandText;
        }

        var named = new Dictionary<string, DbParameter>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<DbParameter>(parameters.Count);
        foreach (DbParameter parameter in parameters)
        {
            if (parameter.Direction is System.Data.ParameterDirection.Output
                or System.Data.ParameterDirection.InputOutput
                or System.Data.ParameterDirection.ReturnValue)
            {
                throw new NotSupportedException("Quack commands support input parameters only.");
            }

            positional.Add(parameter);
            var name = parameter.ParameterName.TrimStart('$', '@', ':');
            if (name.Length > 0)
            {
                named[name] = parameter;
            }
        }

        var result = new StringBuilder(commandText.Length + parameters.Count * 16);
        var positionalIndex = 0;
        for (var index = 0; index < commandText.Length;)
        {
            var current = commandText[index];
            if (current == '\'' || current == '"')
            {
                AppendQuoted(
                    commandText,
                    result,
                    ref index,
                    current,
                    current == '\'' && IsEscapeStringPrefix(commandText, index));
                continue;
            }

            if (current == '-' && index + 1 < commandText.Length && commandText[index + 1] == '-')
            {
                AppendLineComment(commandText, result, ref index);
                continue;
            }

            if (current == '/' && index + 1 < commandText.Length && commandText[index + 1] == '*')
            {
                AppendBlockComment(commandText, result, ref index);
                continue;
            }

            if (current == '$' && TryAppendDollarQuoted(commandText, result, ref index))
            {
                continue;
            }

            if (current == '?' && positionalIndex < positional.Count)
            {
                result.Append(GenerateLiteral(positional[positionalIndex++]));
                index++;
                continue;
            }

            if (current is '$' or '@' or ':'
                && !(current == ':' && index > 0 && commandText[index - 1] == ':'))
            {
                var nameStart = index + 1;
                var end = nameStart;
                while (end < commandText.Length && IsParameterNameCharacter(commandText[end]))
                {
                    end++;
                }

                if (end > nameStart
                    && named.TryGetValue(commandText[nameStart..end], out var parameter))
                {
                    result.Append(GenerateLiteral(parameter));
                    index = end;
                    continue;
                }
            }

            result.Append(current);
            index++;
        }

        return result.ToString();
    }

    internal static string GenerateLiteral(DbParameter parameter)
        => GenerateLiteral(parameter.Value is DBNull ? null : parameter.Value);

    internal static string GenerateLiteral(object? value)
    {
        if (value is null or DBNull)
        {
            return "NULL";
        }

        var type = value.GetType();
        if (type.IsEnum)
        {
            return Convert.ToString(
                Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture)!;
        }

        return value switch
        {
            string text => Quote(text),
            char character => Quote(character.ToString()),
            bool boolean => boolean ? "TRUE" : "FALSE",
            byte or sbyte or short or ushort or int or uint or long or ulong or decimal
                => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            float single => FormatFloatingPoint(single, "FLOAT"),
            double number => FormatFloatingPoint(number, "DOUBLE"),
            DateOnly date => FormatTemporalLiteral("DATE", date, "yyyy-MM-dd"),
            DateTime timestamp => FormatTemporalLiteral("TIMESTAMP", timestamp, "yyyy-MM-dd HH:mm:ss.fffffff"),
            DateTimeOffset timestamp => FormatTemporalLiteral("TIMESTAMPTZ", timestamp, "yyyy-MM-dd HH:mm:ss.fffffffzzz"),
            TimeOnly time => FormatTemporalLiteral("TIME", time, "HH:mm:ss.fffffff"),
            TimeSpan time => FormatTemporalLiteral("TIME", TimeOnly.FromTimeSpan(time), "HH:mm:ss.fffffff"),
            Guid guid => $"UUID '{guid:D}'",
            byte[] bytes => $"from_hex('{Convert.ToHexString(bytes)}')",
            JsonDocument document => Quote(document.RootElement.GetRawText()) + "::JSON",
            JsonElement element => Quote(element.GetRawText()) + "::JSON",
            IDictionary dictionary => FormatDictionary(dictionary),
            ITuple tuple => FormatTuple(tuple),
            IEnumerable enumerable => FormatCollection(enumerable),
            _ => throw new NotSupportedException(
                $"Quack command replay cannot safely render a parameter of CLR type '{type.FullName}'.")
        };
    }

    internal static string Quote(string value)
        => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    internal static string QuoteIdentifier(string value)
        => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string FormatFloatingPoint<T>(T value, string storeType)
        where T : IFormattable
    {
        var number = value.ToString(null, CultureInfo.InvariantCulture);
        return number switch
        {
            "NaN" => $"'NaN'::{storeType}",
            "Infinity" => $"'Infinity'::{storeType}",
            "-Infinity" => $"'-Infinity'::{storeType}",
            _ => number
        };
    }

    private static string FormatTemporalLiteral(string storeType, IFormattable value, string format)
        => $"{storeType} '{value.ToString(format, CultureInfo.InvariantCulture)}'";

    private static string FormatCollection(IEnumerable values)
    {
        var literals = new List<string>();
        foreach (var value in values)
        {
            literals.Add(GenerateLiteral(value));
        }

        return $"[{string.Join(", ", literals)}]";
    }

    private static string FormatDictionary(IDictionary values)
    {
        var fields = new List<string>();
        foreach (DictionaryEntry value in values)
        {
            fields.Add($"{Quote(Convert.ToString(value.Key, CultureInfo.InvariantCulture)!)}: {GenerateLiteral(value.Value)}");
        }

        return $"{{{string.Join(", ", fields)}}}";
    }

    private static string FormatTuple(ITuple tuple)
    {
        var values = new string[tuple.Length];
        for (var index = 0; index < tuple.Length; index++)
        {
            values[index] = GenerateLiteral(tuple[index]);
        }

        return $"({string.Join(", ", values)})";
    }

    private static void AppendQuoted(
        string sql,
        StringBuilder result,
        ref int index,
        char quote,
        bool backslashEscapes)
    {
        result.Append(quote);
        index++;
        while (index < sql.Length)
        {
            var current = sql[index++];
            result.Append(current);
            if (backslashEscapes && current == '\\' && index < sql.Length)
            {
                result.Append(sql[index++]);
                continue;
            }

            if (current != quote)
            {
                continue;
            }

            if (index < sql.Length && sql[index] == quote)
            {
                result.Append(sql[index++]);
                continue;
            }

            return;
        }
    }

    private static bool IsEscapeStringPrefix(string sql, int quoteIndex)
        => quoteIndex > 0
           && sql[quoteIndex - 1] is 'E' or 'e'
           && (quoteIndex == 1 || !IsParameterNameCharacter(sql[quoteIndex - 2]));

    private static void AppendLineComment(string sql, StringBuilder result, ref int index)
    {
        while (index < sql.Length)
        {
            var current = sql[index++];
            result.Append(current);
            if (current == '\n')
            {
                return;
            }
        }
    }

    private static void AppendBlockComment(string sql, StringBuilder result, ref int index)
    {
        result.Append("/*");
        index += 2;
        var depth = 1;
        while (index < sql.Length)
        {
            if (index + 1 < sql.Length && sql[index] == '/' && sql[index + 1] == '*')
            {
                result.Append("/*");
                index += 2;
                depth++;
                continue;
            }

            if (index + 1 < sql.Length && sql[index] == '*' && sql[index + 1] == '/')
            {
                result.Append("*/");
                index += 2;
                if (--depth == 0)
                {
                    return;
                }

                continue;
            }

            var current = sql[index++];
            result.Append(current);
        }
    }

    private static bool TryAppendDollarQuoted(string sql, StringBuilder result, ref int index)
    {
        var delimiterEnd = index + 1;
        if (delimiterEnd >= sql.Length)
        {
            return false;
        }

        if (sql[delimiterEnd] != '$')
        {
            if (!char.IsAsciiLetter(sql[delimiterEnd]) && sql[delimiterEnd] != '_')
            {
                return false;
            }

            delimiterEnd++;
            while (delimiterEnd < sql.Length && IsParameterNameCharacter(sql[delimiterEnd]))
            {
                delimiterEnd++;
            }

            if (delimiterEnd >= sql.Length || sql[delimiterEnd] != '$')
            {
                return false;
            }
        }

        var delimiter = sql[index..(delimiterEnd + 1)];
        var contentStart = delimiterEnd + 1;
        var closing = sql.IndexOf(delimiter, contentStart, StringComparison.Ordinal);
        if (closing < 0)
        {
            result.Append(sql, index, sql.Length - index);
            index = sql.Length;
            return true;
        }

        var end = closing + delimiter.Length;
        result.Append(sql, index, end - index);
        index = end;
        return true;
    }

    private static bool IsParameterNameCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character == '_';
}