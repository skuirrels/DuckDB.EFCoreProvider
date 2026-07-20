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
            if (explicitColumnNameConfigurationSource is null)
            {
                property.SetColumnName(
                    FormatUniqueColumnName(structColumnName, nestedPath, inferredLeafFromClr),
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

            // Struct field was already set explicitly (e.g. via HasStructField). Don't
            // overwrite it, but if the explicit annotation doesn't carry a LeafFieldName,
            // fill it in so the SQL generator uses the user's intent (HasColumnName wins
            // over camelCase inference).
            if (existing.LeafFieldName is null)
            {
                property.SetOrRemoveAnnotation(
                    DuckDBAnnotationNames.StructField,
                    new DuckDBStructFieldInfo(existing.StructColumnName, [..existing.NestedFieldNames], leafFieldName),
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
