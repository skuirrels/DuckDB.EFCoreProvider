using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;
using System.Diagnostics.CodeAnalysis;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     Owns bulk-update eligibility and resolves a compatible command run into immutable rendering inputs.
/// </summary>
internal static class DuckDBBulkUpdatePlanner
{
    public static bool CanPlan(IReadOnlyModificationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.EntityState == EntityState.Modified
            && command.StoreStoredProcedure is null
            && DuckDBModificationCommandShape.HasColumns(command, DuckDBModificationColumnRole.Write)
            && !DuckDBModificationCommandShape.HasColumns(command, DuckDBModificationColumnRole.Read)
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
                DuckDBModificationColumnRole.Write)
            && DuckDBModificationCommandShape.ColumnNamesEqual(
                first,
                second,
                DuckDBModificationColumnRole.Condition);
    }

    public static bool TryCreate(
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        [NotNullWhen(true)] out DuckDBBulkUpdatePlan? plan)
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

        plan = new DuckDBBulkUpdatePlan(
            commands,
            DuckDBModificationCommandShape.CountColumns(
                firstCommand,
                DuckDBModificationColumnRole.Condition),
            DuckDBModificationCommandShape.CountColumns(
                firstCommand,
                DuckDBModificationColumnRole.Write));
        return true;
    }

    public static DuckDBBulkUpdatePlan Create(IReadOnlyList<IReadOnlyModificationCommand> commands)
        => TryCreate(commands, out var plan)
            ? plan
            : throw new ArgumentException(
                "Bulk update commands must be eligible updates with matching tables, schemas, condition columns, and write columns.",
                nameof(commands));
}

/// <summary>
///     Immutable snapshot of a validated bulk-update command run.
/// </summary>
internal sealed class DuckDBBulkUpdatePlan
{
    private readonly IReadOnlyModificationCommand[] _commands;
    private readonly int[] _keyIndexes;
    private readonly int[] _writeIndexes;
    private readonly int _keyColumnCount;
    private readonly int _writeColumnCount;

    internal DuckDBBulkUpdatePlan(
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        int keyColumnCount,
        int writeColumnCount)
    {
        _commands = new IReadOnlyModificationCommand[commands.Count];
        _keyIndexes = new int[commands.Count * keyColumnCount];
        _writeIndexes = new int[commands.Count * writeColumnCount];
        _keyColumnCount = keyColumnCount;
        _writeColumnCount = writeColumnCount;

        for (var i = 0; i < commands.Count; i++)
        {
            _commands[i] = commands[i];
            CollectColumnIndexes(commands[i], _keyIndexes, i * keyColumnCount, conditionColumns: true);
            CollectColumnIndexes(commands[i], _writeIndexes, i * writeColumnCount, conditionColumns: false);
        }

        TableName = _commands[0].TableName;
        Schema = _commands[0].Schema;
    }

    public string TableName { get; }

    public string? Schema { get; }

    public int RowCount => _commands.Length;

    public int KeyColumnCount => _keyColumnCount;

    public int WriteColumnCount => _writeColumnCount;

    public string GetKeyColumnName(int index)
        => _commands[0].ColumnModifications[_keyIndexes[index]].ColumnName;

    public string GetWriteColumnName(int index)
        => _commands[0].ColumnModifications[_writeIndexes[index]].ColumnName;

    public string GetOriginalKeyParameterName(int rowIndex, int keyIndex)
        => _commands[rowIndex].ColumnModifications[_keyIndexes[(rowIndex * _keyColumnCount) + keyIndex]].OriginalParameterName!;

    public string GetWriteParameterName(int rowIndex, int writeIndex)
        => _commands[rowIndex].ColumnModifications[_writeIndexes[(rowIndex * _writeColumnCount) + writeIndex]].ParameterName!;

    private static void CollectColumnIndexes(
        IReadOnlyModificationCommand command,
        int[] indexes,
        int targetIndex,
        bool conditionColumns)
    {
        var modifications = command.ColumnModifications;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (conditionColumns ? modifications[i].IsCondition : modifications[i].IsWrite)
            {
                indexes[targetIndex++] = i;
            }
        }
    }
}