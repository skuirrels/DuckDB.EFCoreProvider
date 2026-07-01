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
}
