using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>Configuration extensions for opt-in DuckDB STRUCT mappings.</summary>
public static class DuckDBStructPropertyBuilderExtensions
{
    public static ComplexPropertyBuilder UseStructMapping(this ComplexPropertyBuilder propertyBuilder)
        => UseStructMapping(propertyBuilder, null);

    public static ComplexPropertyBuilder UseStructMapping(
        this ComplexPropertyBuilder propertyBuilder,
        string? structColumnName)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.Metadata.SetAnnotation(DuckDBAnnotationNames.UseStructMapping, true);
        if (structColumnName is not null)
        {
            propertyBuilder.Metadata.SetAnnotation(
                DuckDBAnnotationNames.StructColumnName,
                ValidateName(structColumnName, nameof(structColumnName)));
        }

        return propertyBuilder;
    }

    public static ComplexPropertyBuilder<TComplex> UseStructMapping<TComplex>(
        this ComplexPropertyBuilder<TComplex> propertyBuilder)
        where TComplex : class
        => UseStructMapping(propertyBuilder, null);

    public static ComplexPropertyBuilder<TComplex> UseStructMapping<TComplex>(
        this ComplexPropertyBuilder<TComplex> propertyBuilder,
        string? structColumnName)
        where TComplex : class
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.Metadata.SetAnnotation(DuckDBAnnotationNames.UseStructMapping, true);
        if (structColumnName is not null)
        {
            propertyBuilder.Metadata.SetAnnotation(
                DuckDBAnnotationNames.StructColumnName,
                ValidateName(structColumnName, nameof(structColumnName)));
        }

        return propertyBuilder;
    }

    public static ComplexPropertyBuilder<TComplex> HasStructFieldName<TComplex>(
        this ComplexPropertyBuilder<TComplex> propertyBuilder,
        string fieldName)
        where TComplex : class
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.Metadata.SetAnnotation(
            DuckDBAnnotationNames.StructFieldName,
            ValidateName(fieldName, nameof(fieldName)));
        return propertyBuilder;
    }

    public static ComplexTypePropertyBuilder<TProperty> HasStructFieldName<TProperty>(
        this ComplexTypePropertyBuilder<TProperty> propertyBuilder,
        string fieldName)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.HasAnnotation(
            DuckDBAnnotationNames.StructFieldName,
            ValidateName(fieldName, nameof(fieldName)));
        return propertyBuilder;
    }

    public static PropertyBuilder<TProperty> HasStructFieldName<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        string fieldName)
    {
        ArgumentNullException.ThrowIfNull(propertyBuilder);
        propertyBuilder.HasAnnotation(
            DuckDBAnnotationNames.StructFieldName,
            ValidateName(fieldName, nameof(fieldName)));
        return propertyBuilder;
    }

    private static string ValidateName(string name, string parameterName)
        => string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("STRUCT names must be non-empty.", parameterName)
            : name;
}
