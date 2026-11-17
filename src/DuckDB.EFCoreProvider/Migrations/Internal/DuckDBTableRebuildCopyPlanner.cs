using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace DuckDB.EFCoreProvider.Migrations.Internal;

/// <summary>How one column of a rebuilt table is filled from the backup of the old table.</summary>
/// <param name="TargetColumn">The column in the rebuilt table.</param>
/// <param name="SourceColumn">
///     The backup column holding its old values, or <see langword="null" /> when the column is new in this migration.
/// </param>
/// <param name="Fallback">
///     The migration operation whose default fills a new column, or replaces <c>NULL</c> in a column that became
///     required; <see langword="null" /> when the old values are copied unchanged.
/// </param>
internal sealed record DuckDBRebuildColumnCopy(string TargetColumn, string? SourceColumn, ColumnOperation? Fallback);

/// <summary>
///     Plans the <c>INSERT ... SELECT</c> that copies rows from a rebuild backup into a table recreated from the target
///     model, when the rebuild also absorbs the migration's column operations on that table.
/// </summary>
/// <remarks>
///     The target model of a scaffolded migration carries no default for a column that EF backfills, because EF puts
///     the backfill value only on the <see cref="AddColumnOperation" /> or <see cref="AlterColumnOperation" />. The copy
///     therefore takes those values from the operations, not from the rebuilt table.
/// </remarks>
internal static class DuckDBTableRebuildCopyPlanner
{
    internal static IReadOnlyList<DuckDBRebuildColumnCopy> Plan(
        IEnumerable<string> targetColumns,
        IEnumerable<MigrationOperation> tableOperations)
    {
        // Current column name -> backup column name; null marks a column with no backup values.
        var sources = new Dictionary<string, string?>(StringComparer.Ordinal);
        var fallbacks = new Dictionary<string, ColumnOperation>(StringComparer.Ordinal);

        foreach (var operation in tableOperations)
        {
            switch (operation)
            {
                case AddColumnOperation add:
                    sources[add.Name] = null;
                    SetFallback(add.Name, HasDefault(add) ? add : null);
                    break;
                case RenameColumnOperation rename:
                    var source = sources.Remove(rename.Name, out var renamed) ? renamed : rename.Name;
                    sources[rename.NewName] = source;
                    SetFallback(rename.NewName, fallbacks.Remove(rename.Name, out var moved) ? moved : null);
                    break;
                case DropColumnOperation drop:
                    sources[drop.Name] = null;
                    fallbacks.Remove(drop.Name);
                    break;
                case AlterColumnOperation alter when alter.OldColumn.IsNullable && !alter.IsNullable && HasDefault(alter):
                    SetFallback(alter.Name, alter);
                    break;
            }
        }

        return targetColumns
            .Select(column => new DuckDBRebuildColumnCopy(
                column,
                sources.TryGetValue(column, out var source) ? source : column,
                fallbacks.GetValueOrDefault(column)))
            .ToArray();

        void SetFallback(string column, ColumnOperation? fallback)
        {
            if (fallback is null)
            {
                fallbacks.Remove(column);
            }
            else
            {
                fallbacks[column] = fallback;
            }
        }
    }

    private static bool HasDefault(ColumnOperation operation)
        => operation.DefaultValueSql is not null || operation.DefaultValue is not null and not DBNull;
}