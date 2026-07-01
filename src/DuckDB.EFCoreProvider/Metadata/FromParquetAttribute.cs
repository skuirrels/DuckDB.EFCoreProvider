namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     Maps an entity type to a parquet file path or glob pattern.
/// </summary>
/// <remarks>
///     When applied, DuckDB query SQL generation translates table references for the annotated entity type to
///     DuckDB <c>read_parquet(...)</c> using the supplied path. The path should be a DuckDB-compatible parquet
///     path or glob pattern and any embedded quotes are escaped during SQL generation.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class FromParquetAttribute : DuckDBFileSourceAttribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="FromParquetAttribute" /> class.
    /// </summary>
    /// <param name="path">The parquet file path or glob pattern used for DuckDB <c>read_parquet(...)</c>.</param>
    public FromParquetAttribute(string path)
        : base("read_parquet", path)
    {
    }
}
