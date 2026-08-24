using System.Collections;
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
    private StructuralStringList _nestedFieldNames = null!;
    private string _structColumnName = null!;
    private string? _leafFieldName;
    private readonly RelationalMetadata _relationalMetadata;

    /// <summary>
    ///     Creates a STRUCT field descriptor.
    /// </summary>
    public DuckDBStructFieldInfo(
        string structColumnName,
        string[] nestedFieldNames,
        string? leafFieldName = null)
        : this(structColumnName, nestedFieldNames, leafFieldName, isRootNullable: null)
    {
    }

    /// <summary>
    ///     Creates a STRUCT field descriptor with the nullability of its owning complex-property root.
    /// </summary>
    public DuckDBStructFieldInfo(
        string structColumnName,
        string[] nestedFieldNames,
        string? leafFieldName,
        bool? isRootNullable)
    {
        StructColumnName = structColumnName;
        NestedFieldNames = nestedFieldNames ?? throw new ArgumentNullException(nameof(nestedFieldNames));
        LeafFieldName = leafFieldName;
        _relationalMetadata = new RelationalMetadata();
        _relationalMetadata.IsRootNullable = isRootNullable;
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
            nestedFieldNames,
            leafFieldName,
            efColumnName,
            storeType,
            isNullable,
            isRootNullable: null)
    {
    }

    internal DuckDBStructFieldInfo(
        string structColumnName,
        IEnumerable<string> nestedFieldNames,
        string? leafFieldName,
        string? efColumnName,
        string? storeType,
        bool? isNullable,
        bool? isRootNullable)
        : this(
            structColumnName,
            nestedFieldNames.ToArray(),
            leafFieldName,
            isRootNullable)
    {
        if (efColumnName is not null)
        {
            ValidateFieldName(efColumnName);
        }

        _relationalMetadata.Set(efColumnName, storeType, isNullable, isRootNullable);
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
            _nestedFieldNames = new StructuralStringList(value.Select(ValidateFieldName));
        }
    }

    /// <summary>The physical leaf field name, or <see langword="null" /> for legacy descriptors.</summary>
    public string? LeafFieldName
    {
        get => _leafFieldName;
        init => _leafFieldName = value is null ? null : ValidateFieldName(value);
    }

    /// <summary>The synthetic EF relational column identity used for this leaf.</summary>
    public string? EfColumnName
        => _relationalMetadata.EfColumnName;

    /// <summary>The leaf store type captured during model finalization.</summary>
    public string? StoreType
        => _relationalMetadata.StoreType;

    /// <summary>The leaf nullability captured during model finalization.</summary>
    public bool? IsNullable
        => _relationalMetadata.IsNullable;

    /// <summary>The owning complex-property root nullability captured during model finalization.</summary>
    public bool? IsRootNullable
        => _relationalMetadata.IsRootNullable;

    /// <summary>The complete immutable physical field path, including the leaf.</summary>
    public IReadOnlyList<string> FieldPath
        => LeafFieldName is null
            ? _nestedFieldNames
            : new StructuralStringList(_nestedFieldNames.Append(LeafFieldName));

    private static string ValidateFieldName(string name)
        => string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("STRUCT field names must be non-empty.", nameof(name))
            : name;

    private sealed class StructuralStringList(IEnumerable<string> values) : IReadOnlyList<string>
    {
        private readonly ImmutableArray<string> _values = values.ToImmutableArray();

        public int Count
            => _values.Length;

        public string this[int index]
            => _values[index];

        public IEnumerator<string> GetEnumerator()
            => ((IEnumerable<string>)_values).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public override bool Equals(object? obj)
            => obj is StructuralStringList other
                && _values.SequenceEqual(other._values, StringComparer.Ordinal);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var value in _values)
            {
                hash.Add(value, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }

        public IReadOnlyList<string> Append(string value)
            => _values.Add(value);
    }

    private sealed class RelationalMetadata
    {
        internal string? EfColumnName { get; set; }

        internal string? StoreType { get; set; }

        internal bool? IsNullable { get; set; }

        internal bool? IsRootNullable { get; set; }

        internal void Set(
            string? efColumnName,
            string? storeType,
            bool? nullable,
            bool? isRootNullable)
        {
            EfColumnName = efColumnName;
            StoreType = storeType;
            IsNullable = nullable;
            IsRootNullable = isRootNullable;
        }

        // These values are derived relational caches, not STRUCT field identity.
        public override bool Equals(object? obj)
            => obj is RelationalMetadata;

        public override int GetHashCode()
            => 0;
    }
}