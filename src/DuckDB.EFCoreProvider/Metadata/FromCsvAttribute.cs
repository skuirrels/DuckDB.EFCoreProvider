namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     Maps an entity type to a CSV file path or glob pattern.
/// </summary>
/// <remarks>
///     When applied, DuckDB query SQL generation translates table references for the annotated entity type to
///     DuckDB <c>read_csv(...)</c> using the supplied path (DuckDB auto-detects the dialect and column types).
///     The path should be a DuckDB-compatible path or glob pattern and any embedded quotes are escaped during
///     SQL generation.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FromCsvAttribute : DuckDBFileSourceAttribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="FromCsvAttribute" /> class.
    /// </summary>
    /// <param name="path">The CSV file path or glob pattern used for DuckDB <c>read_csv(...)</c>.</param>
    public FromCsvAttribute(string path)
        : base("read_csv", path)
    {
    }
}
