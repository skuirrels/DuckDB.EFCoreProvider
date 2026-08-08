using DuckDB.EFCoreProvider.Diagnostics;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.Logging;

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
    private const int MaxBatchCellCount = 10_000;

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
    private readonly List<DuckDBDualRoleUpdatePlan> _pendingBulkUpdatePlans = [];
    private readonly List<IReadOnlyModificationCommand> _pendingBulkDeleteCommands = [];
    private readonly bool _insertBatching;
    private readonly bool _updateBatching;
    private readonly bool _deleteBatching;
    private readonly bool _useUpdateFallback;
    private readonly IDuckDBEngineCapabilities _capabilities;
    private bool _requiresUpdateFallbackExecution;
    private bool _requiresConditionalForeignKeyExecution;
    private IReadOnlyModificationCommand? _commandBeingAdded;
    private DuckDBDualRoleUpdatePlan? _commandBeingAddedPlan;
    private DuckDBBulkInsertShape? _pendingBulkInsertShape;

    public DuckDBModificationCommandBatch(
        ModificationCommandBatchFactoryDependencies dependencies,
        int maxBatchSize,
        bool insertBatching,
        bool updateBatching,
        bool deleteBatching)
        : this(
            dependencies,
            maxBatchSize,
            insertBatching,
            updateBatching,
            deleteBatching,
            global::DuckDB.EFCoreProvider.Internal.DuckDBEngineCapabilities.Native)
    {
    }

    internal DuckDBModificationCommandBatch(
        ModificationCommandBatchFactoryDependencies dependencies,
        int maxBatchSize,
        bool insertBatching,
        bool updateBatching,
        bool deleteBatching,
        IDuckDBEngineCapabilities capabilities)
        : base(dependencies, maxBatchSize)
    {
        _insertBatching = insertBatching;
        _updateBatching = updateBatching;
        _deleteBatching = deleteBatching;
        _capabilities = capabilities;
        _useUpdateFallback =
            capabilities.SupportsReturning
            && !capabilities.SupportsReturningOnReferencedTableUpdates;
    }

    private new DuckDBUpdateSqlGenerator UpdateSqlGenerator
        => (DuckDBUpdateSqlGenerator)base.UpdateSqlGenerator;

    /// <inheritdoc />
    public override bool TryAddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        var dualRolePlan = DuckDBDualRoleUpdatePlanner.Create(modificationCommand, _capabilities);
        var requiresSpecialExecution = dualRolePlan.RequiresConditionalForeignKeyUpdate
            || RequiresUpdateFallbackExecution(modificationCommand, dualRolePlan);
        if (_requiresUpdateFallbackExecution
            || _requiresConditionalForeignKeyExecution
            || (requiresSpecialExecution && ModificationCommands.Count > 0))
        {
            return false;
        }

        // A pending insert/update run must be flushed before a command that cannot join it (a different
        // operation kind, a different table, or a different column shape) is added.
        if (_pendingBulkInsertCommands.Count > 0
            && !DuckDBBulkInsertPlanner.CanAppend(_pendingBulkInsertShape!, modificationCommand))
        {
            ApplyPendingBulkInsertCommands("ShapeChanged");
            _pendingBulkInsertCommands.Clear();
            _pendingBulkInsertShape = null;
        }

        if (_pendingBulkUpdateCommands.Count > 0
            && !DuckDBBulkUpdatePlanner.CanAppend(
                _pendingBulkUpdateCommands[0],
                _pendingBulkUpdatePlans[0],
                modificationCommand,
                dualRolePlan))
        {
            ApplyPendingBulkUpdateCommands("ShapeChanged");
            _pendingBulkUpdateCommands.Clear();
            _pendingBulkUpdatePlans.Clear();
        }

        if (_pendingBulkDeleteCommands.Count > 0
            && !DuckDBBulkDeletePlanner.CanAppend(_pendingBulkDeleteCommands[0], modificationCommand))
        {
            ApplyPendingBulkDeleteCommands("ShapeChanged");
            _pendingBulkDeleteCommands.Clear();
        }

        _commandBeingAdded = modificationCommand;
        _commandBeingAddedPlan = dualRolePlan;
        try
        {
            return base.TryAddCommand(modificationCommand);
        }
        finally
        {
            _commandBeingAdded = null;
            _commandBeingAddedPlan = null;
        }
    }

    /// <inheritdoc />
    protected override void AddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        var dualRolePlan = ReferenceEquals(_commandBeingAdded, modificationCommand)
                           && _commandBeingAddedPlan is { } classifiedPlan
            ? classifiedPlan
            : DuckDBDualRoleUpdatePlanner.Create(modificationCommand, _capabilities);
        var requiresUpdateFallback = RequiresUpdateFallbackExecution(modificationCommand, dualRolePlan);
        _requiresConditionalForeignKeyExecution = dualRolePlan.RequiresConditionalForeignKeyUpdate;

        // Buffer the eligible insert/update and add its parameters now; the merged SQL is generated when the
        // run is flushed (on the next non-mergeable command or on Complete).
        if (_insertBatching && DuckDBBulkInsertPlanner.CanPlan(modificationCommand))
        {
            _pendingBulkInsertShape ??= DuckDBBulkInsertPlanner.CreateShape(modificationCommand);
            _pendingBulkInsertCommands.Add(modificationCommand);
            AddParameters(modificationCommand);
        }
        else if (CanUseBulkUpdate(modificationCommand, dualRolePlan))
        {
            _pendingBulkUpdateCommands.Add(modificationCommand);
            _pendingBulkUpdatePlans.Add(dualRolePlan);
            AddParameters(modificationCommand);
        }
        else if (_deleteBatching && DuckDBBulkDeletePlanner.CanPlan(modificationCommand))
        {
            _pendingBulkDeleteCommands.Add(modificationCommand);
            AddParameters(modificationCommand);
        }
        else
        {
            _requiresUpdateFallbackExecution = requiresUpdateFallback;
            base.AddCommand(modificationCommand);
        }
    }

    private bool RequiresUpdateFallbackExecution(
        IReadOnlyModificationCommand command,
        DuckDBDualRoleUpdatePlan dualRolePlan)
        => _useUpdateFallback
            && !dualRolePlan.RequiresConditionalForeignKeyUpdate
            && !CanUseBulkUpdate(command, dualRolePlan)
            && DuckDBUpdateFallbackPlanner.CanPlan(command);

    private bool CanUseBulkUpdate(
        IReadOnlyModificationCommand command,
        DuckDBDualRoleUpdatePlan dualRolePlan)
        => _updateBatching
            && !dualRolePlan.HasChangedForeignKeyWrites
            && DuckDBBulkUpdatePlanner.CanPlan(command, dualRolePlan);

    /// <inheritdoc />
    protected override void RollbackLastCommand(IReadOnlyModificationCommand modificationCommand)
    {
        if (_pendingBulkInsertCommands.Count > 0)
        {
            _pendingBulkInsertCommands.RemoveAt(_pendingBulkInsertCommands.Count - 1);
            if (_pendingBulkInsertCommands.Count == 0)
            {
                _pendingBulkInsertShape = null;
            }
        }
        else if (_pendingBulkUpdateCommands.Count > 0)
        {
            _pendingBulkUpdateCommands.RemoveAt(_pendingBulkUpdateCommands.Count - 1);
            _pendingBulkUpdatePlans.RemoveAt(_pendingBulkUpdatePlans.Count - 1);
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
            LogBatchDecision("Mixed", PendingBatchRowCount(), PendingBatchColumnCount(), "ParameterLimit");
            return false;
        }

        var pendingCells = PendingBatchCellCount();
        if (pendingCells > MaxBatchCellCount)
        {
            LogBatchDecision("Mixed", PendingBatchRowCount(), PendingBatchColumnCount(), "CellLimit");
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
                DuckDBModificationCommandShape.CountColumns(
                    _pendingBulkDeleteCommands[0],
                    DuckDBModificationColumnRole.Condition));
        }

        if (length >= MaxScriptLength)
        {
            LogBatchDecision("Mixed", PendingBatchRowCount(), PendingBatchColumnCount(), "ScriptLength");
            return false;
        }

        return true;
    }

    private static int EstimateMergedStatementLength(int rowCount, int columnCount)
        => (columnCount * EstimatedBytesPerColumnName)
           + EstimatedFixedStatementBytes
           + (rowCount * columnCount * EstimatedBytesPerValueCell);

    /// <inheritdoc />
    public override void Complete(bool moreBatchesExpected)
    {
        ApplyPendingBulkInsertCommands("Completed");
        ApplyPendingBulkUpdateCommands("Completed");
        ApplyPendingBulkDeleteCommands("Completed");

        base.Complete(moreBatchesExpected);
    }

    /// <inheritdoc />
    public override void Execute(IRelationalConnection connection)
    {
        if (_requiresUpdateFallbackExecution)
        {
            DuckDBUpdateFallbackExecutor.Execute(
                Dependencies,
                UpdateSqlGenerator,
                connection,
                StoreCommand,
                ModificationCommands,
                _capabilities);
            return;
        }

        try
        {
            base.Execute(connection);
        }
        catch (DbUpdateException exception) when (
            _requiresConditionalForeignKeyExecution
            && DuckDBUpdateFallbackExecutor.IsInboundReferenceConstraintFailure(exception))
        {
            throw DuckDBUpdateFallbackExecutor.CreateConditionalForeignKeyException(
                exception,
                ModificationCommands,
                _capabilities);
        }
    }

    /// <inheritdoc />
    public override async Task ExecuteAsync(
        IRelationalConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (_requiresUpdateFallbackExecution)
        {
            await DuckDBUpdateFallbackExecutor
                .ExecuteAsync(
                    Dependencies,
                    UpdateSqlGenerator,
                    connection,
                    StoreCommand,
                    ModificationCommands,
                    _capabilities,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        try
        {
            await base.ExecuteAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (
            _requiresConditionalForeignKeyExecution
            && DuckDBUpdateFallbackExecutor.IsInboundReferenceConstraintFailure(exception))
        {
            throw DuckDBUpdateFallbackExecutor.CreateConditionalForeignKeyException(
                exception,
                ModificationCommands,
                _capabilities);
        }
    }

    private void ApplyPendingBulkInsertCommands(string reason)
    {
        if (_pendingBulkInsertCommands.Count == 0)
        {
            return;
        }

        var wasCommandTextEmpty = IsCommandTextEmpty;

        var plan = DuckDBBulkInsertPlanner.Create(_pendingBulkInsertCommands, _pendingBulkInsertShape!);
        var resultSetMapping = UpdateSqlGenerator.AppendBulkInsertOperation(
            SqlBuilder,
            plan,
            out var requiresTransaction);
        LogBatchDecision(
            "Insert",
            _pendingBulkInsertCommands.Count,
            plan.WriteColumnCount,
            reason);

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

    private void ApplyPendingBulkUpdateCommands(string reason)
    {
        if (_pendingBulkUpdateCommands.Count == 0)
        {
            return;
        }

        var wasCommandTextEmpty = IsCommandTextEmpty;
        var plan = DuckDBBulkUpdatePlanner.Create(_pendingBulkUpdateCommands, _pendingBulkUpdatePlans);

        var resultSetMapping = UpdateSqlGenerator.AppendBulkUpdateOperation(
            SqlBuilder,
            plan,
            out var requiresTransaction);
        LogBatchDecision("Update", plan.RowCount, plan.WriteColumnCount + plan.KeyColumnCount, reason);

        SetRequiresTransaction(!wasCommandTextEmpty || requiresTransaction);

        // Eligible updates read nothing back, so every command maps to a no-results statement.
        for (var i = 0; i < _pendingBulkUpdateCommands.Count; i++)
        {
            ResultSetMappings.Add(resultSetMapping);
        }
    }

    private void ApplyPendingBulkDeleteCommands(string reason)
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
        LogBatchDecision(
            "Delete",
            _pendingBulkDeleteCommands.Count,
            DuckDBModificationCommandShape.CountColumns(
                _pendingBulkDeleteCommands[0],
                DuckDBModificationColumnRole.Condition),
            reason);

        SetRequiresTransaction(!wasCommandTextEmpty || requiresTransaction);

        // Eligible deletes read nothing back, so every command maps to a no-results statement.
        for (var i = 0; i < _pendingBulkDeleteCommands.Count; i++)
        {
            ResultSetMappings.Add(resultSetMapping);
        }
    }

    private int PendingBatchCellCount()
    {
        var cells = 0;
        if (_pendingBulkInsertCommands.Count > 0)
        {
            cells += _pendingBulkInsertCommands.Count * _pendingBulkInsertShape!.WriteColumnCount;
        }

        if (_pendingBulkUpdateCommands.Count > 0)
        {
            cells += _pendingBulkUpdateCommands.Count
                     * (_pendingBulkUpdatePlans[0].WriteOperations.Count
                        + DuckDBModificationCommandShape.CountColumns(
                            _pendingBulkUpdateCommands[0],
                            DuckDBModificationColumnRole.Condition));
        }

        if (_pendingBulkDeleteCommands.Count > 0)
        {
            cells += _pendingBulkDeleteCommands.Count * DuckDBModificationCommandShape.CountColumns(
                _pendingBulkDeleteCommands[0],
                DuckDBModificationColumnRole.Condition);
        }

        return cells;
    }

    private int PendingBatchColumnCount()
    {
        if (_pendingBulkInsertCommands.Count > 0)
        {
            return _pendingBulkInsertShape!.WriteColumnCount;
        }

        if (_pendingBulkUpdateCommands.Count > 0)
        {
            return _pendingBulkUpdatePlans[0].WriteOperations.Count
                   + DuckDBModificationCommandShape.CountColumns(
                       _pendingBulkUpdateCommands[0],
                       DuckDBModificationColumnRole.Condition);
        }

        return _pendingBulkDeleteCommands.Count > 0
            ? DuckDBModificationCommandShape.CountColumns(
                _pendingBulkDeleteCommands[0],
                DuckDBModificationColumnRole.Condition)
            : 0;
    }

    private int PendingBatchRowCount()
        => _pendingBulkInsertCommands.Count
           + _pendingBulkUpdateCommands.Count
           + _pendingBulkDeleteCommands.Count;

    private void LogBatchDecision(string operation, int rowCount, int columnCount, string reason)
    {
        var logger = Dependencies.UpdateLogger.Logger;
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        logger.LogDebug(
            DuckDBEventId.SaveChangesBatch,
            "DuckDB SaveChanges {Operation} batch: rows={RowCount}, columns={ColumnCount}, cells={CellCount}, "
            + "parameters={ParameterCount}, sql_characters={SqlCharacters}, reason={Reason}.",
            operation,
            rowCount,
            columnCount,
            rowCount * columnCount,
            ParameterValues.Count,
            SqlBuilder.Length,
            reason);
    }
}