using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.ValueGeneration.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

/// <summary>
///     Assigns client-generated keys, such as Hi-Lo keys, to entities written by <c>BulkInsert</c> or <c>Upsert</c>
///     that still hold the key's sentinel value, matching what <c>SaveChanges</c> does when the entities are added.
/// </summary>
/// <remarks>
///     Keys are drawn as the entities are enumerated, so a streamed input is not buffered. A replayed attempt sees
///     the keys the failed attempt assigned and keeps them, because they are no longer sentinel values.
/// </remarks>
internal static class DuckDBClientKeyAssignment
{
    internal static IEnumerable<TEntity> Assign<TEntity>(
        DbContext context,
        IEntityType entityType,
        IEnumerable<TEntity> entities)
        where TEntity : class
    {
        List<(IClrPropertyGetter Getter, DuckDBStoreValueWriter Writer, IDuckDBClientKeyGenerator Generator)>? keys = null;
        foreach (var property in entityType.GetProperties())
        {
            if (property.GetValueGenerationStrategy() != DuckDBValueGenerationStrategy.HiLo)
            {
                continue;
            }

            if (!context.GetService<IValueGeneratorSelector>().TrySelect(property, entityType, out var selected)
                || selected is not IDuckDBClientKeyGenerator generator)
            {
                throw new InvalidOperationException(
                    $"No Hi-Lo key generator is registered for '{entityType.DisplayName()}.{property.Name}'.");
            }

            (keys ??= []).Add((property.GetGetter(), new DuckDBStoreValueWriter(property), generator));
        }

        return keys is null ? entities : AssignKeys(entities, keys);
    }

    private static IEnumerable<TEntity> AssignKeys<TEntity>(
        IEnumerable<TEntity> entities,
        List<(IClrPropertyGetter Getter, DuckDBStoreValueWriter Writer, IDuckDBClientKeyGenerator Generator)> keys)
        where TEntity : class
    {
        foreach (var entity in entities)
        {
            foreach (var (getter, writer, generator) in keys)
            {
                if (getter.HasSentinelValueUsingContainingEntity(entity))
                {
                    writer.Write(entity, generator.NextKey());
                }
            }

            yield return entity;
        }
    }
}