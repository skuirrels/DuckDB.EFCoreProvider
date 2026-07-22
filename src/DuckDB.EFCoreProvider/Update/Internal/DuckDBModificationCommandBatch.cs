using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Update;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     <para>
///         Collapses runs of consecutive insert commands that target the same table with the same written
///         and returned columns into a single multi-row <c>INSERT ... VALUES (..),(..),.. [RETURNING ..]</c>
///         statement. On DuckDB's columnar engine this is roughly an order of magnitude faster than issuing
///         one insert statement per row.
///     </para>
///     <para>
///         Store-generated values are correlated back to each command positionally, which is safe because
///         DuckDB returns <c>RETURNING</c> rows in the same order as the supplied <c>VALUES</c> tuples.
///         Updates, deletes, and inserts that cannot be merged fall back to the standard per-command path.
///     </para>
/// </remarks>
public class DuckDBModificationCommandBatch : AffectedCountModificationCommandBatch
{
    /// <summary>
    ///     Upper bound on the number of bind parameters in a single batch. DuckDB tolerates a large number of
    ///     parameters, but parameter binding cost grows with the count, so the batch is kept within a sane
    ///     ceiling; <c>MaxBatchSize</c> is the primary control.
    /// </summary>
    private const int MaxParameterCount = 100_000;

    /// <summary>
    ///     Upper bound on the generated script length, mirroring DuckDB's practical statement-size limits.
    /// </summary>
    private const int MaxScriptLength = 100_000_000;

    // Rough per-batch script-size estimate used by IsValid before the merged SQL is generated. These are
    // deliberately generous over-estimates of the characters a merged statement will add, so the script-length
    // guard trips before the real SQL could exceed MaxScriptLength.
    private const int EstimatedBytesPerColumnName = 128; // delimited column name in the header / SET list
    private const int EstimatedFixedStatementBytes = 256 + 300; // keywords, parentheses, table name, clauses
    private const int EstimatedBytesPerValueCell = 6; // one parameter placeholder plus separators

    private readonly List<IReadOnlyModificationCommand> _pendingBulkInsertCommands = [];
    private readonly List<IReadOnlyModificationCommand> _pendingBulkUpdateCommands = [];
    private readonly List<IReadOnlyModificationCommand> _pendingBulkDeleteCommands = [];
    private readonly bool _insertBatching;
    private readonly bool _updateBatching;
    private readonly bool _deleteBatching;

    public DuckDBModificationCommandBatch(
        ModificationCommandBatchFactoryDependencies dependencies,
        int maxBatchSize,
        bool insertBatching,
        bool updateBatching,
        bool deleteBatching)
        : base(dependencies, maxBatchSize)
    {
        _insertBatching = insertBatching;
        _updateBatching = updateBatching;
        _deleteBatching = deleteBatching;
    }

    private new DuckDBUpdateSqlGenerator UpdateSqlGenerator
        => (DuckDBUpdateSqlGenerator)base.UpdateSqlGenerator;

    /// <inheritdoc />
    public override bool TryAddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        // A pending insert/update run must be flushed before a command that cannot join it (a different
        // operation kind, a different table, or a different column shape) is added.
        if (_pendingBulkInsertCommands.Count > 0
            && !CanBeInsertedInSameStatement(_pendingBulkInsertCommands[0], modificationCommand))
        {
            ApplyPendingBulkInsertCommands();
            _pendingBulkInsertCommands.Clear();
        }

        if (_pendingBulkUpdateCommands.Count > 0
            && !DuckDBBulkUpdatePlanner.CanAppend(_pendingBulkUpdateCommands[0], modificationCommand))
        {
            ApplyPendingBulkUpdateCommands();
            _pendingBulkUpdateCommands.Clear();
        }

        if (_pendingBulkDeleteCommands.Count > 0
            && !CanBeDeletedInSameStatement(_pendingBulkDeleteCommands[0], modificationCommand))
        {
            ApplyPendingBulkDeleteCommands();
            _pendingBulkDeleteCommands.Clear();
        }

        return base.TryAddCommand(modificationCommand);
    }

    /// <inheritdoc />
    protected override void AddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        // Buffer the eligible insert/update and add its parameters now; the merged SQL is generated when the
        // run is flushed (on the next non-mergeable command or on Complete).
        if (CanBulkInsert(modificationCommand))
        {
            _pendingBulkInsertCommands.Add(modificationCommand);
            AddParameters(modificationCommand);
        }
        else if (_updateBatching && DuckDBBulkUpdatePlanner.CanPlan(modificationCommand))
        {
            _pendingBulkUpdateCommands.Add(modificationCommand);
            AddParameters(modificationCommand);
        }
        else if (CanBulkDelete(modificationCommand))
        {
            _pendingBulkDeleteCommands.Add(modificationCommand);
            AddParameters(modificationCommand);
        }
        else
        {
            base.AddCommand(modificationCommand);
        }
    }

    /// <inheritdoc />
    protected override void RollbackLastCommand(IReadOnlyModificationCommand modificationCommand)
    {
        if (_pendingBulkInsertCommands.Count > 0)
        {
            _pendingBulkInsertCommands.RemoveAt(_pendingBulkInsertCommands.Count - 1);
        }
        else if (_pendingBulkUpdateCommands.Count > 0)
        {
            _pendingBulkUpdateCommands.RemoveAt(_pendingBulkUpdateCommands.Count - 1);
        }
        else if (_pendingBulkDeleteCommands.Count > 0)
        {
            _pendingBulkDeleteCommands.RemoveAt(_pendingBulkDeleteCommands.Count - 1);
        }

        base.RollbackLastCommand(modificationCommand);
    }

    /// <inheritdoc />
    protected override bool IsValid()
    {
        if (ParameterValues.Count > MaxParameterCount)
        {
            return false;
        }

        var length = SqlBuilder.Length;

        // Account for the merged SQL that the pending runs will generate but that is not in SqlBuilder yet.
        if (_pendingBulkInsertCommands.Count > 0)
        {
            length += EstimateMergedStatementLength(
                _pendingBulkInsertCommands.Count,
                _pendingBulkInsertCommands[0].ColumnModifications.Count);
        }

        if (_pendingBulkUpdateCommands.Count > 0)
        {
            length += EstimateMergedStatementLength(
                _pendingBulkUpdateCommands.Count,
                _pendingBulkUpdateCommands[0].ColumnModifications.Count);
        }

        if (_pendingBulkDeleteCommands.Count > 0)
        {
            length += EstimateMergedStatementLength(
                _pendingBulkDeleteCommands.Count,
                CountConditions(_pendingBulkDeleteCommands[0]));
        }

        return length < MaxScriptLength;
    }

    private static int EstimateMergedStatementLength(int rowCount, int columnCount)
        => (columnCount * EstimatedBytesPerColumnName)
           + EstimatedFixedStatementBytes
           + (rowCount * columnCount * EstimatedBytesPerValueCell);

    /// <inheritdoc />
    public override void Complete(bool moreBatchesExpected)
    {
        ApplyPendingBulkInsertCommands();
        ApplyPendingBulkUpdateCommands();
        ApplyPendingBulkDeleteCommands();

        base.Complete(moreBatchesExpected);
    }

    private bool CanBulkInsert(IReadOnlyModificationCommand command)
        => _insertBatching
           && command.EntityState == EntityState.Added
           && command.StoreStoredProcedure is null
           && HasWrite(command);

    private bool CanBulkDelete(IReadOnlyModificationCommand command)
        => _deleteBatching
           && command.EntityState == EntityState.Deleted
           && command.StoreStoredProcedure is null
           // The WHERE clause is the primary key only (no concurrency tokens), so the rows can be matched by
           // key without per-row affected-count verification.
           && AllConditionsAreKey(command)
           && HasCondition(command);

    private bool CanBeInsertedInSameStatement(
        IReadOnlyModificationCommand first,
        IReadOnlyModificationCommand second)
        => CanBulkInsert(second)
           && first.TableName == second.TableName
           && first.Schema == second.Schema
           && ColumnNamesEqual(first, second, ColumnRole.Write)
           && ColumnNamesEqual(first, second, ColumnRole.Read);

    private bool CanBeDeletedInSameStatement(
        IReadOnlyModificationCommand first,
        IReadOnlyModificationCommand second)
        => CanBulkDelete(second)
           && first.TableName == second.TableName
           && first.Schema == second.Schema
           && ColumnNamesEqual(first, second, ColumnRole.Condition);

    private enum ColumnRole
    {
        Write,
        Read,
        Condition
    }

    private static bool HasRole(IColumnModification modification, ColumnRole role)
        => role switch
        {
            ColumnRole.Write => modification.IsWrite,
            ColumnRole.Read => modification.IsRead,
            _ => modification.IsCondition
        };

    // command.ColumnModifications.Any(o => o.IsWrite)
    private static bool HasWrite(IReadOnlyModificationCommand command)
    {
        var modifications = command.ColumnModifications;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (modifications[i].IsWrite)
            {
                return true;
            }
        }

        return false;
    }

    // command.ColumnModifications.Any(o => o.IsCondition)
    private static bool HasCondition(IReadOnlyModificationCommand command)
    {
        var modifications = command.ColumnModifications;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (modifications[i].IsCondition)
            {
                return true;
            }
        }

        return false;
    }

    // command.ColumnModifications.Count(o => o.IsCondition)
    private static int CountConditions(IReadOnlyModificationCommand command)
    {
        var modifications = command.ColumnModifications;
        var count = 0;
        for (var i = 0; i < modifications.Count; i++)
        {
            if (modifications[i].IsCondition)
            {
                count++;
            }
        }

        return count;
    }

    // command.ColumnModifications.Where(o => o.IsCondition).All(o => o.IsKey)
    private static bool AllConditionsAreKey(IReadOnlyModificationCommand command)
    {
        var modifications = command.ColumnModifications;
        for (var i = 0; i < modifications.Count; i++)
        {
            var modification = modifications[i];
            if (modification.IsCondition && !modification.IsKey)
            {
                return false;
            }
        }

        return true;
    }

    // Equivalent to comparing first.ColumnModifications.Where(role).Select(o => o.ColumnName) with the same
    // projection over second, in order (SequenceEqual) — without allocating the intermediate LINQ iterators.
    private static bool ColumnNamesEqual(
        IReadOnlyModificationCommand first,
        IReadOnlyModificationCommand second,
        ColumnRole role)
    {
        var firstModifications = first.ColumnModifications;
        var secondModifications = second.ColumnModifications;
        int i = 0, j = 0;

        while (true)
        {
            while (i < firstModifications.Count && !HasRole(firstModifications[i], role))
            {
                i++;
            }

            while (j < secondModifications.Count && !HasRole(secondModifications[j], role))
            {
                j++;
            }

            var firstDone = i >= firstModifications.Count;
            var secondDone = j >= secondModifications.Count;
            if (firstDone || secondDone)
            {
                // SequenceEqual is true only when both sequences end at the same point.
                return firstDone && secondDone;
            }

            if (firstModifications[i].ColumnName != secondModifications[j].ColumnName)
            {
                return false;
            }

            i++;
            j++;
        }
    }

    private void ApplyPendingBulkInsertCommands()
    {
        if (_pendingBulkInsertCommands.Count == 0)
        {
            return;
        }

        var wasCommandTextEmpty = IsCommandTextEmpty;

        var resultSetMapping = UpdateSqlGenerator.AppendBulkInsertOperation(
            SqlBuilder,
            _pendingBulkInsertCommands,
            out var requiresTransaction);

        SetRequiresTransaction(!wasCommandTextEmpty || requiresTransaction);

        for (var i = 0; i < _pendingBulkInsertCommands.Count; i++)
        {
            ResultSetMappings.Add(resultSetMapping);
        }

        // When the merged statement returns rows, each command maps to one row in insertion order; mark the
        // final command as the last row in the result set so consumption stops at the right place.
        if (resultSetMapping.HasFlag(ResultSetMapping.HasResultRow))
        {
            var lastIndex = ResultSetMappings.Count - 1;
            ResultSetMappings[lastIndex] =
                (ResultSetMappings[lastIndex] & ~ResultSetMapping.NotLastInResultSet)
                | ResultSetMapping.LastInResultSet;
        }
    }

    private void ApplyPendingBulkUpdateCommands()
    {
        if (_pendingBulkUpdateCommands.Count == 0)
        {
            return;
        }

        var wasCommandTextEmpty = IsCommandTextEmpty;

        var resultSetMapping = UpdateSqlGenerator.AppendBulkUpdateOperation(
            SqlBuilder,
            _pendingBulkUpdateCommands,
            out var requiresTransaction);

        SetRequiresTransaction(!wasCommandTextEmpty || requiresTransaction);

        // Eligible updates read nothing back, so every command maps to a no-results statement.
        for (var i = 0; i < _pendingBulkUpdateCommands.Count; i++)
        {
            ResultSetMappings.Add(resultSetMapping);
        }
    }

    private void ApplyPendingBulkDeleteCommands()
    {
        if (_pendingBulkDeleteCommands.Count == 0)
        {
            return;
        }

        var wasCommandTextEmpty = IsCommandTextEmpty;

        var resultSetMapping = UpdateSqlGenerator.AppendBulkDeleteOperation(
            SqlBuilder,
            _pendingBulkDeleteCommands,
            out var requiresTransaction);

        SetRequiresTransaction(!wasCommandTextEmpty || requiresTransaction);

        // Eligible deletes read nothing back, so every command maps to a no-results statement.
        for (var i = 0; i < _pendingBulkDeleteCommands.Count; i++)
        {
            ResultSetMappings.Add(resultSetMapping);
        }
    }
}