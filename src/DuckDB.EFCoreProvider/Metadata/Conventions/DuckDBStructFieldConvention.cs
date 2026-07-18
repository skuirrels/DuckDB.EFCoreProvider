using System.Collections.Generic;
using System.Reflection;
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
            foreach (var complexProperty in entityType.GetComplexProperties())
            {
                if (!IsStructMappingEnabled(complexProperty))
                {
                    continue;
                }

                ProcessComplexType(
                    (IReadOnlyTypeBase)complexProperty.ComplexType,
                    complexProperty.Name,
                    nestedPath: []);
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

    private static void ProcessComplexType(
        IReadOnlyTypeBase complexType,
        string structColumnName,
        string[] nestedPath)
    {
        // Scalar properties → leaf struct fields.
        foreach (var property in complexType.GetProperties())
        {
            if (property is IConventionProperty conventionProperty)
            {
                ProcessScalarProperty(
                    conventionProperty,
                    structColumnName,
                    nestedPath);
            }
        }

        // Nested complex properties → intermediate struct fields.
        foreach (var nestedComplexProperty in complexType.GetComplexProperties())
        {
            var nestedFieldName = ToCamelCase(nestedComplexProperty.Name);
            var extendedPath = AppendPath(nestedPath, nestedFieldName);

            ProcessComplexType(
                nestedComplexProperty.ComplexType,
                structColumnName,
                extendedPath);
        }
    }

    private static void ProcessScalarProperty(
        IConventionProperty property,
        string structColumnName,
        string[] nestedPath)
    {
        var leafFieldName = ToCamelCase(property.Name);

        // Build a unique EF column name that includes the struct path so that
        // properties with the same leaf name under different struct columns
        // (e.g. Billing.City vs Shipping.City) don't collide in EF's column
        // namespace. The actual DuckDB struct field name is stored separately
        // in DuckDBStructFieldInfo.LeafFieldName.
        if (property.GetColumnNameConfigurationSource() is null)
        {
            property.SetColumnName(
                FormatUniqueColumnName(structColumnName, nestedPath, leafFieldName),
                fromDataAnnotation: false);
        }

        // Infer struct field annotation, if not already set explicitly.
        var existing = property.FindAnnotation(DuckDBAnnotationNames.StructField)?.Value as DuckDBStructFieldInfo;
        if (existing is null)
        {
            property.SetOrRemoveAnnotation(
                DuckDBAnnotationNames.StructField,
                new DuckDBStructFieldInfo(structColumnName, nestedPath, leafFieldName),
                fromDataAnnotation: false);
            return;
        }

        // Struct field was already set explicitly (e.g. via HasStructField) — don't
        // overwrite it, but if the explicit annotation doesn't carry a LeafFieldName,
        // set it so the SQL generator uses the correct DuckDB field name instead of
        // the unique-ified EF column name.
        if (existing.LeafFieldName is null)
        {
            property.SetOrRemoveAnnotation(
                DuckDBAnnotationNames.StructField,
                new DuckDBStructFieldInfo(existing.StructColumnName, existing.NestedFieldNames.ToArray(), leafFieldName),
                fromDataAnnotation: false);
        }

        // If the annotation was already set by a previous entity (shared complex type
        // used under a different struct column), do not overwrite — the first entity
        // processed wins. Users must use explicit HasStructField per-entity to
        // disambiguate shared complex types mapped to different struct columns.
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
