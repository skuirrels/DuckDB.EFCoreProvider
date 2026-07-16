namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     One deterministic relationship path through which a mapped child participates in a tiered-storage
///     aggregate. A child can expose multiple bindings when it is owned by independently archived roots.
/// </summary>
/// <param name="BindingId">The stable, bounded technical identifier for this relationship path.</param>
/// <param name="RootEntityName">The EF Core entity-type name of the aggregate root.</param>
/// <param name="ParentEntityName">The EF Core entity-type name of the immediate parent.</param>
/// <param name="ParentNavigationName">The parent collection navigation used to reach the child.</param>
/// <param name="ParentBindingId">
///     The binding identifier of the immediate parent, or the root binding identifier for a direct child.
/// </param>
/// <param name="ArchivePath">The configured archive base path owned by the root.</param>
/// <param name="ControlKey">The root-scoped tier control key.</param>
public readonly record struct TieredStoreBinding(
    string BindingId,
    string RootEntityName,
    string ParentEntityName,
    string ParentNavigationName,
    string ParentBindingId,
    string ArchivePath,
    string ControlKey);
