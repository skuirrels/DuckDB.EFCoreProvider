namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

/// <summary>
///     Immutable provider configuration for an attached DuckLake catalog.
/// </summary>
internal sealed record DuckLakeOptions
{
    public const string DefaultCatalogName = "ducklake";

    public string? MetadataSource { get; init; }

    public bool UsesSecret { get; init; }

    public string CatalogName { get; init; } = DefaultCatalogName;

    public string? DataPath { get; init; }

    public bool IsReadOnly { get; init; }

    public bool CreateIfNotExists { get; init; } = true;

    public bool AutomaticMigration { get; init; }

    public bool OverrideDataPath { get; init; }

    public long? SnapshotVersion { get; init; }

    public DateTimeOffset? SnapshotTime { get; init; }

    public IReadOnlyList<DuckLakeOptions> AdditionalCatalogs { get; init; } = [];

    public DuckLakeOptions AsReadOnly()
        => this with
        {
            IsReadOnly = true,
            CreateIfNotExists = false,
            AutomaticMigration = false,
            AdditionalCatalogs = AdditionalCatalogs.Select(catalog => catalog.AsReadOnly()).ToArray()
        };
}