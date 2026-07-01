namespace DuckDB.EFCoreProvider.Metadata.Internal;

internal static class DuckDBSequenceNameHelper
{
    public static string GenerateSequenceNameLiteral(string name, string? schema)
    {
        var sequenceName = schema is null
            ? QuoteIdentifier(name)
            : QuoteIdentifier(schema) + "." + QuoteIdentifier(name);

        return "'" + sequenceName.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string QuoteIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
