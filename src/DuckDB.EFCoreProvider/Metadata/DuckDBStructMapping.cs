using System.Collections.Immutable;

namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     Immutable mapping tree for one opted-in complex-property usage.
/// </summary>
public sealed class DuckDBStructMapping
{
    public DuckDBStructMapping(
        string structColumnName,
        string? fieldName,
        IReadOnlyDictionary<string, DuckDBStructChildMapping> children)
        : this(structColumnName, fieldName, children, [])
    {
    }

    internal DuckDBStructMapping(
        string structColumnName,
        string? fieldName,
        IReadOnlyDictionary<string, DuckDBStructChildMapping> children,
        IEnumerable<DuckDBStructFieldInfo> fields)
    {
        if (string.IsNullOrWhiteSpace(structColumnName))
        {
            throw new ArgumentException("A STRUCT column name is required.", nameof(structColumnName));
        }

        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(fields);

        StructColumnName = structColumnName;
        FieldName = fieldName;
        Children = children
            .Select(pair => new KeyValuePair<string, DuckDBStructChildMapping>(
                ValidateName(pair.Key, nameof(children)),
                pair.Value ?? throw new ArgumentException("STRUCT child mappings cannot be null.", nameof(children))))
            .ToImmutableDictionary(StringComparer.Ordinal);
        Fields = fields.ToImmutableArray();
    }

    public string StructColumnName { get; }

    public string? FieldName { get; }

    public IReadOnlyDictionary<string, DuckDBStructChildMapping> Children { get; }

    /// <summary>
    ///     The leaf descriptors derived from this mapping tree. This is the canonical source
    ///     used to derive relational lookup indexes.
    /// </summary>
    public IReadOnlyList<DuckDBStructFieldInfo> Fields { get; }

    private static string ValidateName(string name, string parameterName)
        => string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("STRUCT mapping names must be non-empty.", parameterName)
            : name;
}

/// <summary>
///     Immutable description of one complex child inside a STRUCT mapping.
/// </summary>
public sealed class DuckDBStructChildMapping
{
    public DuckDBStructChildMapping(string fieldName)
        : this(fieldName, null)
    {
    }

    public DuckDBStructChildMapping(string fieldName, DuckDBStructMapping? nested)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("STRUCT field names must be non-empty.", nameof(fieldName));
        }

        FieldName = fieldName;
        Nested = nested;
    }

    public string FieldName { get; }

    public DuckDBStructMapping? Nested { get; }

    public bool IsComplex => Nested is not null;
}

/// <summary>
///     Immutable STRUCT metadata for one entity type. The column index is derived from the
///     canonical root mapping trees during model finalization.
/// </summary>
internal sealed class DuckDBStructEntityMetadata
{
    public DuckDBStructEntityMetadata(
        IEnumerable<DuckDBStructMapping> roots,
        IEnumerable<KeyValuePair<string, DuckDBStructFieldInfo>> columns)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(columns);

        Roots = roots.ToImmutableArray();
        Columns = columns.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
    }

    public IReadOnlyList<DuckDBStructMapping> Roots { get; }

    public IReadOnlyDictionary<string, DuckDBStructFieldInfo> Columns { get; }

    public bool TryGetField(string efColumnName, out DuckDBStructFieldInfo field)
        => Columns.TryGetValue(efColumnName, out field!);
}
