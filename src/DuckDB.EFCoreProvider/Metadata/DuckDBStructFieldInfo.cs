using System.Collections.Immutable;

namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     Describes one mapped leaf in a DuckDB <c>STRUCT</c> column.
/// </summary>
/// <remarks>
///     The physical path and the EF relational column identity are deliberately separate.
///     EF uses the latter while planning a command; DuckDB uses the former when SQL is rendered.
/// </remarks>
public sealed record DuckDBStructFieldInfo
{
    private readonly ImmutableArray<string> _nestedFieldNames;
    private string _structColumnName = null!;
    private string? _leafFieldName;

    /// <summary>
    ///     Creates a STRUCT field descriptor.
    /// </summary>
    public DuckDBStructFieldInfo(
        string structColumnName,
        string[] nestedFieldNames,
        string? leafFieldName = null)
    {
        StructColumnName = structColumnName;
        NestedFieldNames = nestedFieldNames ?? throw new ArgumentNullException(nameof(nestedFieldNames));
        LeafFieldName = leafFieldName;
    }

    internal DuckDBStructFieldInfo(
        string structColumnName,
        IEnumerable<string> nestedFieldNames,
        string? leafFieldName,
        string? efColumnName,
        string? storeType,
        bool? isNullable)
        : this(
            structColumnName,
            nestedFieldNames.ToArray(),
            leafFieldName)
    {
        if (efColumnName is not null)
        {
            ValidateFieldName(efColumnName);
        }

        EfColumnName = efColumnName;
        StoreType = storeType;
        IsNullable = isNullable;
    }

    /// <summary>The physical DuckDB STRUCT column name.</summary>
    public string StructColumnName
    {
        get => _structColumnName;
        init => _structColumnName = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A STRUCT column name is required.", nameof(value))
            : value;
    }

    /// <summary>The immutable intermediate physical field path.</summary>
    public IReadOnlyList<string> NestedFieldNames
    {
        get => _nestedFieldNames;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _nestedFieldNames = value.Select(ValidateFieldName).ToImmutableArray();
        }
    }

    /// <summary>The physical leaf field name, or <see langword="null" /> for legacy descriptors.</summary>
    public string? LeafFieldName
    {
        get => _leafFieldName;
        init => _leafFieldName = value is null ? null : ValidateFieldName(value);
    }

    /// <summary>The synthetic EF relational column identity used for this leaf.</summary>
    public string? EfColumnName { get; private init; }

    /// <summary>The leaf store type captured during model finalization.</summary>
    public string? StoreType { get; private init; }

    /// <summary>The leaf nullability captured during model finalization.</summary>
    public bool? IsNullable { get; private init; }

    /// <summary>The complete immutable physical field path, including the leaf.</summary>
    public IReadOnlyList<string> FieldPath
        => LeafFieldName is null
            ? _nestedFieldNames
            : _nestedFieldNames.Add(LeafFieldName);

    private static string ValidateFieldName(string name)
        => string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("STRUCT field names must be non-empty.", nameof(name))
            : name;
}
