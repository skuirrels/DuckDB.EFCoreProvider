using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using System.Diagnostics.CodeAnalysis;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     Owns bulk-delete eligibility and resolves a compatible command run into immutable rendering inputs.
/// </summary>
internal static class DuckDBBulkDeletePlanner
{
    public static bool CanPlan(IReadOnlyModificationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.EntityState == EntityState.Deleted
            && command.StoreStoredProcedure is null
            && DuckDBModificationCommandShape.AllConditionsAreKeys(command)
            && DuckDBModificationCommandShape.HasColumns(command, DuckDBModificationColumnRole.Condition);
    }

    public static bool CanAppend(
        IReadOnlyModificationCommand first,
        IReadOnlyModificationCommand second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return CanPlan(first)
            && CanPlan(second)
            && first.TableName == second.TableName
            && first.Schema == second.Schema
            && DuckDBModificationCommandShape.ColumnNamesEqual(
                first,
                second,
                DuckDBModificationColumnRole.Condition);
    }

    public static bool TryCreate(
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        [NotNullWhen(true)] out DuckDBBulkDeletePlan? plan)
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Count == 0 || !CanPlan(commands[0]))
        {
            plan = null;
            return false;
        }

        var firstCommand = commands[0];
        for (var i = 1; i < commands.Count; i++)
        {
            if (!CanAppend(firstCommand, commands[i]))
            {
                plan = null;
                return false;
            }
        }

        plan = new DuckDBBulkDeletePlan(
            commands,
            DuckDBModificationCommandShape.CountColumns(
                firstCommand,
                DuckDBModificationColumnRole.Condition));
        return true;
    }

    public static DuckDBBulkDeletePlan Create(IReadOnlyList<IReadOnlyModificationCommand> commands)
        => TryCreate(commands, out var plan)
            ? plan
            : throw new ArgumentException(
                "Bulk delete commands must be eligible deletes with matching tables, schemas, and key columns.",
                nameof(commands));
}

/// <summary>
///     Immutable snapshot of a validated bulk-delete command run.
/// </summary>
internal sealed class DuckDBBulkDeletePlan
{
    private readonly string[] _keyColumnNames;
    private readonly string[] _originalKeyParameterNames;

    internal DuckDBBulkDeletePlan(
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        int keyColumnCount)
    {
        _keyColumnNames = new string[keyColumnCount];
        _originalKeyParameterNames = new string[commands.Count * keyColumnCount];

        CopyKeyColumns(commands[0], _keyColumnNames);

        for (var rowIndex = 0; rowIndex < commands.Count; rowIndex++)
        {
            CopyOriginalKeyParameterNames(
                commands[rowIndex],
                _originalKeyParameterNames,
                rowIndex * keyColumnCount);
        }

        TableName = commands[0].TableName;
        Schema = commands[0].Schema;
        RowCount = commands.Count;
    }

    public string TableName { get; }

    public string? Schema { get; }

    public int RowCount { get; }

    public int KeyColumnCount => _keyColumnNames.Length;

    public string GetKeyColumnName(int keyIndex)
        => _keyColumnNames[keyIndex];

    public string GetOriginalKeyParameterName(int rowIndex, int keyIndex)
        => _originalKeyParameterNames[(rowIndex * KeyColumnCount) + keyIndex];

    private static void CopyKeyColumns(
        IReadOnlyModificationCommand command,
        string[] target)
    {
        var targetIndex = 0;
        var modifications = command.ColumnModifications;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (modifications[i].IsCondition)
            {
                target[targetIndex++] = modifications[i].ColumnName;
            }
        }
    }

    private static void CopyOriginalKeyParameterNames(
        IReadOnlyModificationCommand command,
        string[] target,
        int targetIndex)
    {
        var modifications = command.ColumnModifications;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (modifications[i].IsCondition)
            {
                target[targetIndex++] = modifications[i].OriginalParameterName
                    ?? throw new InvalidOperationException(
                        $"Bulk delete key column '{modifications[i].ColumnName}' has no original-value parameter.");
            }
        }
    }
}