using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections.Concurrent;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     Cache for struct field metadata keyed by entity type and column name, enabling O(1) lookups
///     during SQL generation instead of O(n) entity iteration.
/// </summary>
public interface IStructFieldInfoCache
{
    /// <summary>
    ///     Gets struct field metadata for a column on an entity type, or <see langword="null" />
    ///     when the column is not a struct field.
    /// </summary>
    /// <param name="entityType">The entity type to query.</param>
    /// <param name="columnName">The EF column name (alias used in SELECT/JOIN).</param>
    /// <returns>Struct field info if found; <see langword="null" /> otherwise.</returns>
    DuckDBStructFieldInfo? GetStructFieldInfo(IEntityType entityType, string columnName);
}

/// <summary>
///     Default implementation of <see cref="IStructFieldInfoCache" /> using a thread-safe
///     dictionary to cache lookups per entity type and column name.
/// </summary>
internal sealed class StructFieldInfoCache : IStructFieldInfoCache
{
    private readonly ConcurrentDictionary<(Type ClrType, string ColumnName), DuckDBStructFieldInfo?> _cache = new();

    /// <inheritdoc />
    public DuckDBStructFieldInfo? GetStructFieldInfo(IEntityType entityType, string columnName)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentException.ThrowIfNullOrEmpty(columnName);

        var key = (entityType.ClrType, columnName);
        if (!_cache.TryGetValue(key, out var cachedResult))
        {
            // Lazy-load on first access: query the entity's struct column map.
            // The map is built once at model finalization by the convention and
            // stored on the entity type, so this lookup is cheap and repeatable.
            var columnMap = entityType.GetStructColumnMap();
            var result = columnMap?.TryGetValue(columnName, out var info) == true ? info : null;
            
            _cache.TryAdd(key, result);
            return result;
        }

        return cachedResult;
    }
}
