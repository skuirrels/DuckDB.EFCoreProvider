using DuckDB.EFCoreProvider.Infrastructure.Internal;
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
    private readonly bool _useUpdateFallback =
        capabilities.SupportsReturning
        && !capabilities.SupportsReturningOnReferencedTableUpdates;
    private bool _requiresUpdateFallbackExecution;

    private new DuckDBUpdateSqlGenerator UpdateSqlGenerator
        => (DuckDBUpdateSqlGenerator)base.UpdateSqlGenerator;

    protected override void AddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        _requiresUpdateFallbackExecution =
            _useUpdateFallback
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
                ModificationCommands);
            return;
        }

        base.Execute(connection);
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
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await base.ExecuteAsync(connection, cancellationToken).ConfigureAwait(false);
    }
}