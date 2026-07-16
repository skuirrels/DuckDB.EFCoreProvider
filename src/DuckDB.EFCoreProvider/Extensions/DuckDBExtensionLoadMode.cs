namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>Controls how a configured DuckDB extension is prepared when a provider-owned connection opens.</summary>
public enum DuckDBExtensionLoadMode
{
    /// <summary>Run <c>INSTALL</c> and then <c>LOAD</c>. This preserves the provider's historical behaviour.</summary>
    InstallAndLoad,

    /// <summary>Run <c>LOAD</c> only. Use this in offline or immutable environments with preinstalled extensions.</summary>
    LoadOnly,

    /// <summary>
    ///     Record the dependency without running extension SQL. The consuming application owns installation
    ///     and loading, normally through a connection initializer or deployment image.
    /// </summary>
    CallerManaged,
}

/// <summary>One validated DuckDB extension dependency and its connection-open behaviour.</summary>
public readonly record struct DuckDBExtensionConfiguration(
    string Name,
    DuckDBExtensionLoadMode Mode);