using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     Executes one modification without a result-set reader and validates DuckDB.NET's affected-row count.
/// </summary>
internal sealed class DuckDBNonReturningModificationCommandBatch(
    ModificationCommandBatchFactoryDependencies dependencies)
    : SingularModificationCommandBatch(dependencies)
{
    public override void Execute(IRelationalConnection connection)
    {
        if (StoreCommand is null)
        {
            throw new InvalidOperationException(RelationalStrings.ModificationCommandBatchNotComplete);
        }

        try
        {
            var affectedRows = StoreCommand.RelationalCommand.ExecuteNonQuery(CreateParameterObject(connection));
            DuckDBAffectedRowsValidator.Validate(Dependencies, ModificationCommands, affectedRows);
        }
        catch (Exception exception) when (exception is not DbUpdateException and not OperationCanceledException)
        {
            throw new DbUpdateException(
                RelationalStrings.UpdateStoreException,
                exception,
                ModificationCommands.SelectMany(command => command.Entries).ToList());
        }
    }

    public override async Task ExecuteAsync(
        IRelationalConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (StoreCommand is null)
        {
            throw new InvalidOperationException(RelationalStrings.ModificationCommandBatchNotComplete);
        }

        try
        {
            var affectedRows = await StoreCommand.RelationalCommand
                .ExecuteNonQueryAsync(CreateParameterObject(connection), cancellationToken)
                .ConfigureAwait(false);
            await DuckDBAffectedRowsValidator
                .ValidateAsync(Dependencies, ModificationCommands, affectedRows, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not DbUpdateException and not OperationCanceledException)
        {
            throw new DbUpdateException(
                RelationalStrings.UpdateStoreException,
                exception,
                ModificationCommands.SelectMany(command => command.Entries).ToList());
        }
    }

    private RelationalCommandParameterObject CreateParameterObject(IRelationalConnection connection)
        => new(
            connection,
            StoreCommand!.ParameterValues,
            null,
            Dependencies.CurrentContext.Context,
            Dependencies.Logger,
            CommandSource.SaveChanges);

    protected override void Consume(RelationalDataReader reader)
        => throw new NotSupportedException("Non-returning modification batches do not consume result sets.");

    protected override Task ConsumeAsync(RelationalDataReader reader, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Non-returning modification batches do not consume result sets.");
}