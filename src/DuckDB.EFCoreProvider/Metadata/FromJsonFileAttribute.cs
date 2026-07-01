namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     Maps an entity type to a JSON file path or glob pattern.
/// </summary>
/// <remarks>
///     When applied, DuckDB query SQL generation translates table references for the annotated entity type to
///     DuckDB <c>read_json(...)</c> using the supplied path (DuckDB auto-detects the structure and column
///     types). The path should be a DuckDB-compatible path or glob pattern and any embedded quotes are escaped
///     during SQL generation.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FromJsonFileAttribute : DuckDBFileSourceAttribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="FromJsonFileAttribute" /> class.
    /// </summary>
    /// <param name="path">The JSON file path or glob pattern used for DuckDB <c>read_json(...)</c>.</param>
    public FromJsonFileAttribute(string path)
        : base("read_json", path)
    {
    }
}
