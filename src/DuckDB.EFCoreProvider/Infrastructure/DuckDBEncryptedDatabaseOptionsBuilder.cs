using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Infrastructure;

/// <summary>
///     Configures the encrypted DuckDB database attached by the provider.
/// </summary>
/// <remarks>
///     Instances are supplied to the configuration action of
///     <see cref="DuckDBDbContextOptionsBuilder.UseEncryptedDatabase" /> and are not designed to be constructed
///     in application code.
/// </remarks>
public sealed class DuckDBEncryptedDatabaseOptionsBuilder
{
    private readonly DbContextOptionsBuilder _optionsBuilder;

    internal DuckDBEncryptedDatabaseOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
        => _optionsBuilder = optionsBuilder;

    /// <summary>
    ///     Sets the alias the encrypted database is attached under. The default is derived from the database
    ///     file name, so <c>/var/lib/app/secure.duckdb</c> is attached as <c>secure</c>.
    /// </summary>
    /// <remarks>
    ///     The alias is the context's default catalog, so entities and the migrations history table are created
    ///     inside the encrypted file. Give each encrypted database its own alias when one process configures
    ///     several of them: they share a single DuckDB host instance, and a reused alias is rejected on attach.
    /// </remarks>
    /// <param name="catalogName">The safe identifier used for the attached catalog.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckDBEncryptedDatabaseOptionsBuilder CatalogName(string catalogName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogName);
        DuckLakeDbContextOptionsBuilder.ValidateIdentifier(catalogName, nameof(catalogName), "DuckDB catalog");
        if (DuckDBEncryptedDatabaseOptions.IsReservedCatalogName(catalogName))
        {
            throw new ArgumentException(
                "'memory', 'system', and 'temp' are built-in DuckDB catalogs and cannot be used as the alias for "
                + "an attached encrypted database.",
                nameof(catalogName));
        }

        return WithOption(options => options with { CatalogName = catalogName });
    }

    /// <summary>Attaches the encrypted database read-only.</summary>
    /// <remarks>
    ///     A read-only attachment requires an existing file: DuckDB does not create one. The mode applies to the
    ///     attachment, so it is fixed by whichever context in the host instance attaches the database first.
    /// </remarks>
    /// <param name="readOnly">Whether the database should be attached read-only.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckDBEncryptedDatabaseOptionsBuilder ReadOnly(bool readOnly = true)
        => WithOption(options => options with { IsReadOnly = readOnly });

    /// <summary>
    ///     Controls whether DuckDB encrypts the temporary files it spills query results to. Enabled by default.
    /// </summary>
    /// <remarks>
    ///     DuckDB leaves <c>temp_file_encryption</c> off by default, and a query that spills writes row data to
    ///     the temporary directory in the clear even when it reads from an encrypted database. The provider
    ///     therefore enables the setting on every connection it opens for an encrypted database. The setting is
    ///     instance-wide, so it also applies to other contexts sharing the DuckDB host instance.
    /// </remarks>
    /// <param name="encryptTemporaryFiles">Whether spilled temporary files are encrypted.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckDBEncryptedDatabaseOptionsBuilder EncryptTemporaryFiles(bool encryptTemporaryFiles = true)
        => WithOption(options => options with { EncryptTemporaryFiles = encryptTemporaryFiles });

    private DuckDBEncryptedDatabaseOptionsBuilder WithOption(
        Func<DuckDBEncryptedDatabaseOptions, DuckDBEncryptedDatabaseOptions> setAction)
    {
        var infrastructure = (IDbContextOptionsBuilderInfrastructure)_optionsBuilder;
        var extension = _optionsBuilder.Options.FindExtension<DuckDBOptionsExtension>()
            ?? throw new InvalidOperationException("Configure DuckDB before configuring an encrypted database.");
        var encryptedDatabase = extension.EncryptedDatabase
            ?? throw new InvalidOperationException(
                "Call UseEncryptedDatabase(path, keyProvider) before configuring the encrypted database.");

        infrastructure.AddOrUpdateExtension(extension.WithEncryptedDatabase(setAction(encryptedDatabase)));
        return this;
    }
}
