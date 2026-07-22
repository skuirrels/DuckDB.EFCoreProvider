using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     DuckDB specific extension methods for <see cref="IComplexProperty" />.
/// </summary>
public static class DuckDBComplexPropertyExtensions
{
    /// <summary>
    ///     Returns the <see cref="DuckDBStructMapping" /> stored on this complex property,
    ///     or <see langword="null" /> when the property is not part of a DuckDB STRUCT mapping.
    /// </summary>
    public static DuckDBStructMapping? GetStructMapping(this IReadOnlyComplexProperty complexProperty)
        => complexProperty.FindAnnotation(DuckDBAnnotationNames.StructMapping)?.Value as DuckDBStructMapping;

    /// <summary>
    ///     Sets the <see cref="DuckDBStructMapping" /> on this convention complex property.
    /// </summary>
    public static DuckDBStructMapping SetStructMapping(
        this IConventionComplexProperty complexProperty,
        DuckDBStructMapping mapping,
        bool fromDataAnnotation = false)
    {
        complexProperty.SetOrRemoveAnnotation(DuckDBAnnotationNames.StructMapping, mapping, fromDataAnnotation);
        return mapping;
    }

    /// <summary>
    ///     Returns the column-name-to-field-info map stored on this entity type, or
    ///     <see langword="null" /> when the entity has no struct-mapped complex properties.
    /// </summary>
    public static IReadOnlyDictionary<string, DuckDBStructFieldInfo>? GetStructColumnMap(this IReadOnlyEntityType entityType)
        => entityType.FindAnnotation(DuckDBAnnotationNames.StructColumnMap)?.Value
            as IReadOnlyDictionary<string, DuckDBStructFieldInfo>;

    /// <summary>
    ///     Sets the column-name-to-field-info map on this convention entity type.
    /// </summary>
    public static IReadOnlyDictionary<string, DuckDBStructFieldInfo> SetStructColumnMap(
        this IConventionEntityType entityType,
        IReadOnlyDictionary<string, DuckDBStructFieldInfo> map,
        bool fromDataAnnotation = false)
    {
        entityType.SetOrRemoveAnnotation(DuckDBAnnotationNames.StructColumnMap, map, fromDataAnnotation);
        return map;
    }
}
