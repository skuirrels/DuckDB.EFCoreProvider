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
                DuckDBModificationColumnRole.Read);
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
        for (var i = 1; i < commands.Count; i++)
        {
            if (!CanAppend(firstCommand, commands[i]))
            {
                plan = null;
                return false;
            }
        }

        plan = new DuckDBBulkInsertPlan(
            commands,
            DuckDBModificationCommandShape.CountColumns(
                firstCommand,
                DuckDBModificationColumnRole.Write),
            DuckDBModificationCommandShape.CountColumns(
                firstCommand,
                DuckDBModificationColumnRole.Read));
        return true;
    }

    public static DuckDBBulkInsertPlan Create(IReadOnlyList<IReadOnlyModificationCommand> commands)
        => TryCreate(commands, out var plan)
            ? plan
            : throw new ArgumentException(
                "Bulk insert commands must be eligible inserts with matching tables, schemas, write columns, and read columns.",
                nameof(commands));
}

/// <summary>
///     Immutable snapshot of a validated bulk-insert command run.
/// </summary>
internal sealed class DuckDBBulkInsertPlan
{
    private readonly DuckDBColumnModificationSnapshot[] _readColumns;
    private readonly DuckDBColumnModificationSnapshot[] _writeColumns;
    private readonly int _writeColumnCount;

    internal DuckDBBulkInsertPlan(
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        int writeColumnCount,
        int readColumnCount)
    {
        _writeColumnCount = writeColumnCount;
        _writeColumns = new DuckDBColumnModificationSnapshot[commands.Count * writeColumnCount];
        _readColumns = new DuckDBColumnModificationSnapshot[readColumnCount];

        for (var rowIndex = 0; rowIndex < commands.Count; rowIndex++)
        {
            CopyColumns(
                commands[rowIndex],
                DuckDBModificationColumnRole.Write,
                _writeColumns,
                rowIndex * writeColumnCount);
        }

        CopyColumns(
            commands[0],
            DuckDBModificationColumnRole.Read,
            _readColumns,
            0);

        TableName = commands[0].TableName;
        Schema = commands[0].Schema;
        RowCount = commands.Count;
    }

    public string TableName { get; }

    public string? Schema { get; }

    public int RowCount { get; }

    public int WriteColumnCount => _writeColumnCount;

    public int ReadColumnCount => _readColumns.Length;

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
        DuckDBColumnModificationSnapshot[] target,
        int targetIndex)
    {
        var modifications = command.ColumnModifications;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (DuckDBModificationCommandShape.HasRole(modifications[i], role))
            {
                target[targetIndex++] = new DuckDBColumnModificationSnapshot(modifications[i]);
            }
        }
    }
}

/// <summary>
///     Captures the rendering state of an EF column modification without retaining its mutable values.
/// </summary>
internal sealed class DuckDBColumnModificationSnapshot : IColumnModification
{
    private const string ImmutableMessage = "Bulk-insert column snapshots cannot be modified.";

    public DuckDBColumnModificationSnapshot(IColumnModification source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Entry = source.Entry;
        Property = source.Property;
        Column = source.Column;
        TypeMapping = source.TypeMapping;
        IsNullable = source.IsNullable;
        IsRead = source.IsRead;
        IsWrite = source.IsWrite;
        IsCondition = source.IsCondition;
        IsKey = source.IsKey;
        UseOriginalValueParameter = source.UseOriginalValueParameter;
        UseCurrentValueParameter = source.UseCurrentValueParameter;
        UseOriginalValue = source.UseOriginalValue;
        UseCurrentValue = source.UseCurrentValue;
        UseParameter = source.UseParameter;
        ParameterName = source.ParameterName;
        OriginalParameterName = source.OriginalParameterName;
        ColumnName = source.ColumnName;
        ColumnType = source.ColumnType;
        OriginalValue = source.OriginalValue;
        Value = source.Value;
        JsonPath = source.JsonPath;
    }

    public IUpdateEntry? Entry { get; }

    public IProperty? Property { get; }

    public IColumnBase? Column { get; }

    public RelationalTypeMapping? TypeMapping { get; }

    public bool? IsNullable { get; }

    public bool IsRead { get; }

    public bool IsWrite { get; }

    public bool IsCondition { get; }

    public bool IsKey { get; }

    public bool UseOriginalValueParameter { get; }

    public bool UseCurrentValueParameter { get; }

    public bool UseOriginalValue { get; }

    public bool UseCurrentValue { get; }

    public bool UseParameter { get; }

    public string? ParameterName { get; }

    public string? OriginalParameterName { get; }

    public string ColumnName { get; }

    public string? ColumnType { get; }

    public object? OriginalValue { get; }

    public object? Value { get; }

    public string? JsonPath { get; }

    bool IColumnModification.IsRead
    {
        get => IsRead;
        set => throw new NotSupportedException(ImmutableMessage);
    }

    bool IColumnModification.IsWrite
    {
        get => IsWrite;
        set => throw new NotSupportedException(ImmutableMessage);
    }

    bool IColumnModification.IsCondition
    {
        get => IsCondition;
        set => throw new NotSupportedException(ImmutableMessage);
    }

    bool IColumnModification.IsKey
    {
        get => IsKey;
        set => throw new NotSupportedException(ImmutableMessage);
    }

    object? IColumnModification.OriginalValue
    {
        get => OriginalValue;
        set => throw new NotSupportedException(ImmutableMessage);
    }

    object? IColumnModification.Value
    {
        get => Value;
        set => throw new NotSupportedException(ImmutableMessage);
    }

    void IColumnModification.AddSharedColumnModification(IColumnModification modification)
        => throw new NotSupportedException(ImmutableMessage);

    void IColumnModification.ResetParameterNames()
        => throw new NotSupportedException(ImmutableMessage);
}