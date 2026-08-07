using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DuckDB.EFCoreProvider.Metadata.Internal;

internal static class DuckDBStructRelationalMetadata
{
    public static DuckDBStructFieldInfo? FindFieldInfo(IColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        // The entity-level map disambiguates shared complex types used under different STRUCT roots.
        foreach (var mapping in column.Table?.EntityTypeMappings ?? [])
        {
            if (mapping.TypeBase is IEntityType entityType
                && entityType.GetStructColumnMap() is { } columnMap
                && columnMap.TryGetValue(column.Name, out var mappedField))
            {
                return mappedField;
            }
        }

        return column.PropertyMappings
            .Select(mapping => mapping.Property.GetStructFieldInfo())
            .FirstOrDefault(candidate => candidate is not null);
    }

    public static bool IsStructFieldForeignKey(IForeignKeyConstraint foreignKey)
    {
        ArgumentNullException.ThrowIfNull(foreignKey);
        return foreignKey.Columns.Any(column => FindFieldInfo(column) is not null);
    }
}