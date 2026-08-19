using DuckDB.EFCoreProvider.Infrastructure.Internal;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     Builds the statements that attach an encrypted DuckDB database file to the in-memory host connection and
///     select it as the default catalog.
/// </summary>
/// <remarks>
///     The encryption key can only be passed as an <c>ATTACH</c> parameter literal: DuckDB has no secret type or
///     prepared-statement form for it. The generated text therefore contains the key and is executed directly on
///     the <see cref="System.Data.Common.DbConnection" />, outside EF Core's command pipeline, so it is never
///     written to EF logs, diagnostics, or interceptors.
/// </remarks>
internal static class DuckDBEncryptedAttachCommandBuilder
{
    /// <summary>Builds the <c>ATTACH</c> statement for the configured database.</summary>
    /// <remarks>
    ///     <c>IF NOT EXISTS</c> makes the statement idempotent: connections sharing the in-memory host instance
    ///     check for the attachment and attach without a lock between the two steps, so several of them can race
    ///     to attach the same database. DuckDB matches the alias rather than the file, so the caller must still
    ///     confirm the resulting attachment is the configured database.
    /// </remarks>
    public static string BuildAttachment(DuckDBEncryptedDatabaseOptions options, string key)
    {
        var parameters = options.IsReadOnly ? ", READ_ONLY" : string.Empty;

        return $"ATTACH IF NOT EXISTS '{EscapeLiteral(options.Path)}' AS {DelimitIdentifier(options.CatalogName)} "
            + $"(ENCRYPTION_KEY {KeyLiteral(key)}{parameters});";
    }

    /// <summary>
    ///     Builds the SQL literal the attachment writes the key as. Redaction matches on this exact form, so
    ///     that knowledge of how the key reaches the statement stays with the code that puts it there.
    /// </summary>
    public static string KeyLiteral(string key)
        => $"'{EscapeLiteral(key)}'";

    /// <summary>Builds the statement that makes the encrypted database the connection's default catalog.</summary>
    public static string BuildUse(DuckDBEncryptedDatabaseOptions options)
        => $"USE {DelimitIdentifier(options.CatalogName)};";

    /// <summary>
    ///     Builds the statements that release the encrypted database from the host instance: the default catalog
    ///     moves back to the in-memory host first, because DuckDB refuses to detach the current catalog.
    /// </summary>
    public static string BuildDetach(DuckDBEncryptedDatabaseOptions options)
        => $"USE memory; DETACH {DelimitIdentifier(options.CatalogName)};";

    /// <summary>Builds the statement that encrypts temporary files spilled by queries.</summary>
    public static string BuildTemporaryFileEncryption()
        => "SET temp_file_encryption = true;";

    private static string EscapeLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static string DelimitIdentifier(string identifier)
        => '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
}
