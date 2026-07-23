using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Shared detection logic for struct-mapped complex properties.
/// </summary>
/// <remarks>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs.
/// </remarks>
internal static class StructMappingHelper
{
    /// <summary>
    ///     Returns <see langword="true" /> if the entity type has any struct-mapped complex property
    ///     (direct or nested). Such properties produce consolidated STRUCT columns whose sub-field values
    ///     cannot be accessed via the EF scalar-property iteration used by bulk insert/upsert.
    /// </summary>
    internal static bool HasStructMappedComplexProperties(IEntityType entityType)
        => HasStructMappedComplexPropertiesCore(entityType);

    private static bool HasStructMappedComplexPropertiesCore(IReadOnlyTypeBase typeBase)
    {
        foreach (var complexProperty in typeBase.GetComplexProperties())
        {
            if (complexProperty.FindAnnotation(DuckDBAnnotationNames.UseStructMapping)?.Value is true
                || complexProperty.PropertyInfo?.IsDefined(typeof(UseStructMappingAttribute), inherit: true) == true)
            {
                return true;
            }

            if (HasStructMappedComplexPropertiesCore(complexProperty.ComplexType))
            {
                return true;
            }
        }

        return false;
    }
}