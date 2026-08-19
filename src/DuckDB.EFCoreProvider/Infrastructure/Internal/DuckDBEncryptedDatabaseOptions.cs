namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

/// <summary>
///     Immutable provider configuration for an encrypted DuckDB database file attached to the in-memory host.
/// </summary>
/// <remarks>
///     DuckDB accepts an encryption key only as an <c>ATTACH</c> parameter, so an encrypted file cannot be
///     opened as the connection string's data source. The provider therefore hosts an in-memory database and
///     attaches the encrypted file to it. Only the key provider is stored here; the key itself is resolved on
///     each attachment and is never persisted in the options.
/// </remarks>
internal sealed record DuckDBEncryptedDatabaseOptions
{
    /// <summary>The catalog alias used when a name cannot be derived from the database file name.</summary>
    public const string FallbackCatalogName = "encrypted";

    /// <summary>DuckDB catalog aliases that are always present and cannot host an attached database.</summary>
    private static readonly string[] ReservedCatalogNames = ["memory", "system", "temp"];

    /// <summary>The encrypted DuckDB database file.</summary>
    public required string Path { get; init; }

    /// <summary>Resolves the AES encryption key. It is invoked per attachment and per attachment verification.</summary>
    public required Func<string> KeyProvider { get; init; }

    /// <summary>The alias the encrypted database is attached under and selected as the default catalog.</summary>
    public string CatalogName { get; init; } = FallbackCatalogName;

    /// <summary><see langword="true" /> when the database is attached read-only.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    ///     <see langword="true" /> when <c>temp_file_encryption</c> is enabled on connection open so query
    ///     results spilled to the temporary directory are encrypted as well.
    /// </summary>
    public bool EncryptTemporaryFiles { get; init; } = true;

    /// <summary>Resolves the encryption key for one attachment.</summary>
    /// <exception cref="InvalidOperationException">The configured provider returned no key.</exception>
    public string ResolveKey()
    {
        var key = KeyProvider();
        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException(
                "The encrypted database key provider returned no key. Return the key from the configured secret "
                + "store, or fail the resolution so the connection is not opened without encryption.");
        }

        return key;
    }

    /// <summary>Derives a safe catalog alias from the database file name, mirroring DuckDB's own ATTACH default.</summary>
    public static string DeriveCatalogName(string path)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(stem))
        {
            return FallbackCatalogName;
        }

        var candidate = new string(
            [.. stem.Select(character => char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_')]);

        if (char.IsAsciiDigit(candidate[0]))
        {
            candidate = '_' + candidate;
        }

        return IsReservedCatalogName(candidate) ? candidate + "_db" : candidate;
    }

    /// <summary><see langword="true" /> when the alias belongs to a built-in DuckDB catalog.</summary>
    public static bool IsReservedCatalogName(string catalogName)
        => ReservedCatalogNames.Contains(catalogName, StringComparer.OrdinalIgnoreCase);
}
