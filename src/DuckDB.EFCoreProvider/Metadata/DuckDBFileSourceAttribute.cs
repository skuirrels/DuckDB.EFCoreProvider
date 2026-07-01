namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     Base class for attributes that map an entity type to a DuckDB file-reading table function
///     (for example <c>read_parquet</c>, <c>read_csv</c>, or <c>read_json</c>).
/// </summary>
/// <remarks>
///     When applied, DuckDB query SQL generation translates table references for the annotated entity type to
///     <c>&lt;function&gt;('&lt;path&gt;')</c>. The path may be a single file or a DuckDB-compatible glob
///     pattern; any embedded quotes are escaped during SQL generation. The function name is provider-defined
///     by the concrete attribute and is never taken from user input.
/// </remarks>
public abstract class DuckDBFileSourceAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DuckDBFileSourceAttribute" /> class.
    /// </summary>
    /// <param name="function">The DuckDB table function to read the file (e.g. <c>read_parquet</c>).</param>
    /// <param name="path">The file path or glob pattern.</param>
    protected DuckDBFileSourceAttribute(string function, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("File source path cannot be null or whitespace.", nameof(path));
        }

        Function = function;
        Path = path;
    }

    /// <summary>
    ///     The DuckDB table function used to read the file (e.g. <c>read_parquet</c>, <c>read_csv</c>,
    ///     <c>read_json</c>).
    /// </summary>
    public virtual string Function { get; }

    /// <summary>
    ///     The file path or glob pattern.
    /// </summary>
    public virtual string Path { get; }
}
