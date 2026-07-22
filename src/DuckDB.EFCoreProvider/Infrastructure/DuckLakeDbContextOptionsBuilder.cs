using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Infrastructure;

/// <summary>
///     Configures the DuckLake catalog attached by the DuckDB EF Core provider.
/// </summary>
/// <remarks>
///     Credentials should be supplied through a DuckDB secret created by
///     <see cref="DuckDBDbContextOptionsBuilder.ConfigureConnection" />. The profile stores only the secret name.
/// </remarks>
public sealed class DuckLakeDbContextOptionsBuilder
{
    private readonly DbContextOptionsBuilder _optionsBuilder;

    internal DuckLakeDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
        => _optionsBuilder = optionsBuilder;

    /// <summary>Uses a local DuckDB file as the DuckLake metadata catalog.</summary>
    /// <remarks>
    ///     Remote metadata sources must be configured through <see cref="UseNamedSecret" /> or
    ///     <see cref="UseDefaultSecret" /> so credentials remain on the initialized connection.
    /// </remarks>
    /// <param name="metadataPath">The local metadata file path.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckLakeDbContextOptionsBuilder UseLocalMetadata(string metadataPath)
    {
        DuckLakeMetadataSourceValidator.ValidateLocalPath(metadataPath, nameof(metadataPath));

        return WithOption(options => options with { MetadataSource = metadataPath, UsesSecret = false });
    }

    /// <summary>Reads the metadata and data-storage configuration from the default unnamed DuckDB secret.</summary>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckLakeDbContextOptionsBuilder UseDefaultSecret()
        => WithOption(options => options with { MetadataSource = string.Empty, UsesSecret = true });

    /// <summary>Reads the metadata and data-storage configuration from a named DuckDB secret.</summary>
    /// <remarks>
    ///     <para>
    ///         Create a <c>TYPE ducklake</c> secret in
    ///         <see cref="DuckDBDbContextOptionsBuilder.ConfigureConnection" />. The secret shape depends on the
    ///         metadata backend. For example, a PostgreSQL-backed catalog may use:
    ///     </para>
    ///     <code>
    /// CREATE SECRET my_profile (
    ///     TYPE ducklake,
    ///     METADATA_PATH '',
    ///     DATA_PATH 's3://bucket/lake/',
    ///     METADATA_PARAMETERS MAP {'TYPE': 'postgres', 'SECRET': 'my_pg_credentials'});
    ///     </code>
    ///     <para>
    ///         Create the referenced PostgreSQL and object-storage secrets on the same connection. The EF options
    ///         store only <c>my_profile</c>, never its credentials.
    ///     </para>
    /// </remarks>
    /// <param name="secretName">The secret name. Credentials are not copied into this profile.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckLakeDbContextOptionsBuilder UseNamedSecret(string secretName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        ValidateIdentifier(secretName, nameof(secretName), "DuckLake secret");
        return WithOption(options => options with { MetadataSource = secretName, UsesSecret = true });
    }

    /// <summary>Sets the attached DuckDB catalog name. The default is <c>ducklake</c>.</summary>
    /// <param name="catalogName">The safe identifier used for the attached catalog.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckLakeDbContextOptionsBuilder CatalogName(string catalogName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogName);
        ValidateIdentifier(catalogName, nameof(catalogName), "DuckLake catalog");
        return WithOption(options => options with { CatalogName = catalogName });
    }

    /// <summary>Sets the DuckLake data-file location used when creating or attaching a catalog.</summary>
    /// <param name="dataPath">The local or object-storage data path.</param>
    /// <param name="overrideForCurrentConnection">
    ///     Whether connections created by this profile may use <paramref name="dataPath" /> instead of the path stored in
    ///     DuckLake metadata. This does not modify the persisted catalog setting.
    /// </param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckLakeDbContextOptionsBuilder DataPath(string dataPath, bool overrideForCurrentConnection = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataPath);
        return WithOption(options => options with
        {
            DataPath = dataPath,
            OverrideDataPath = overrideForCurrentConnection
        });
    }

    /// <summary>Attaches the DuckLake in read-only mode.</summary>
    /// <remarks>
    ///     Scale reads with a separate read-only <see cref="DbContext" /> and connection per concurrent operation,
    ///     attached to a metadata catalog that supports the required client concurrency. A <see cref="DbContext" />
    ///     is not thread-safe, and this option does not create or manage replicas.
    /// </remarks>
    /// <param name="readOnly">Whether the catalog should be attached read-only.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckLakeDbContextOptionsBuilder ReadOnly(bool readOnly = true)
        => WithOption(options => options with
        {
            IsReadOnly = readOnly,
            CreateIfNotExists = readOnly ? false : options.CreateIfNotExists,
            AutomaticMigration = readOnly ? false : options.AutomaticMigration
        });

    /// <summary>Controls whether attaching may create a missing DuckLake. Enabled by default.</summary>
    /// <param name="createIfNotExists">Whether DuckLake may create missing metadata.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckLakeDbContextOptionsBuilder CreateIfNotExists(bool createIfNotExists = true)
        => WithOption(options => options with { CreateIfNotExists = createIfNotExists });

    /// <summary>Allows the DuckLake extension to upgrade an older metadata schema while attaching.</summary>
    /// <param name="automaticMigration">Whether attachment may upgrade the catalog metadata schema.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckLakeDbContextOptionsBuilder AutomaticMigration(bool automaticMigration = true)
        => WithOption(options => options with { AutomaticMigration = automaticMigration });

    /// <summary>
    ///     Attaches the catalog at one committed snapshot. Queries through the resulting context use normal EF LINQ
    ///     against a catalog-wide, read-only historical view.
    /// </summary>
    /// <param name="snapshotId">The non-negative DuckLake snapshot identifier.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckLakeDbContextOptionsBuilder AsOfSnapshot(long snapshotId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(snapshotId);
        return WithOption(options => options with
        {
            SnapshotVersion = snapshotId,
            SnapshotTime = null,
            IsReadOnly = true,
            CreateIfNotExists = false,
            AutomaticMigration = false
        });
    }

    /// <summary>
    ///     Attaches the catalog at the latest snapshot committed at or before a timestamp. Queries through the
    ///     resulting context use normal EF LINQ against a catalog-wide, read-only historical view.
    /// </summary>
    /// <param name="timestamp">The point in time used to select the historical snapshot.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckLakeDbContextOptionsBuilder AsOfTimestamp(DateTimeOffset timestamp)
        => WithOption(options => options with
        {
            SnapshotVersion = null,
            SnapshotTime = timestamp,
            IsReadOnly = true,
            CreateIfNotExists = false,
            AutomaticMigration = false
        });

    /// <summary>Adds another local DuckLake catalog to every connection created by this profile.</summary>
    /// <remarks>
    ///     Additional catalogs are read-only by default. They can be queried with catalog-qualified raw SQL. EF
    ///     entity mappings continue to target the primary catalog selected by <see cref="CatalogName" />.
    /// </remarks>
    /// <param name="catalogName">The safe DuckDB alias for the additional catalog.</param>
    /// <param name="metadataPath">The local DuckLake metadata file path.</param>
    /// <param name="readOnly">Whether the additional catalog is attached read-only.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckLakeDbContextOptionsBuilder AlsoAttach(
        string catalogName,
        string metadataPath,
        bool readOnly = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogName);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataPath);
        ValidateIdentifier(catalogName, nameof(catalogName), "DuckLake catalog");
        DuckLakeMetadataSourceValidator.ValidateLocalPath(metadataPath, nameof(metadataPath));

        return AddCatalog(
            new DuckLakeOptions
            {
                MetadataSource = metadataPath,
                CatalogName = catalogName,
                IsReadOnly = readOnly,
                CreateIfNotExists = !readOnly
            });
    }

    /// <summary>Adds another secret-backed DuckLake catalog to every connection created by this profile.</summary>
    /// <remarks>
    ///     <para>
    ///         Create the named <c>TYPE ducklake</c> secret in
    ///         <see cref="DuckDBDbContextOptionsBuilder.ConfigureConnection" />. Only the secret name is stored in
    ///         the EF options; metadata and data-storage credentials remain on the initialized connection.
    ///     </para>
    ///     <para>
    ///         Additional catalogs are read-only by default and are available to catalog-qualified raw or dynamic
    ///         SQL. EF entity mappings continue to target the primary catalog selected by <see cref="CatalogName" />.
    ///     </para>
    /// </remarks>
    /// <param name="catalogName">The safe DuckDB alias for the additional catalog.</param>
    /// <param name="secretName">The named DuckDB <c>TYPE ducklake</c> secret.</param>
    /// <param name="readOnly">Whether the additional catalog is attached read-only.</param>
    /// <returns>This builder so that further configuration can be chained.</returns>
    public DuckLakeDbContextOptionsBuilder AlsoAttachNamedSecret(
        string catalogName,
        string secretName,
        bool readOnly = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        ValidateIdentifier(catalogName, nameof(catalogName), "DuckLake catalog");
        ValidateIdentifier(secretName, nameof(secretName), "DuckLake secret");

        return AddCatalog(
            new DuckLakeOptions
            {
                MetadataSource = secretName,
                UsesSecret = true,
                CatalogName = catalogName,
                IsReadOnly = readOnly,
                CreateIfNotExists = !readOnly
            });
    }

    private DuckLakeDbContextOptionsBuilder AddCatalog(DuckLakeOptions catalog)
        => WithOption(options => options with
        {
            AdditionalCatalogs =
            [
                .. options.AdditionalCatalogs,
                catalog
            ]
        });

    private DuckLakeDbContextOptionsBuilder WithOption(Func<DuckLakeOptions, DuckLakeOptions> setAction)
    {
        var infrastructure = (IDbContextOptionsBuilderInfrastructure)_optionsBuilder;
        var extension = _optionsBuilder.Options.FindExtension<DuckDBOptionsExtension>()
            ?? throw new InvalidOperationException("Configure DuckDB before configuring DuckLake.");
        infrastructure.AddOrUpdateExtension(extension.WithDuckLakeOptions(setAction(extension.DuckLakeOptions ?? new DuckLakeOptions())));
        return this;
    }

    internal static void ValidateIdentifier(string identifier, string parameterName, string kind)
    {
        if (!char.IsAsciiLetter(identifier[0]) && identifier[0] != '_'
            || identifier.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException(
                $"{kind} names must start with an ASCII letter or underscore and contain only ASCII letters, digits, and underscores.",
                parameterName);
        }
    }

}