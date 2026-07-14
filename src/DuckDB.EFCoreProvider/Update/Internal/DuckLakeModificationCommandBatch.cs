using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     Executes one DuckLake modification without a reader and validates DuckDB.NET's affected-row count.
/// </summary>
internal sealed class DuckLakeModificationCommandBatch(ModificationCommandBatchFactoryDependencies dependencies)
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
            ValidateAffectedRows(affectedRows);
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
            await ValidateAffectedRowsAsync(affectedRows, cancellationToken).ConfigureAwait(false);
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

    private void ValidateAffectedRows(int affectedRows)
    {
        const int expectedRows = 1;
        if (affectedRows == expectedRows)
        {
            return;
        }

        var entries = GetEntries();
        var exception = CreateConcurrencyException(expectedRows, affectedRows, entries);

        if (!Dependencies.UpdateLogger.OptimisticConcurrencyException(
                Dependencies.CurrentContext.Context,
                entries,
                exception,
                CreateConcurrencyExceptionEventData).IsSuppressed)
        {
            throw exception;
        }
    }

    private async Task ValidateAffectedRowsAsync(int affectedRows, CancellationToken cancellationToken)
    {
        const int expectedRows = 1;
        if (affectedRows == expectedRows)
        {
            return;
        }

        var entries = GetEntries();
        var exception = CreateConcurrencyException(expectedRows, affectedRows, entries);

        if (!(await Dependencies.UpdateLogger.OptimisticConcurrencyExceptionAsync(
                    Dependencies.CurrentContext.Context,
                    entries,
                    exception,
                    CreateConcurrencyExceptionEventData,
                    cancellationToken)
                .ConfigureAwait(false)).IsSuppressed)
        {
            throw exception;
        }
    }

    private IReadOnlyList<IUpdateEntry> GetEntries()
        => ModificationCommands.SelectMany(command => command.Entries).ToList();

    private static DbUpdateConcurrencyException CreateConcurrencyException(
        int expectedRows,
        int affectedRows,
        IReadOnlyList<IUpdateEntry> entries)
        => new(
            RelationalStrings.UpdateConcurrencyException(expectedRows, affectedRows),
            entries);

    private static ConcurrencyExceptionEventData CreateConcurrencyExceptionEventData(
        DbContext context,
        DbUpdateConcurrencyException exception,
        IReadOnlyList<IUpdateEntry> entries,
        EventDefinition<Exception> definition)
        => new(
            definition,
            static (eventDefinition, eventData)
                => ((EventDefinition<Exception>)eventDefinition)
                    .GenerateMessage(((ConcurrencyExceptionEventData)eventData).Exception),
            context,
            entries,
            exception);

    protected override void Consume(RelationalDataReader reader)
        => throw new NotSupportedException("DuckLake modification batches do not consume result sets.");

    protected override Task ConsumeAsync(RelationalDataReader reader, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("DuckLake modification batches do not consume result sets.");
}