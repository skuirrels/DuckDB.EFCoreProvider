using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Internal;
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
        => CanPlan(command, DuckDBEngineCapabilities.Native);

    public static bool CanPlan(
        IReadOnlyModificationCommand command,
        IDuckDBEngineCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(capabilities);

        return CanPlan(
            command,
            DuckDBDualRoleUpdatePlanner.Create(command, capabilities).WriteOperations);
    }

    private static bool CanPlan(
        IReadOnlyModificationCommand command,
        IReadOnlyList<IColumnModification> writeOperations)
        => command.EntityState == EntityState.Modified
            && command.StoreStoredProcedure is null
            && writeOperations.Count > 0
            && !DuckDBModificationCommandShape.HasColumns(command, DuckDBModificationColumnRole.Read)
            && DuckDBModificationCommandShape.AllConditionsAreKeys(command)
            && DuckDBModificationCommandShape.HasColumns(command, DuckDBModificationColumnRole.Condition);

    public static bool CanAppend(
        IReadOnlyModificationCommand first,
        IReadOnlyModificationCommand second)
        => CanAppend(first, second, DuckDBEngineCapabilities.Native);

    public static bool CanAppend(
        IReadOnlyModificationCommand first,
        IReadOnlyModificationCommand second,
        IDuckDBEngineCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(capabilities);

        var firstWrites = DuckDBDualRoleUpdatePlanner.Create(first, capabilities).WriteOperations;
        var secondWrites = DuckDBDualRoleUpdatePlanner.Create(second, capabilities).WriteOperations;

        return CanPlan(first, firstWrites)
            && CanPlan(second, secondWrites)
            && first.TableName == second.TableName
            && first.Schema == second.Schema
            && WriteShapesEqual(firstWrites, secondWrites)
            && DuckDBModificationCommandShape.ColumnNamesEqual(
                first,
                second,
                DuckDBModificationColumnRole.Condition);
    }

    public static bool TryCreate(
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        [NotNullWhen(true)] out DuckDBBulkUpdatePlan? plan)
        => TryCreate(commands, DuckDBEngineCapabilities.Native, out plan);

    public static bool TryCreate(
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        IDuckDBEngineCapabilities capabilities,
        [NotNullWhen(true)] out DuckDBBulkUpdatePlan? plan)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (commands.Count == 0)
        {
            plan = null;
            return false;
        }

        var dualRolePlans = new DuckDBDualRoleUpdatePlan[commands.Count];
        for (var i = 0; i < commands.Count; i++)
        {
            dualRolePlans[i] = DuckDBDualRoleUpdatePlanner.Create(commands[i], capabilities);
        }

        if (!CanPlan(commands[0], dualRolePlans[0].WriteOperations))
        {
            plan = null;
            return false;
        }

        var firstCommand = commands[0];
        for (var i = 1; i < commands.Count; i++)
        {
            if (!CanPlan(commands[i], dualRolePlans[i].WriteOperations)
                || firstCommand.TableName != commands[i].TableName
                || firstCommand.Schema != commands[i].Schema
                || !WriteShapesEqual(dualRolePlans[0].WriteOperations, dualRolePlans[i].WriteOperations)
                || !DuckDBModificationCommandShape.ColumnNamesEqual(
                    firstCommand,
                    commands[i],
                    DuckDBModificationColumnRole.Condition))
            {
                plan = null;
                return false;
            }
        }

        plan = new DuckDBBulkUpdatePlan(
            commands,
            dualRolePlans,
            DuckDBModificationCommandShape.CountColumns(
                firstCommand,
                DuckDBModificationColumnRole.Condition));
        return true;
    }

    public static DuckDBBulkUpdatePlan Create(IReadOnlyList<IReadOnlyModificationCommand> commands)
        => Create(commands, DuckDBEngineCapabilities.Native);

    public static DuckDBBulkUpdatePlan Create(
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        IDuckDBEngineCapabilities capabilities)
        => TryCreate(commands, capabilities, out var plan)
            ? plan
            : throw new ArgumentException(
                "Bulk update commands must be eligible updates with matching tables, schemas, condition columns, and write columns.",
                nameof(commands));

    private static bool WriteShapesEqual(
        IReadOnlyList<IColumnModification> first,
        IReadOnlyList<IColumnModification> second)
    {
        var firstPlan = CreateMutationPlan(first);
        var secondPlan = CreateMutationPlan(second);
        return firstPlan is null
            ? secondPlan is null
                && first.Select(modification => modification.ColumnName)
                    .SequenceEqual(second.Select(modification => modification.ColumnName), StringComparer.Ordinal)
            : secondPlan is not null && firstPlan.HasSamePhysicalShape(secondPlan);
    }

    private static DuckDBStructMutationPlan? CreateMutationPlan(IReadOnlyList<IColumnModification> writes)
    {
        DuckDBStructMutationPlan.TryCreate(writes, DuckDBStructMutationMode.BulkUpdate, out var plan);
        return plan;
    }
}

/// <summary>
///     Immutable snapshot of a validated bulk-update command run.
/// </summary>
internal sealed class DuckDBBulkUpdatePlan
{
    private readonly IReadOnlyModificationCommand[] _commands;
    private readonly int[] _keyIndexes;
    private readonly IColumnModification[][] _writeOperations;
    private readonly int _keyColumnCount;
    private readonly int _writeColumnCount;

    internal DuckDBBulkUpdatePlan(
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        IReadOnlyList<DuckDBDualRoleUpdatePlan> dualRolePlans,
        int keyColumnCount)
    {
        _commands = new IReadOnlyModificationCommand[commands.Count];
        _keyIndexes = new int[commands.Count * keyColumnCount];
        _writeOperations = new IColumnModification[commands.Count][];
        _keyColumnCount = keyColumnCount;
        _writeColumnCount = dualRolePlans[0].WriteOperations.Count;

        for (var i = 0; i < commands.Count; i++)
        {
            _commands[i] = commands[i];
            CollectColumnIndexes(commands[i], _keyIndexes, i * keyColumnCount, conditionColumns: true);
            _writeOperations[i] = dualRolePlans[i].WriteOperations.ToArray();
        }

        TableName = _commands[0].TableName;
        Schema = _commands[0].Schema;
        StructMutationPlan = CreateStructMutationPlan();
    }

    public string TableName { get; }

    public string? Schema { get; }

    public int RowCount => _commands.Length;

    public int KeyColumnCount => _keyColumnCount;

    public int WriteColumnCount => _writeColumnCount;

    public DuckDBStructMutationPlan? StructMutationPlan { get; }

    public string GetKeyColumnName(int index)
        => _commands[0].ColumnModifications[_keyIndexes[index]].ColumnName;

    public string GetWriteColumnName(int index)
        => _writeOperations[0][index].ColumnName;

    public void CollectWriteColumns(int rowIndex, List<IColumnModification> target)
    {
        target.Clear();
        foreach (var modification in _writeOperations[rowIndex])
        {
            target.Add(modification);
        }
    }

    public string GetOriginalKeyParameterName(int rowIndex, int keyIndex)
        => _commands[rowIndex].ColumnModifications[_keyIndexes[(rowIndex * _keyColumnCount) + keyIndex]].OriginalParameterName!;

    public string GetWriteParameterName(int rowIndex, int writeIndex)
        => _writeOperations[rowIndex][writeIndex].ParameterName!;

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

    private DuckDBStructMutationPlan? CreateStructMutationPlan()
    {
        DuckDBStructMutationPlan.TryCreate(
            _writeOperations[0],
            DuckDBStructMutationMode.BulkUpdate,
            out var plan);
        return plan;
    }
}