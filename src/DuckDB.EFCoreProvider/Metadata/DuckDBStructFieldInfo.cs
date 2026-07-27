using System.Collections.Immutable;

namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     Describes one mapped leaf in a DuckDB <c>STRUCT</c> column.
/// </summary>
/// <remarks>
///     The physical path and the EF relational column identity are deliberately separate.
///     EF uses the latter while planning a command; DuckDB uses the former when SQL is rendered.
/// </remarks>
public sealed class DuckDBStructFieldInfo : IEquatable<DuckDBStructFieldInfo>
{
    private readonly ImmutableArray<string> _nestedFieldNames;

    /// <summary>
    ///     Creates a STRUCT field descriptor.
    /// </summary>
    public DuckDBStructFieldInfo(
        string structColumnName,
        string[] nestedFieldNames,
        string? leafFieldName = null)
        : this(
            structColumnName,
            (nestedFieldNames ?? throw new ArgumentNullException(nameof(nestedFieldNames))).AsReadOnly(),
            leafFieldName,
            null,
            null,
            null)
    {
    }

    internal DuckDBStructFieldInfo(
        string structColumnName,
        IEnumerable<string> nestedFieldNames,
        string? leafFieldName,
        string? efColumnName,
        string? storeType,
        bool? isNullable)
    {
        if (string.IsNullOrWhiteSpace(structColumnName))
        {
            throw new ArgumentException("A STRUCT column name is required.", nameof(structColumnName));
        }

        ArgumentNullException.ThrowIfNull(nestedFieldNames);
        _nestedFieldNames = nestedFieldNames
            .Select(ValidateFieldName)
            .ToImmutableArray();

        if (leafFieldName is not null)
        {
            ValidateFieldName(leafFieldName);
        }

        if (efColumnName is not null)
        {
            ValidateFieldName(efColumnName);
        }

        StructColumnName = structColumnName;
        LeafFieldName = leafFieldName;
        EfColumnName = efColumnName;
        StoreType = storeType;
        IsNullable = isNullable;
    }

    /// <summary>The physical DuckDB STRUCT column name.</summary>
    public string StructColumnName { get; }

    /// <summary>The immutable intermediate physical field path.</summary>
    public IReadOnlyList<string> NestedFieldNames => _nestedFieldNames;

    /// <summary>The physical leaf field name, or <see langword="null" /> for legacy descriptors.</summary>
    public string? LeafFieldName { get; }

    /// <summary>The synthetic EF relational column identity used for this leaf.</summary>
    public string? EfColumnName { get; }

    /// <summary>The leaf store type captured during model finalization.</summary>
    public string? StoreType { get; }

    /// <summary>The leaf nullability captured during model finalization.</summary>
    public bool? IsNullable { get; }

    /// <summary>The complete immutable physical field path, including the leaf.</summary>
    public IReadOnlyList<string> FieldPath
        => LeafFieldName is null
            ? _nestedFieldNames
            : _nestedFieldNames.Add(LeafFieldName);

    public bool Equals(DuckDBStructFieldInfo? other)
        => other is not null
            && string.Equals(StructColumnName, other.StructColumnName, StringComparison.Ordinal)
            && string.Equals(LeafFieldName, other.LeafFieldName, StringComparison.Ordinal)
            && string.Equals(EfColumnName, other.EfColumnName, StringComparison.Ordinal)
            && string.Equals(StoreType, other.StoreType, StringComparison.Ordinal)
            && IsNullable == other.IsNullable
            && _nestedFieldNames.SequenceEqual(other._nestedFieldNames, StringComparer.Ordinal);

    public override bool Equals(object? obj)
        => Equals(obj as DuckDBStructFieldInfo);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StructColumnName, StringComparer.Ordinal);
        hash.Add(LeafFieldName, StringComparer.Ordinal);
        hash.Add(EfColumnName, StringComparer.Ordinal);
        hash.Add(StoreType, StringComparer.Ordinal);
        hash.Add(IsNullable);
        foreach (var field in _nestedFieldNames)
        {
            hash.Add(field, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static string ValidateFieldName(string name)
        => string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("STRUCT field names must be non-empty.", nameof(name))
            : name;
}
