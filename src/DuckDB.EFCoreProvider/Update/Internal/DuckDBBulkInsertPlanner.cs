using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using System.Diagnostics.CodeAnalysis;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     Owns bulk-insert eligibility and resolves a compatible command run into immutable rendering inputs.
/// </summary>
internal static class DuckDBBulkInsertPlanner
{
    public static bool CanPlan(IReadOnlyModificationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.EntityState == EntityState.Added
            && command.StoreStoredProcedure is null
            && DuckDBModificationCommandShape.HasColumns(command, DuckDBModificationColumnRole.Write);
    }

    public static bool CanAppend(
        IReadOnlyModificationCommand first,
        IReadOnlyModificationCommand second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return CanPlan(first)
            && CanAppend(CreateShape(first), second);
    }

    internal static bool CanAppend(DuckDBBulkInsertShape shape, IReadOnlyModificationCommand command)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(command);
        return shape.CanAppend(command);
    }

    public static bool TryCreate(
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        [NotNullWhen(true)] out DuckDBBulkInsertPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Count == 0 || !CanPlan(commands[0]))
        {
            plan = null;
            return false;
        }

        var firstCommand = commands[0];
        var shape = CreateShape(firstCommand);
        for (var i = 1; i < commands.Count; i++)
        {
            if (!CanAppend(shape, commands[i]))
            {
                plan = null;
                return false;
            }
        }

        plan = new DuckDBBulkInsertPlan(commands, shape);
        return true;
    }

    public static DuckDBBulkInsertPlan Create(IReadOnlyList<IReadOnlyModificationCommand> commands)
        => TryCreate(commands, out var plan)
            ? plan
            : throw new ArgumentException(
                "Bulk insert commands must be eligible inserts with matching tables, schemas, write columns, and read columns.",
                nameof(commands));

    internal static DuckDBBulkInsertPlan Create(
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        DuckDBBulkInsertShape shape)
        => new(commands, shape);

    internal static DuckDBBulkInsertShape CreateShape(IReadOnlyModificationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!CanPlan(command))
        {
            throw new ArgumentException("The command is not eligible for bulk insert.", nameof(command));
        }

        return new DuckDBBulkInsertShape(command, CreateMutationPlan(command));
    }

    internal static DuckDBStructMutationPlan? CreateMutationPlan(IReadOnlyModificationCommand command)
    {
        var modifications = command.ColumnModifications;
        var writeCount = 0;
        var hasStructField = false;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (modifications[i].IsWrite)
            {
                writeCount++;
                hasStructField |= DuckDBStructMutationPlan.IsStructField(modifications[i]);
            }
        }

        if (writeCount == 0 || !hasStructField)
        {
            return null;
        }

        var writes = new IColumnModification[writeCount];
        var writeIndex = 0;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (modifications[i].IsWrite)
            {
                writes[writeIndex++] = modifications[i];
            }
        }

        DuckDBStructMutationPlan.TryCreate(writes, DuckDBStructMutationMode.Insert, out var plan);
        return plan;
    }
}

/// <summary>
///     Immutable physical shape for one compatible bulk-insert run.
/// </summary>
internal sealed class DuckDBBulkInsertShape
{
    private readonly string[] _readColumnNames;
    private readonly string[] _writeColumnNames;

    internal DuckDBBulkInsertShape(
        IReadOnlyModificationCommand command,
        DuckDBStructMutationPlan? structMutationPlan)
    {
        TableName = command.TableName;
        Schema = command.Schema;
        _writeColumnNames = CollectColumnNames(command, DuckDBModificationColumnRole.Write);
        _readColumnNames = CollectColumnNames(command, DuckDBModificationColumnRole.Read);
        StructMutationPlan = structMutationPlan;
    }

    public string TableName { get; }

    public string? Schema { get; }

    public int WriteColumnCount => _writeColumnNames.Length;

    public int ReadColumnCount => _readColumnNames.Length;

    public DuckDBStructMutationPlan? StructMutationPlan { get; }

    public bool CanAppend(IReadOnlyModificationCommand command)
    {
        if (!DuckDBBulkInsertPlanner.CanPlan(command)
            || TableName != command.TableName
            || Schema != command.Schema
            || !ColumnNamesEqual(command, DuckDBModificationColumnRole.Read, _readColumnNames))
        {
            return false;
        }

        var candidateMutationPlan = DuckDBBulkInsertPlanner.CreateMutationPlan(command);
        return StructMutationPlan is null
            ? candidateMutationPlan is null
                && ColumnNamesEqual(command, DuckDBModificationColumnRole.Write, _writeColumnNames)
            : candidateMutationPlan is not null
              && StructMutationPlan.HasSamePhysicalShape(candidateMutationPlan);
    }

    private static string[] CollectColumnNames(
        IReadOnlyModificationCommand command,
        DuckDBModificationColumnRole role)
    {
        var count = DuckDBModificationCommandShape.CountColumns(command, role);
        var names = new string[count];
        var targetIndex = 0;
        var modifications = command.ColumnModifications;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (DuckDBModificationCommandShape.HasRole(modifications[i], role))
            {
                names[targetIndex++] = modifications[i].ColumnName;
            }
        }

        return names;
    }

    private static bool ColumnNamesEqual(
        IReadOnlyModificationCommand command,
        DuckDBModificationColumnRole role,
        IReadOnlyList<string> expected)
    {
        var matched = 0;
        var modifications = command.ColumnModifications;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (!DuckDBModificationCommandShape.HasRole(modifications[i], role))
            {
                continue;
            }

            if (matched >= expected.Count
                || !string.Equals(modifications[i].ColumnName, expected[matched], StringComparison.Ordinal))
            {
                return false;
            }

            matched++;
        }

        return matched == expected.Count;
    }
}

/// <summary>
///     Validated bulk-insert rendering plan. Command-owned column modifications are retained only for the
///     immediate synchronous render performed by the update SQL generator.
/// </summary>
internal sealed class DuckDBBulkInsertPlan
{
    private readonly IColumnModification[] _readColumns;
    private readonly DuckDBStructMutationPlan?[]? _structMutationPlans;
    private readonly IColumnModification[] _writeColumns;
    private readonly int _writeColumnCount;

    internal DuckDBBulkInsertPlan(
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        DuckDBBulkInsertShape shape)
    {
        _writeColumnCount = shape.WriteColumnCount;
        _writeColumns = new IColumnModification[commands.Count * _writeColumnCount];
        _readColumns = new IColumnModification[shape.ReadColumnCount];
        _structMutationPlans = shape.StructMutationPlan is null
            ? null
            : new DuckDBStructMutationPlan?[commands.Count];

        for (var rowIndex = 0; rowIndex < commands.Count; rowIndex++)
        {
            CopyColumns(
                commands[rowIndex],
                DuckDBModificationColumnRole.Write,
                _writeColumns,
                rowIndex * _writeColumnCount);
            if (_structMutationPlans is not null)
            {
                if (rowIndex == 0)
                {
                    _structMutationPlans[rowIndex] = shape.StructMutationPlan;
                }
                else
                {
                    DuckDBStructMutationPlan.TryCreate(
                        new ArraySegment<IColumnModification>(
                            _writeColumns,
                            rowIndex * _writeColumnCount,
                            _writeColumnCount),
                        DuckDBStructMutationMode.Insert,
                        out _structMutationPlans[rowIndex]);
                }
            }
        }

        CopyColumns(
            commands[0],
            DuckDBModificationColumnRole.Read,
            _readColumns,
            0);

        TableName = shape.TableName;
        Schema = shape.Schema;
        RowCount = commands.Count;
    }

    public string TableName { get; }

    public string? Schema { get; }

    public int RowCount { get; }

    public int WriteColumnCount => _writeColumnCount;

    public int ReadColumnCount => _readColumns.Length;

    public DuckDBStructMutationPlan? GetStructMutationPlan(int rowIndex)
        => _structMutationPlans?[rowIndex];

    public void CollectWriteColumns(int rowIndex, List<IColumnModification> target)
    {
        target.Clear();
        var start = rowIndex * _writeColumnCount;
        for (var i = 0; i < _writeColumnCount; i++)
        {
            target.Add(_writeColumns[start + i]);
        }
    }

    public void CollectReadColumns(List<IColumnModification> target)
    {
        target.Clear();
        target.AddRange(_readColumns);
    }

    private static void CopyColumns(
        IReadOnlyModificationCommand command,
        DuckDBModificationColumnRole role,
        IColumnModification[] target,
        int targetIndex)
    {
        var modifications = command.ColumnModifications;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (DuckDBModificationCommandShape.HasRole(modifications[i], role))
            {
                target[targetIndex++] = modifications[i];
            }
        }
    }
}