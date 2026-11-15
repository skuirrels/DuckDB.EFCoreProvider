using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DuckDB.EFCoreProvider.ValueGeneration.Internal;

/// <summary>
///     One Hi-Lo counter table and the key columns that draw values from it.
/// </summary>
/// <remarks>
///     The key columns seed a new counter above the largest key already stored, so switching an existing table to
///     Hi-Lo never hands out a value the application assigned by hand.
/// </remarks>
internal sealed record DuckDBHiLoCounter(
    string? Schema,
    string TableName,
    IReadOnlyList<DuckDBHiLoKeyColumn> KeyColumns)
{
    /// <summary>
    ///     Resolves the counter for <paramref name="property" /> from the model, including every other Hi-Lo key
    ///     property that shares its counter table.
    /// </summary>
    internal static DuckDBHiLoCounter For(IProperty property)
    {
        var tableName = property.GetHiLoTableName();
        var model = property.DeclaringType.Model;
        var keyColumns = new List<DuckDBHiLoKeyColumn>();
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var candidate in entityType.GetDeclaredProperties())
            {
                if (candidate.GetValueGenerationStrategy() != DuckDBValueGenerationStrategy.HiLo
                    || !string.Equals(candidate.GetHiLoTableName(), tableName, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var mapping in candidate.GetTableColumnMappings())
                {
                    var table = mapping.TableMapping.Table;
                    keyColumns.Add(new DuckDBHiLoKeyColumn(table.Schema, table.Name, mapping.Column.Name));
                }
            }
        }

        return new DuckDBHiLoCounter(model.GetDefaultSchema(), tableName, keyColumns.Distinct().ToArray());
    }
}

/// <summary>
///     A table column whose values a Hi-Lo counter generates.
/// </summary>
internal sealed record DuckDBHiLoKeyColumn(string? Schema, string Table, string Column);