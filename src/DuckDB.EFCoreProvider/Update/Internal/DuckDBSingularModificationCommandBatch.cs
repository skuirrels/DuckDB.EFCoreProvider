using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     Preserves EF Core's singular-command semantics while selecting the referenced-table update fallback
///     only when the configured engine capabilities require it.
/// </summary>
internal sealed class DuckDBSingularModificationCommandBatch(
    ModificationCommandBatchFactoryDependencies dependencies,
    IDuckDBEngineCapabilities capabilities)
    : SingularModificationCommandBatch(dependencies)
{
    private readonly IDuckDBEngineCapabilities _capabilities = capabilities;
    private readonly bool _useUpdateFallback =
        capabilities.SupportsReturning
        && !capabilities.SupportsReturningOnReferencedTableUpdates;
    private bool _requiresUpdateFallbackExecution;
    private bool _requiresConditionalForeignKeyExecution;

    private new DuckDBUpdateSqlGenerator UpdateSqlGenerator
        => (DuckDBUpdateSqlGenerator)base.UpdateSqlGenerator;

    protected override void AddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        var dualRolePlan = DuckDBDualRoleUpdatePlanner.Create(modificationCommand, _capabilities);
        _requiresConditionalForeignKeyExecution = dualRolePlan.RequiresConditionalForeignKeyUpdate;
        _requiresUpdateFallbackExecution =
            _useUpdateFallback
            && !dualRolePlan.RequiresConditionalForeignKeyUpdate
            && DuckDBUpdateFallbackPlanner.CanPlan(modificationCommand);
        base.AddCommand(modificationCommand);
    }

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
}