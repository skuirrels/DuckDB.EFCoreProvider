using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using System.Diagnostics.CodeAnalysis;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     Selects updates to tables with inbound foreign keys for DuckDB's non-returning execution path.
///     It first uses command-local relational metadata, then consults an immutable model-scoped index
///     for EF command shapes that do not carry complete table metadata.
/// </summary>
internal static class DuckDBUpdateFallbackPlanner
{
    public static bool CanPlan(IReadOnlyModificationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.EntityState == EntityState.Modified
            && command.StoreStoredProcedure is null
            && IsReferencedPrincipal(command);
    }

    public static bool TryCreate(
        IReadOnlyModificationCommand command,
        IDuckDBEngineCapabilities capabilities,
        [NotNullWhen(true)] out DuckDBUpdateFallbackPlan? plan)
    {
        if (!CanPlan(command))
        {
            plan = null;
            return false;
        }

        plan = new DuckDBUpdateFallbackPlan(
            command,
            DuckDBDualRoleUpdatePlanner.Create(command, capabilities));
        return true;
    }

    public static DuckDBUpdateFallbackPlan Create(
        IReadOnlyModificationCommand command,
        IDuckDBEngineCapabilities capabilities)
        => TryCreate(command, capabilities, out var plan)
            ? plan
            : throw new ArgumentException(
                "The command must be an update to a table referenced by a foreign key.",
                nameof(command));

    private static bool IsReferencedPrincipal(IReadOnlyModificationCommand command)
    {
        if (command.Table?.ReferencingForeignKeyConstraints.Any() == true)
        {
            return true;
        }

        using var entries = command.Entries.GetEnumerator();
        return entries.MoveNext()
            && DuckDBReferencedTableIndex
                .For(entries.Current.EntityType.Model)
                .Contains(command.TableName, command.Schema);
    }
}


/// <summary>
///     Immutable rendering and execution inputs for one referenced-table update fallback.
/// </summary>
internal sealed class DuckDBUpdateFallbackPlan
{
    internal DuckDBUpdateFallbackPlan(
        IReadOnlyModificationCommand command,
        DuckDBDualRoleUpdatePlan dualRolePlan)
    {
        Command = command;
        TableName = command.TableName;
        Schema = command.Schema;
        WriteOperations = dualRolePlan.WriteOperations;
        ReadOperations = command.ColumnModifications.Where(operation => operation.IsRead).ToArray();
        ConditionOperations = command.ColumnModifications.Where(operation => operation.IsCondition).ToArray();
        KeyOperations = command.ColumnModifications.Where(operation => operation.IsKey).ToArray();
        HasChangedForeignKeyWrites = dualRolePlan.HasChangedForeignKeyWrites;
    }

    public IReadOnlyModificationCommand Command { get; }

    public string TableName { get; }

    public string? Schema { get; }

    public IReadOnlyList<IColumnModification> WriteOperations { get; }

    public IReadOnlyList<IColumnModification> ReadOperations { get; }

    public IReadOnlyList<IColumnModification> ConditionOperations { get; }

    public IReadOnlyList<IColumnModification> KeyOperations { get; }

    public bool HasChangedForeignKeyWrites { get; }
}