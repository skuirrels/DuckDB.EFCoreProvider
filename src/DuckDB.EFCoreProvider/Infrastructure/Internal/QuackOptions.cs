using DuckDB.EFCoreProvider.Extensions;

namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

/// <summary>
///     Immutable provider configuration for an experimental Quack remote profile.
/// </summary>
internal sealed record QuackOptions
{
    public const string DefaultCatalogName = "__ef_quack";

    public required string Endpoint { get; init; }

    public required string Token { get; init; }

    public string CatalogName { get; init; } = DefaultCatalogName;

    public bool DisableSsl { get; init; }

    public bool EnableHttpConnectionCaching { get; init; } = true;

    public DuckDBExtensionLoadMode ExtensionLoadMode { get; init; } = DuckDBExtensionLoadMode.InstallAndLoad;

    public string? ExtensionPath { get; init; }
}