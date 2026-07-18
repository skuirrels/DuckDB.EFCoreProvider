namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     Describes a property's location inside a DuckDB <c>STRUCT</c> column so that the
///     EF Core SQL generator can emit DuckDB struct field access syntax
///     (<c>t."StructColumn".field.nestedField</c>) instead of a plain column reference.
/// </summary>
/// <remarks>
///     <para>
///         Typically set automatically by the <c>DuckDBStructFieldConvention</c> at model
///         finalization time — no manual configuration is needed. The convention infers the
///         struct column name from the complex property name and the field names from the
///         camelCase property names:</para>
///     <code>
/// // Convention infers HasStructField("Location") on City/Country/Lat:
/// modelBuilder.Entity&lt;Customer&gt;().ComplexProperty(c =&gt; c.Location);
/// // Convention infers HasStructField("Shipping", "address") on Street/City/Zip:
/// modelBuilder.Entity&lt;Order&gt;().ComplexProperty(o =&gt; o.Shipping);
///     </code>
///     <para>
///         For manual override (e.g. when the struct column name differs from the property
///         name), use the <c>HasStructField</c> fluent API:</para>
///     <code>
/// loc.Property(l =&gt; l.City).HasColumnName("city").HasStructField("Location");
///     </code>
/// </remarks>
public sealed class DuckDBStructFieldInfo
{
    /// <summary>
    ///     Creates struct field metadata.
    /// </summary>
    /// <param name="structColumnName">The physical DuckDB STRUCT column name (e.g. <c>"Location"</c>).</param>
    /// <param name="nestedFieldNames">
    ///     Zero or more intermediate struct field names between the struct column and the leaf field.
    ///     For a single-level struct this is empty. For <c>t."Shipping".address.street</c> it would be
    ///     <c>{ "address" }</c> — the leaf field <c>"street"</c> comes from <c>LeafFieldName</c>.
    /// </param>
    /// <param name="leafFieldName">
    ///     The DuckDB leaf field name inside the struct (e.g. <c>"city"</c>). When <see langword="null" />,
    ///     the SQL generator falls back to the property's column name. The convention sets this to a
    ///     lowercased version of the CLR property name.
    /// </param>
    public DuckDBStructFieldInfo(string structColumnName, string[] nestedFieldNames, string? leafFieldName = null)
    {
        StructColumnName = structColumnName;
        NestedFieldNames = nestedFieldNames;
        LeafFieldName = leafFieldName;
    }

    /// <summary>The physical DuckDB STRUCT column name.</summary>
    public string StructColumnName { get; }

    /// <summary>
    ///     Intermediate struct field names between the struct column and the leaf field (may be empty for
    ///     single-level structs).
    /// </summary>
    public IReadOnlyList<string> NestedFieldNames { get; }

    /// <summary>
    ///     The DuckDB struct leaf field name (e.g. <c>"city"</c>). When <see langword="null" />, the SQL
    ///     generator uses the property's column name instead.
    /// </summary>
    public string? LeafFieldName { get; }

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is DuckDBStructFieldInfo other
           && StructColumnName == other.StructColumnName
           && NestedFieldNames.SequenceEqual(other.NestedFieldNames)
           && LeafFieldName == other.LeafFieldName;

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StructColumnName);
        hash.Add(LeafFieldName);
        foreach (var field in NestedFieldNames)
        {
            hash.Add(field);
        }
        return hash.ToHashCode();
    }
}