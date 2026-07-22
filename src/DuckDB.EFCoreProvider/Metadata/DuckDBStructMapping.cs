using System.Collections.Generic;
using System.Collections.Immutable;

namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     Describes the DuckDB <c>STRUCT</c> mapping for a specific usage of a complex property.
/// </summary>
/// <remarks>
///     <para>
///         Stored as the <c>DuckDB:StructMapping</c> annotation on a complex property. Because
///         the annotation lives on the complex <em>property</em> (not the shared complex type's
///         scalar properties), the same CLR complex type can be used for multiple struct columns
///         (e.g. <c>Billing</c> and <c>Shipping</c>) with distinct field paths.</para>
///     <para>
///         The mapping is immutable once constructed so cached model metadata cannot be mutated
///         after finalization.</para>
/// </remarks>
public sealed class DuckDBStructMapping
{
    /// <summary>
    ///     Creates a struct mapping descriptor.
    /// </summary>
    /// <param name="structColumnName">The physical DuckDB STRUCT column name (e.g. <c>"Location"</c>).</param>
    /// <param name="fieldName">
    ///     The field name of this complex property inside its parent struct. <see langword="null" />
    ///     for the root complex property whose name is the struct column name.
    /// </param>
    /// <param name="children">Child property mappings keyed by EF property name.</param>
    public DuckDBStructMapping(
        string structColumnName,
        string? fieldName,
        IReadOnlyDictionary<string, DuckDBStructChildMapping> children)
    {
        StructColumnName = structColumnName;
        FieldName = fieldName;
        Children = children.ToImmutableDictionary();
    }

    /// <summary>The physical DuckDB STRUCT column name.</summary>
    public string StructColumnName { get; }

    /// <summary>
    ///     The DuckDB field name for this complex property inside its parent struct, or
    ///     <see langword="null" /> for the root property.
    /// </summary>
    public string? FieldName { get; }

    /// <summary>
    ///     Child mappings keyed by EF property name. A scalar child maps directly to a leaf
    ///     field name; a complex child carries a nested <see cref="DuckDBStructMapping" />.
    /// </summary>
    public IReadOnlyDictionary<string, DuckDBStructChildMapping> Children { get; }
}

/// <summary>
///     Describes how a single child property maps inside a DuckDB <c>STRUCT</c> column.
/// </summary>
public sealed class DuckDBStructChildMapping
{
    /// <summary>
    ///     Creates a scalar child mapping.
    /// </summary>
    /// <param name="fieldName">The physical DuckDB field name for this child.</param>
    public DuckDBStructChildMapping(string fieldName)
    {
        FieldName = fieldName;
        Nested = null;
    }

    /// <summary>
    ///     Creates a nested complex child mapping.
    /// </summary>
    /// <param name="fieldName">The physical DuckDB field name for this child (intermediate node).</param>
    /// <param name="nested">The nested struct mapping for this child complex property.</param>
    public DuckDBStructChildMapping(string fieldName, DuckDBStructMapping nested)
    {
        FieldName = fieldName;
        Nested = nested;
    }

    /// <summary>The physical DuckDB field name for this child.</summary>
    public string FieldName { get; }

    /// <summary>
    ///     The nested struct mapping when the child is a complex property, or <see langword="null" />
    ///     for scalar leaf properties.
    /// </summary>
    public DuckDBStructMapping? Nested { get; }

    /// <summary>
    ///     <see langword="true" /> when this child is a complex property with a nested mapping.
    /// </summary>
    public bool IsComplex => Nested is not null;
}
