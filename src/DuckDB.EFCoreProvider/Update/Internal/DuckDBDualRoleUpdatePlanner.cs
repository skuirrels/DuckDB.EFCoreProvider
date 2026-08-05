using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     Resolves native physical-foreign-key writes for a table that has both inbound and outbound relationships.
/// </summary>
internal static class DuckDBDualRoleUpdatePlanner
{
    public static bool AppliesTo(IDuckDBEngineCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return capabilities.SupportsSchemaConstraints
            && !capabilities.SupportsReferencedTableForeignKeyUpdates;
    }

    public static DuckDBDualRoleUpdatePlan Create(
        IReadOnlyModificationCommand command,
        IDuckDBEngineCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (!AppliesTo(capabilities)
            || command.EntityState != EntityState.Modified
            || command.StoreStoredProcedure is not null)
        {
            return DuckDBDualRoleUpdatePlan.Unchanged(command);
        }

        using var entries = command.Entries.GetEnumerator();
        if (!entries.MoveNext())
        {
            return DuckDBDualRoleUpdatePlan.Unchanged(command);
        }

        var index = DuckDBReferencedTableIndex.For(entries.Current.EntityType.Model);
        if (!index.Contains(command.TableName, command.Schema))
        {
            return DuckDBDualRoleUpdatePlan.Unchanged(command);
        }

        var writeOperations = new List<IColumnModification>();
        var unchangedWrites = new List<IColumnModification>();
        var changedWrites = new List<IColumnModification>();
        foreach (var modification in command.ColumnModifications)
        {
            if (!modification.IsWrite)
            {
                continue;
            }

            if (!index.IsOutboundForeignKeyColumn(
                    command.TableName,
                    command.Schema,
                    modification.ColumnName))
            {
                writeOperations.Add(modification);
                continue;
            }

            if (ValuesEqual(modification))
            {
                unchangedWrites.Add(modification);
            }
            else
            {
                changedWrites.Add(modification);
                writeOperations.Add(modification);
            }
        }

        return new DuckDBDualRoleUpdatePlan(command, writeOperations, unchangedWrites, changedWrites);
    }

    private static bool ValuesEqual(IColumnModification modification)
    {
        if (modification is { Entry: { } entry, Property: { } property })
        {
            return property.GetProviderValueComparer().Equals(
                entry.GetOriginalProviderValue(property),
                entry.GetCurrentProviderValue(property));
        }

        return Equals(modification.OriginalValue, modification.Value);
    }
}

/// <summary>
///     Immutable classification of outbound foreign-key writes for one dual-role table update.
/// </summary>
internal sealed class DuckDBDualRoleUpdatePlan
{
    private readonly IColumnModification[] _writeOperations;
    private readonly IColumnModification[] _unchangedForeignKeyWrites;
    private readonly IColumnModification[] _changedForeignKeyWrites;

    public DuckDBDualRoleUpdatePlan(
        IReadOnlyModificationCommand command,
        IReadOnlyList<IColumnModification> writeOperations,
        IReadOnlyList<IColumnModification> unchangedForeignKeyWrites,
        IReadOnlyList<IColumnModification> changedForeignKeyWrites)
    {
        Command = command;
        _writeOperations = writeOperations.ToArray();
        _unchangedForeignKeyWrites = unchangedForeignKeyWrites.ToArray();
        _changedForeignKeyWrites = changedForeignKeyWrites.ToArray();
    }

    public IReadOnlyModificationCommand Command { get; }

    public IReadOnlyList<IColumnModification> WriteOperations => _writeOperations;

    public IReadOnlyList<IColumnModification> UnchangedForeignKeyWrites => _unchangedForeignKeyWrites;

    public IReadOnlyList<IColumnModification> ChangedForeignKeyWrites => _changedForeignKeyWrites;

    public bool HasChangedForeignKeyWrites => _changedForeignKeyWrites.Length > 0;

    public bool RequiresConditionalForeignKeyUpdate
        => _writeOperations.Length == 0 && _unchangedForeignKeyWrites.Length > 0;

    public IEnumerable<string> ChangedColumnNames
        => _changedForeignKeyWrites.Select(modification => modification.ColumnName).Distinct(StringComparer.Ordinal);

    public static DuckDBDualRoleUpdatePlan Unchanged(IReadOnlyModificationCommand command)
        => new(
            command,
            command.ColumnModifications.Where(modification => modification.IsWrite).ToArray(),
            [],
            []);
}