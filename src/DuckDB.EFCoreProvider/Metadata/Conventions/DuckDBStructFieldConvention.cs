using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DuckDB.EFCoreProvider.Metadata.Conventions;

/// <summary>
///     Derives one immutable STRUCT mapping tree for every opted-in complex-property usage.
/// </summary>
public sealed class DuckDBStructFieldConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var roots = new List<DuckDBStructMapping>();
            var fields = new List<DuckDBStructFieldInfo>();

            foreach (var complexProperty in entityType.GetComplexProperties())
            {
                if (!IsStructMappingEnabled(complexProperty))
                {
                    continue;
                }

                var physicalRootName =
                    complexProperty.FindAnnotation(DuckDBAnnotationNames.StructColumnName)?.Value as string
                    ?? complexProperty.Name;
                var rootFields = new List<DuckDBStructFieldInfo>();
                var mapping = BuildMapping(
                    complexProperty,
                    physicalRootName,
                    complexProperty.Name,
                    [],
                    rootFields);

                complexProperty.SetStructMapping(mapping, fromDataAnnotation: false);
                roots.Add(mapping);
                fields.AddRange(rootFields);
            }

            if (roots.Count > 0)
            {
                var metadata = new DuckDBStructEntityMetadata(
                    roots,
                    fields
                        .OrderBy(field => field.EfColumnName, StringComparer.Ordinal)
                        .Select(field => new KeyValuePair<string, DuckDBStructFieldInfo>(
                            field.EfColumnName
                                ?? throw new InvalidOperationException(
                                    "A STRUCT field must have an EF relational column identity."),
                            field)));
                entityType.SetStructMetadata(metadata);
            }
        }
    }

    private static DuckDBStructMapping BuildMapping(
        IConventionComplexProperty complexProperty,
        string structColumnName,
        string rootPropertyName,
        IReadOnlyList<string> nestedPath,
        List<DuckDBStructFieldInfo> rootFields)
    {
        var children = new Dictionary<string, DuckDBStructChildMapping>(StringComparer.Ordinal);
        var complexType = complexProperty.ComplexType;

        foreach (var property in complexType.GetProperties().OfType<IConventionProperty>())
        {
            var field = BuildFieldInfo(
                property,
                structColumnName,
                rootPropertyName,
                nestedPath);
            rootFields.Add(field);
            children[property.Name] = new DuckDBStructChildMapping(field.LeafFieldName!);
        }

        foreach (var nestedComplexProperty in complexType.GetComplexProperties())
        {
            var nestedFieldName =
                nestedComplexProperty.FindAnnotation(DuckDBAnnotationNames.StructFieldName)?.Value as string
                ?? ToCamelCase(nestedComplexProperty.Name);
            var extendedPath = nestedPath.Append(nestedFieldName).ToArray();
            var nestedFields = new List<DuckDBStructFieldInfo>();
            var nestedMapping = BuildMapping(
                nestedComplexProperty,
                structColumnName,
                rootPropertyName,
                extendedPath,
                nestedFields);
            nestedComplexProperty.SetStructMapping(nestedMapping, fromDataAnnotation: false);
            rootFields.AddRange(nestedFields);
            children[nestedComplexProperty.Name] = new DuckDBStructChildMapping(nestedFieldName, nestedMapping);
        }

        return new DuckDBStructMapping(structColumnName, nestedPath.LastOrDefault(), children, rootFields);
    }

    private static DuckDBStructFieldInfo BuildFieldInfo(
        IConventionProperty property,
        string structColumnName,
        string rootPropertyName,
        IReadOnlyList<string> nestedPath)
    {
        var configuredInfo = property.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
            as DuckDBStructFieldInfo;
        var explicitColumnName = property.GetColumnNameConfigurationSource() is not null
            ? property.GetColumnName()
            : null;
        var inferredLeafName = ToCamelCase(property.Name);
        var leafFieldName =
            configuredInfo?.LeafFieldName
            ?? property.FindAnnotation(DuckDBAnnotationNames.StructFieldName)?.Value as string
            ?? explicitColumnName
            ?? inferredLeafName;
        var effectiveRootName = configuredInfo?.StructColumnName ?? structColumnName;
        var effectivePath = configuredInfo?.NestedFieldNames ?? nestedPath;
        var efColumnName = explicitColumnName
            ?? FormatUniqueColumnName(rootPropertyName, nestedPath, inferredLeafName);

        if (explicitColumnName is null)
        {
            property.SetColumnName(efColumnName, fromDataAnnotation: false);
        }

        var field = new DuckDBStructFieldInfo(
            effectiveRootName,
            effectivePath,
            leafFieldName,
            efColumnName,
            storeType: null,
            property.IsNullable);

        property.SetOrRemoveAnnotation(
            DuckDBAnnotationNames.StructField,
            field,
            fromDataAnnotation: false);
        return field;
    }

    private static bool IsStructMappingEnabled(IConventionComplexProperty complexProperty)
        => complexProperty.FindAnnotation(DuckDBAnnotationNames.UseStructMapping)?.Value is true
            || complexProperty.PropertyInfo?.IsDefined(typeof(UseStructMappingAttribute), inherit: true) == true;

    private static string FormatUniqueColumnName(
        string rootPropertyName,
        IReadOnlyList<string> nestedPath,
        string leafFieldName)
    {
        var parts = new List<string>(nestedPath.Count + 2)
        {
            ToCamelCase(rootPropertyName)
        };
        parts.AddRange(nestedPath);
        parts.Add(leafFieldName);
        return string.Join("_", parts);
    }

    private static string ToCamelCase(string name)
        => string.IsNullOrEmpty(name) || char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name[1..];
}
