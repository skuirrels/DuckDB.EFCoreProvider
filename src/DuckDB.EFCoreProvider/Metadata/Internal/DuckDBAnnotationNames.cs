namespace DuckDB.EFCoreProvider.Metadata.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public static class DuckDBAnnotationNames
{
    public const string Prefix = "DuckDB:";

    public const string ValueGenerationStrategy = Prefix + "ValueGenerationStrategy";

    public const string FileSourceFunction = Prefix + "FileSourceFunction";

    /// <summary>
    ///     The file path or glob pattern for a file-backed entity type (read via <see cref="FileSourceFunction" />).
    ///     This is an internal API that supports the EF Core infrastructure and may change without notice.
    /// </summary>
    public const string FileSourcePath = Prefix + "FileSourcePath";

    /// <summary>
    ///     The root directory of the cold Parquet archive for a tiered-storage entity. Internal API.
    /// </summary>
    public const string TieredStoreArchivePath = Prefix + "TieredStore:ArchivePath";

    /// <summary>
    ///     The name of the temporal (timestamp) property that partitions a tiered-storage entity between the
    ///     hot table and the cold Parquet archive. Internal API.
    /// </summary>
    public const string TieredStoreTimestamp = Prefix + "TieredStore:Timestamp";

    /// <summary>
    ///     The <see cref="TierGranularity" /> (stored by name) used to partition the cold archive. Internal API.
    /// </summary>
    public const string TieredStoreGranularity = Prefix + "TieredStore:Granularity";

    /// <summary>
    ///     The name of the DuckDB view that unions the hot table and the cold archive for a tiered-storage
    ///     entity. This is also the shared-type entity name of the tiered read model. Internal API.
    /// </summary>
    public const string TieredStoreView = Prefix + "TieredStore:View";

    /// <summary>
    ///     The key identifying a tiered-storage entity's row in the tier control table. Internal API.
    /// </summary>
    public const string TieredStoreControlKey = Prefix + "TieredStore:ControlKey";

    /// <summary>
    ///     The role of a tiered-storage hot entity within its aggregate: <c>Root</c> or <c>Child</c>. Internal API.
    /// </summary>
    public const string TieredStoreRole = Prefix + "TieredStore:Role";

    /// <summary>
    ///     The name of the immediate parent hot entity type of a tiered-storage child. Internal API.
    /// </summary>
    public const string TieredStoreParent = Prefix + "TieredStore:Parent";

    /// <summary>
    ///     The name of the aggregate-root hot entity type a tiered-storage child belongs to. Internal API.
    /// </summary>
    public const string TieredStoreRoot = Prefix + "TieredStore:Root";

    /// <summary>
    ///     The collection navigation on the parent that points to a tiered-storage child (used to resolve the
    ///     foreign key for the archive join). Internal API.
    /// </summary>
    public const string TieredStoreParentNavigation = Prefix + "TieredStore:ParentNavigation";

    /// <summary>
    ///     Whether child union views include the "root is hot" semijoin guard (default <see langword="true" />).
    ///     Internal API.
    /// </summary>
    public const string TieredStoreHotChildFilter = Prefix + "TieredStore:HotChildFilter";
}
