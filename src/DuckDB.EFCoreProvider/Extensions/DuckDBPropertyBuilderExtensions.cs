using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuckDB.EFCoreProvider.Extensions;

public static class DuckDBPropertyBuilderExtensions
{
    public static PropertyBuilder UseAutoIncrement(this PropertyBuilder propertyBuilder)
    {
        propertyBuilder.ValueGeneratedOnAdd();
        propertyBuilder.Metadata.SetValueGenerationStrategy(DuckDBValueGenerationStrategy.AutoIncrement);

        return propertyBuilder;
    }

    public static PropertyBuilder<TProperty> UseAutoIncrement<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder)
        => (PropertyBuilder<TProperty>)UseAutoIncrement((PropertyBuilder)propertyBuilder);

    public static ColumnBuilder UseAutoIncrement(
        this ColumnBuilder columnBuilder)
    {
        columnBuilder.Overrides.SetValueGenerationStrategy(DuckDBValueGenerationStrategy.AutoIncrement);

        return columnBuilder;
    }

    /// <summary>
    ///     Marks a complex property as backed by a DuckDB <c>STRUCT</c> column so the
    ///     <c>DuckDBStructFieldConvention</c> infers <c>HasColumnName</c> and <c>HasStructField</c>
    ///     metadata from the complex property hierarchy at model finalization time.
    /// </summary>
    /// <param name="propertyBuilder">The complex property builder.</param>
    /// <returns>The same builder instance so multiple calls can be chained.</returns>
    public static ComplexPropertyBuilder UseStructMapping(
        this ComplexPropertyBuilder propertyBuilder)
    {
        propertyBuilder.Metadata.SetAnnotation(DuckDBAnnotationNames.UseStructMapping, true);
        return propertyBuilder;
    }

    /// <summary>
    ///     Marks a complex property as backed by a DuckDB <c>STRUCT</c> column so the
    ///     <c>DuckDBStructFieldConvention</c> infers <c>HasColumnName</c> and <c>HasStructField</c>
    ///     metadata from the complex property hierarchy at model finalization time.
    /// </summary>
    /// <typeparam name="TComplex">The complex type.</typeparam>
    /// <param name="propertyBuilder">The complex property builder.</param>
    /// <returns>The same builder instance so multiple calls can be chained.</returns>
    public static ComplexPropertyBuilder<TComplex> UseStructMapping<TComplex>(
        this ComplexPropertyBuilder<TComplex> propertyBuilder)
        where TComplex : class
    {
        propertyBuilder.Metadata.SetAnnotation(DuckDBAnnotationNames.UseStructMapping, true);
        return propertyBuilder;
    }

    /// <summary>
    ///     Maps a complex-property sub-property to a field inside a DuckDB <c>STRUCT</c> column.
    ///     The provider emits DuckDB struct field access syntax (<c>t."StructColumn".field</c>)
    ///     instead of a plain column reference, enabling SQL-level projection of individual struct
    ///     sub-fields.
    /// </summary>
    /// <remarks>
    ///     In most cases, <c>HasStructField</c> is not needed — the <c>DuckDBStructFieldConvention</c>
    ///     automatically infers struct field metadata from complex property names at model finalization
    ///     time. Use this method only when you need to override the convention's inference (e.g. when
    ///     the struct column name differs from the complex property name).
    /// </remarks>
    /// <param name="propertyBuilder">The property builder for the struct sub-field.</param>
    /// <param name="structColumnName">The physical DuckDB STRUCT column name (e.g. <c>"Location"</c>).</param>
    /// <param name="nestedFieldNames">
    ///     Intermediate struct field names between the struct column and the leaf property.
    ///     Empty for single-level structs. For <c>t."Shipping".address.street</c>, pass <c>"address"</c>.
    ///     The leaf field name comes from <c>HasColumnName</c>.
    /// </param>
    /// <returns>The same builder instance so multiple calls can be chained.</returns>
    public static PropertyBuilder<TProperty> HasStructField<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        string structColumnName,
        params string[] nestedFieldNames)
    {
        propertyBuilder.HasAnnotation(
            DuckDBAnnotationNames.StructField,
            new DuckDBStructFieldInfo(structColumnName, nestedFieldNames));
        return propertyBuilder;
    }

    /// <summary>
    ///     Maps a complex-property sub-property to a field inside a DuckDB <c>STRUCT</c> column.
    ///     Same as the <see cref="HasStructField{TProperty}(PropertyBuilder{TProperty}, string, string[])"/>
    ///     overload, but for properties configured via <see cref="ComplexTypePropertyBuilder{TProperty}"/>
    ///     (returned by <see cref="ComplexPropertyBuilder{TComplex}"/> property accessors).
    /// </summary>
    /// <remarks>
    ///     In most cases, <c>HasStructField</c> is not needed — the <c>DuckDBStructFieldConvention</c>
    ///     automatically infers struct field metadata from complex property names at model finalization
    ///     time. Use this method only when you need to override the convention's inference.
    /// </remarks>
    /// <param name="propertyBuilder">The complex-type property builder for the struct sub-field.</param>
    /// <param name="structColumnName">The physical DuckDB STRUCT column name (e.g. <c>"Location"</c>).</param>
    /// <param name="nestedFieldNames">
    ///     Intermediate struct field names between the struct column and the leaf property.
    ///     Empty for single-level structs. For <c>t."Shipping".address.street</c>, pass <c>"address"</c>.
    /// </param>
    /// <returns>The same builder instance so multiple calls can be chained.</returns>
    public static ComplexTypePropertyBuilder<TProperty> HasStructField<TProperty>(
        this ComplexTypePropertyBuilder<TProperty> propertyBuilder,
        string structColumnName,
        params string[] nestedFieldNames)
    {
        propertyBuilder.HasAnnotation(
            DuckDBAnnotationNames.StructField,
            new DuckDBStructFieldInfo(structColumnName, nestedFieldNames));
        return propertyBuilder;
    }
    }
