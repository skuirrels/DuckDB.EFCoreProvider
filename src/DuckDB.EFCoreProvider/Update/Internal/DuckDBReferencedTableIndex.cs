using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     Immutable, model-scoped lookup of tables targeted by conceptual foreign keys.
/// </summary>
internal sealed class DuckDBReferencedTableIndex
{
    private static readonly ConditionalWeakTable<IModel, DuckDBReferencedTableIndex> Cache = new();

    private readonly FrozenSet<StoreObjectIdentifier> _tables;

    private DuckDBReferencedTableIndex(IModel model)
        => _tables = Build(model);

    public static DuckDBReferencedTableIndex For(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Cache.GetValue(model, static currentModel => new DuckDBReferencedTableIndex(currentModel));
    }

    public bool Contains(string tableName, string? schema)
        => _tables.Contains(StoreObjectIdentifier.Table(tableName, schema));

    private static FrozenSet<StoreObjectIdentifier> Build(IModel model)
        => model.GetRelationalModel().Tables
            .Where(table => table.ReferencingForeignKeyConstraints.Any())
            .Select(table => StoreObjectIdentifier.Table(table.Name, table.Schema))
            .ToFrozenSet();
}