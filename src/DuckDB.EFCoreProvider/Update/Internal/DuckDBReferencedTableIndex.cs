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
    private readonly FrozenDictionary<StoreObjectIdentifier, FrozenSet<string>> _outboundForeignKeyColumns;

    private DuckDBReferencedTableIndex(IModel model)
    {
        (_tables, _outboundForeignKeyColumns) = Build(model);
    }

    public static DuckDBReferencedTableIndex For(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Cache.GetValue(model, static currentModel => new DuckDBReferencedTableIndex(currentModel));
    }

    public bool Contains(string tableName, string? schema)
        => _tables.Contains(StoreObjectIdentifier.Table(tableName, schema));

    public bool IsOutboundForeignKeyColumn(string tableName, string? schema, string columnName)
        => _outboundForeignKeyColumns.TryGetValue(
                StoreObjectIdentifier.Table(tableName, schema),
                out var columns)
            && columns.Contains(columnName);

    private static (
        FrozenSet<StoreObjectIdentifier> ReferencedTables,
        FrozenDictionary<StoreObjectIdentifier, FrozenSet<string>> OutboundForeignKeyColumns) Build(IModel model)
    {
        var referencedTables = model.GetRelationalModel().Tables
            .Where(table => table.ReferencingForeignKeyConstraints.Any())
            .ToArray();

        return (
            referencedTables
                .Select(table => StoreObjectIdentifier.Table(table.Name, table.Schema))
                .ToFrozenSet(),
            referencedTables
                .Where(table => table.ForeignKeyConstraints.Any())
                .ToFrozenDictionary(
                    table => StoreObjectIdentifier.Table(table.Name, table.Schema),
                    table => table.ForeignKeyConstraints
                        .SelectMany(foreignKey => foreignKey.Columns)
                        .Select(column => column.Name)
                        .ToFrozenSet()));
    }
}