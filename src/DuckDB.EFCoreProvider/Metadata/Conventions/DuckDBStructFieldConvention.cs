using System.Collections.Generic;
using System.Reflection;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DuckDB.EFCoreProvider.Metadata.Conventions;

/// <summary>
///     A convention that automatically infers DuckDB <c>STRUCT</c> column metadata from
///     EF Core complex properties, eliminating the need for manual
///     <c>HasColumnName</c>/<c>HasStructField</c> calls on each struct sub-field.
/// </summary>
/// <remarks>
///     <para>
///         Struct field inference is <strong>opt-in per complex property</strong> — it only
///         processes properties explicitly marked via one of:
///     </para>
///     <list type="bullet">
///         <item>The <c>[UseStructMapping]</c> attribute on the CLR property.</item>
///         <item>The fluent API: <c>ComplexProperty(c => c.Location).UseStructMapping()</c>.</item>
///     </list>
///     <para>
///         Explicit <c>HasColumnName</c> or <c>HasStructField</c> calls always take precedence.
///     </para>
///     <para>
///         Entities mapped to database views (via <c>ToView</c>) with struct-mapped complex
///         properties are unsupported in Phase 1: EF Core's query projection pipeline cannot
///         build a complex property shaper against a view source when the struct-column metadata
///         is applied. The convention throws during model finalization with a clear message.
///     </para>
/// </remarks>
public sealed class DuckDBStructFieldConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var columnMap = new Dictionary<string, DuckDBStructFieldInfo>();

            foreach (var complexProperty in entityType.GetComplexProperties())
            {
                if (!IsStructMappingEnabled(complexProperty))
                {
                    continue;
                }

                if (entityType.GetViewName() is not null)
                {
                    throw new NotSupportedException(
                        $"Entity '{entityType.DisplayName()}' is mapped to view '{entityType.GetViewName()}' "
                        + $"and contains struct-mapped complex property '{complexProperty.Name}'. "
                        + "DuckDB STRUCT support for view-mapped entities is not implemented in this phase. "
                        + "Query the view via FromSqlRaw instead, or map the entity to a table.");
                }

                ProcessStructComplexProperty(
                    complexProperty,
                    complexProperty.Name,
                    fieldName: null,
                    nestedPath: [],
                    columnMap);
            }

            if (columnMap.Count > 0)
            {
                entityType.SetStructColumnMap(columnMap, fromDataAnnotation: false);
            }
        }
    }

    /// <summary>
    ///     Returns <see langword="true" /> if the complex property should have struct field
    ///     metadata inferred for its scalar sub-properties.
    /// </summary>
    private static bool IsStructMappingEnabled(IConventionComplexProperty complexProperty)
    {
        // Fluent API: ComplexProperty(c => c.Location).UseStructMapping()
        if (complexProperty.FindAnnotation(DuckDBAnnotationNames.UseStructMapping)?.Value is true)
        {
            return true;
        }

        // Attribute: [UseStructMapping] on the CLR property (e.g. `public Location Location { get; set; }`).
        if (complexProperty.PropertyInfo?.IsDefined(typeof(UseStructMappingAttribute), inherit: true) == true)
        {
            return true;
        }

        return false;
    }

    private static DuckDBStructMapping ProcessStructComplexProperty(
        IConventionComplexProperty complexProperty,
        string structColumnName,
        string? fieldName,
        string[] nestedPath,
        Dictionary<string, DuckDBStructFieldInfo> columnMap)
    {
        var childMappings = new Dictionary<string, DuckDBStructChildMapping>();
        var complexType = complexProperty.ComplexType;

        // Scalar properties → leaf struct fields.
        foreach (var property in complexType.GetProperties())
        {
            if (property is IConventionProperty conventionProperty)
            {
                ProcessScalarProperty(
                    conventionProperty,
                    structColumnName,
                    nestedPath,
                    columnMap,
                    childMappings);
            }
        }

        // Nested complex properties → intermediate struct fields.
        foreach (var nestedComplexProperty in complexType.GetComplexProperties())
        {
            var nestedFieldName = ToCamelCase(nestedComplexProperty.Name);
            var extendedPath = AppendPath(nestedPath, nestedFieldName);

            var nestedMapping = ProcessStructComplexProperty(
                nestedComplexProperty,
                structColumnName,
                nestedFieldName,
                extendedPath,
                columnMap);

            childMappings[nestedComplexProperty.Name] = new DuckDBStructChildMapping(
                nestedFieldName,
                nestedMapping);
        }

        var mapping = new DuckDBStructMapping(structColumnName, fieldName, childMappings);
        complexProperty.SetStructMapping(mapping, fromDataAnnotation: false);
        return mapping;
    }

    private static void ProcessScalarProperty(
        IConventionProperty property,
        string structColumnName,
        string[] nestedPath,
        Dictionary<string, DuckDBStructFieldInfo> columnMap,
        Dictionary<string, DuckDBStructChildMapping> parentChildMappings)
    {
        // Determine the DuckDB struct leaf field name. This DEFENSIVELY decouples the
        // physical STRUCT leaf name from the EF column identity (alias) used in
        // projections/joins (#2):
        //   1. If the user set HasColumnName explicitly, treat that as the physical
        //      DuckDB leaf name (since HasColumnName IS the physical column name on a
        //      regular column, and the struct leaf IS the physical column for a sub-field).
        //   2. Otherwise, infer from the CLR property name (camelCased).
        var explicitColumnNameConfigurationSource = property.GetColumnNameConfigurationSource();
        var explicitColumnName = explicitColumnNameConfigurationSource is null
            ? null
            : property.GetColumnName();
        var inferredLeafFromClr = ToCamelCase(property.Name);

        var leafFieldName = explicitColumnName ?? inferredLeafFromClr;

        // EF column identity (alias in SELECT/JOIN). When the user did NOT explicitly
        // set HasColumnName, derive a unique name from the struct path so properties
        // sharing the same leaf under different struct columns (e.g. Billing.City vs
        // Shipping.City) don't collide in EF's column namespace. The actual DuckDB
        // struct leaf name is stored separately in DuckDBStructFieldInfo.LeafFieldName.
        string efColumnName;
        if (explicitColumnNameConfigurationSource is null)
        {
            efColumnName = FormatUniqueColumnName(structColumnName, nestedPath, inferredLeafFromClr);
            property.SetColumnName(efColumnName, fromDataAnnotation: false);
        }
        else
        {
            efColumnName = explicitColumnName ?? property.Name;
        }

        var conventionFieldInfo = new DuckDBStructFieldInfo(structColumnName, nestedPath, leafFieldName);

        // Honor explicit HasStructField configuration if present. This matters when the
        // user overrides the struct column name or nested path on a sub-property inside a
        // struct-mapped complex property.
        var existing = property.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value as DuckDBStructFieldInfo;
        var effectiveFieldInfo = existing switch
        {
            null => conventionFieldInfo,
            { LeafFieldName: null } => new DuckDBStructFieldInfo(
                existing.StructColumnName,
                [..existing.NestedFieldNames],
                leafFieldName),
            _ => existing
        };

        columnMap[efColumnName] = effectiveFieldInfo;
        parentChildMappings[property.Name] = new DuckDBStructChildMapping(leafFieldName);

        // Also maintain the legacy leaf-property annotation for consumers that still look
        // there (e.g. explicit HasStructField, some tests). For shared complex types this
        // annotation cannot represent multiple usages, so runtime code paths use the
        // per-complex-property mapping and the entity column map instead.
        if (existing is null)
        {
            property.SetOrRemoveAnnotation(
                DuckDBAnnotationNames.StructField,
                conventionFieldInfo,
                fromDataAnnotation: false);
        }
        else if (existing.LeafFieldName is null)
        {
            property.SetOrRemoveAnnotation(
                DuckDBAnnotationNames.StructField,
                effectiveFieldInfo,
                fromDataAnnotation: false);
        }
    }

    /// <summary>
    ///     Builds a unique column name from the struct column path and leaf property
    ///     name that won't collide with other struct-mapped properties on the same entity.
    ///     e.g. "Billing" + "City" → "billing_city",
    ///     "Shipping" + ["address"] + "Street" → "shipping_address_street".
    /// </summary>
    private static string FormatUniqueColumnName(
        string structColumnName,
        string[] nestedPath,
        string leafFieldName)
    {
        var parts = new List<string>(2 + nestedPath.Length)
        {
            ToCamelCase(structColumnName)
        };
        parts.AddRange(nestedPath);
        parts.Add(leafFieldName);
        return string.Join("_", parts);
    }

    /// <summary>
    ///     Converts a PascalCase name to camelCase by lower-casing the first character.
    ///     e.g. "City" → "city", "Address" → "address".
    /// </summary>
    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
        {
            return name;
        }

        return string.Create(name.Length, name, (span, src) =>
        {
            span[0] = char.ToLowerInvariant(src[0]);
            for (var i = 1; i < src.Length; i++)
            {
                span[i] = src[i];
            }
        });
    }

    private static string[] AppendPath(string[] path, string field)
    {
        if (path.Length == 0)
        {
            return [field];
        }

        var result = new string[path.Length + 1];
        path.CopyTo(result, 0);
        result[path.Length] = field;
        return result;
    }
}
