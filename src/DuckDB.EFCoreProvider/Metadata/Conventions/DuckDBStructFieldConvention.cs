using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

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

            var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);
            ApplyStructForeignKeyBindings(entityType);
            foreach (var property in GetProperties(entityType))
            {
                if (property.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
                    is not DuckDBStructFieldInfo configuredInfo)
                {
                    continue;
                }

                var efColumnName = configuredInfo.EfColumnName
                    ?? (table is { } storeObject ? property.GetColumnName(storeObject) : property.GetColumnName())
                    ?? property.Name;
                var normalizedInfo = configuredInfo.EfColumnName is not null
                    && configuredInfo.LeafFieldName is not null
                        ? configuredInfo
                        : new DuckDBStructFieldInfo(
                            configuredInfo.StructColumnName,
                            configuredInfo.NestedFieldNames,
                            configuredInfo.LeafFieldName
                                ?? property.FindAnnotation(DuckDBAnnotationNames.StructFieldName)?.Value as string
                                ?? ToCamelCase(property.Name),
                            efColumnName,
                            property.GetColumnType(),
                            property.IsNullable);

                property.SetOrRemoveAnnotation(
                    DuckDBAnnotationNames.StructField,
                    normalizedInfo,
                    fromDataAnnotation: false);

                var existingIndex = fields.FindIndex(field =>
                    string.Equals(field.EfColumnName, efColumnName, StringComparison.Ordinal));
                if (existingIndex >= 0)
                {
                    fields[existingIndex] = normalizedInfo;
                }
                else
                {
                    fields.Add(normalizedInfo);
                }
            }

            foreach (var group in fields.GroupBy(
                         field => field.StructColumnName,
                         StringComparer.OrdinalIgnoreCase))
            {
                if (roots.All(root => !string.Equals(
                        root.StructColumnName,
                        group.Key,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    roots.Add(new DuckDBStructMapping(
                        group.Key,
                        fieldName: null,
                        new Dictionary<string, DuckDBStructChildMapping>(StringComparer.Ordinal),
                        group));
                }
            }

            if (fields.Count > 0)
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
                entityType.SetOrRemoveAnnotation(
                    DuckDBAnnotationNames.StructColumnMap,
                    null,
                    fromDataAnnotation: false);
            }
        }

    }

    private static void ApplyStructForeignKeyBindings(IConventionEntityType entityType)
    {
        var bindings = entityType.GetForeignKeys()
            .OfType<IConventionForeignKey>()
            .Select(foreignKey => (
                ForeignKey: foreignKey,
                Binding: foreignKey.FindAnnotation(DuckDBAnnotationNames.StructForeignKeyPath)?.Value
                    as DuckDBStructForeignKeyPath))
            .Where(entry => entry.Binding is not null)
            .ToArray();

        // Distinct STRUCT paths must never collapse onto the same shadow foreign-key property: the binding is
        // consumed below, so the columns would otherwise silently overwrite one another during finalization.
        foreach (var group in bindings.GroupBy(
                     entry => entry.Binding!.ShadowPropertyName,
                     StringComparer.Ordinal))
        {
            if (group.Select(entry => entry.Binding!).Distinct().Skip(1).Any())
            {
                throw new InvalidOperationException(
                    $"Multiple STRUCT foreign keys on '{entityType.DisplayName()}' resolve to the same shadow "
                    + $"property '{group.Key}'. Use distinct STRUCT paths for distinct relationships.");
            }
        }

        foreach (var (foreignKey, binding) in bindings)
        {
            if (binding is null)
            {
                continue;
            }

            if (foreignKey.Properties.Count != 1
                || !string.Equals(
                    foreignKey.Properties[0].Name,
                    binding.ShadowPropertyName,
                    StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"STRUCT foreign-key path '{FormatPath(binding)}' must be the only property in its foreign key. "
                    + "Composite STRUCT foreign keys are not supported.");
            }

            var leafProperty = FindStructLeaf(entityType, binding.MemberNames);
            var field = leafProperty?.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value
                as DuckDBStructFieldInfo;
            if (field?.EfColumnName is null)
            {
                throw new InvalidOperationException(
                    $"STRUCT foreign-key path '{FormatPath(binding)}' does not resolve to a mapped DuckDB STRUCT "
                    + "leaf. Configure the dependent complex property with UseStructMapping before calling "
                    + "HasStructForeignKey.");
            }

            var shadowProperty = entityType.FindProperty(binding.ShadowPropertyName)
                ?? throw new InvalidOperationException(
                    $"The internal STRUCT foreign-key property '{binding.ShadowPropertyName}' could not be created.");
            shadowProperty.SetColumnName(field.EfColumnName, fromDataAnnotation: false);

            // Infer the join shape from the mapped STRUCT leaf: a non-nullable leaf means the
            // foreign key is required (INNER JOIN); a nullable leaf means it is optional (LEFT JOIN).
            // An explicit IsRequired call (Explicit/DataAnnotation source) always wins over inference.
            var requiredConfigurationSource = foreignKey.GetIsRequiredConfigurationSource();
            if (requiredConfigurationSource is null or ConfigurationSource.Convention)
            {
                foreignKey.SetIsRequired(!leafProperty!.IsNullable, fromDataAnnotation: false);
            }

            foreignKey.SetOrRemoveAnnotation(
                DuckDBAnnotationNames.StructForeignKeyPath,
                null,
                fromDataAnnotation: false);
        }
    }

    private static IConventionProperty? FindStructLeaf(
        IConventionEntityType entityType,
        IReadOnlyList<string> memberNames)
    {
        if (memberNames.Count < 2
            || entityType.FindComplexProperty(memberNames[0]) is not { } root)
        {
            return null;
        }

        var complexType = root.ComplexType;
        for (var index = 1; index < memberNames.Count - 1; index++)
        {
            if (complexType.FindComplexProperty(memberNames[index]) is not { } nested)
            {
                return null;
            }

            complexType = nested.ComplexType;
        }

        return complexType.FindProperty(memberNames[^1]);
    }

    private static string FormatPath(DuckDBStructForeignKeyPath binding)
        => string.Join(".", binding.MemberNames);

    private static IEnumerable<IConventionProperty> GetProperties(IConventionTypeBase typeBase)
    {
        foreach (var property in typeBase.GetProperties().OfType<IConventionProperty>())
        {
            yield return property;
        }

        foreach (var complexProperty in typeBase.GetComplexProperties())
        {
            foreach (var property in GetProperties(complexProperty.ComplexType))
            {
                yield return property;
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