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

    public static ColumnBuilder UseAutoIncrement(this ColumnBuilder columnBuilder)
    {
        columnBuilder.Overrides.SetValueGenerationStrategy(DuckDBValueGenerationStrategy.AutoIncrement);
        return columnBuilder;
    }

    // Keep the original declaring type available for callers that use the provider
    // extension methods through a static class-qualified call.
    public static ComplexPropertyBuilder UseStructMapping(ComplexPropertyBuilder propertyBuilder)
        => DuckDBStructPropertyBuilderExtensions.UseStructMapping(propertyBuilder);

    public static ComplexPropertyBuilder<TComplex> UseStructMapping<TComplex>(
        ComplexPropertyBuilder<TComplex> propertyBuilder)
        where TComplex : class
        => DuckDBStructPropertyBuilderExtensions.UseStructMapping(propertyBuilder);

    public static PropertyBuilder<TProperty> HasStructField<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        string structColumnName,
        params string[] nestedFieldNames)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.HasAnnotation(
            DuckDBAnnotationNames.StructField,
            new DuckDBStructFieldInfo(structColumnName, nestedFieldNames));
        return propertyBuilder;
    }

    public static ComplexTypePropertyBuilder<TProperty> HasStructField<TProperty>(
        this ComplexTypePropertyBuilder<TProperty> propertyBuilder,
        string structColumnName,
        params string[] nestedFieldNames)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.HasAnnotation(
            DuckDBAnnotationNames.StructField,
            new DuckDBStructFieldInfo(structColumnName, nestedFieldNames));
        return propertyBuilder;
    }
}
