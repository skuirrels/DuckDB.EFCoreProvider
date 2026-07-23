namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     Describes a property's location inside a DuckDB <c>STRUCT</c> column so that the
///     EF Core SQL generator can emit DuckDB struct field access syntax
///     (<c>t."StructColumn".field.nestedField</c>) instead of a plain column reference.
/// </summary>
/// <remarks>
///     <para>
///         This type is immutable once constructed. The <c>nestedFieldNames</c> array
///         passed to the constructor is defensively copied; callers cannot mutate
///         cached metadata after model finalization.</para>
///     <para>
///         Typically set automatically by the <c>DuckDBStructFieldConvention</c> at model
///         finalization time — no manual configuration is needed. The convention infers the
///         struct column name from the complex property name and the field names from the
///         camelCase property names:</para>
/// <code>
/// // Convention infers HasStructField("Location") on City/Country/Lat:
/// modelBuilder.Entity&lt;Customer&gt;().ComplexProperty(c =&gt; c.Location);
/// // Convention infers HasStructField("Shipping", "address") on Street/City/Zip:
/// modelBuilder.Entity&lt;Order&gt;().ComplexProperty(o =&gt; o.Shipping);
///     </code>
///     <para>
///         For manual override (e.g. when the struct column name differs from the property
///         name), use the <c>HasStructField</c> fluent API:</para>
/// <code>
/// loc.Property(l =&gt; l.City).HasColumnName("city").HasStructField("Location");
///     </code>
/// </remarks>
public sealed record DuckDBStructFieldInfo
{
    /// <summary>
    ///     Creates struct field metadata.
    /// </summary>
    /// <param name="structColumnName">The physical DuckDB STRUCT column name (e.g. <c>"Location"</c>).</param>
    /// <param name="nestedFieldNames">
    ///     Zero or more intermediate struct field names between the struct column and the leaf field.
    ///     For a single-level struct this is empty. For <c>t."Shipping".address.street</c> it would be
    ///     <c>{ "address" }</c> — the leaf field <c>"street"</c> comes from <c>LeafFieldName</c>.
    ///     A defensive copy is made; the caller's array cannot be mutated through this instance.
    /// </param>
    /// <param name="leafFieldName">
    ///     The DuckDB leaf field name inside the struct (e.g. <c>"city"</c>). When <see langword="null" />,
    ///     the SQL generator uses the property's column name. The convention sets this to a
    ///     lowercased version of the CLR property name.
    /// </param>
    public DuckDBStructFieldInfo(string structColumnName, string[] nestedFieldNames, string? leafFieldName = null)
    {
        StructColumnName = structColumnName;
        // Defensive copy — prevents external mutation of cached model metadata (#4).
        NestedFieldNames = nestedFieldNames is null ? [] : [..nestedFieldNames];
        LeafFieldName = leafFieldName;
    }

    /// <summary>The physical DuckDB STRUCT column name.</summary>
    public string StructColumnName { get; init; }

    /// <summary>
    ///     Intermediate struct field names between the struct column and the leaf field (may be empty for
    ///     single-level structs). Immutable; constructed from a defensive copy of the input array.
    /// </summary>
    public IReadOnlyList<string> NestedFieldNames { get; init; }

    /// <summary>
    ///     The DuckDB struct leaf field name (e.g. <c>"city"</c>). When <see langword="null" />, the SQL
    ///     generator uses the property's column name instead.
    /// </summary>
    public string? LeafFieldName { get; init; }
}