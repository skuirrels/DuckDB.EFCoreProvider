using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Update;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     Preserves EF Core optimistic-concurrency and interceptor semantics for commands executed without
///     a result-set-based affected-row check.
/// </summary>
internal static class DuckDBAffectedRowsValidator
{
    public static bool Validate(
        ModificationCommandBatchFactoryDependencies dependencies,
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        int affectedRows)
    {
        var expectedRows = commands.Count;
        if (affectedRows == expectedRows)
        {
            return true;
        }

        var entries = GetEntries(commands);
        var exception = CreateConcurrencyException(expectedRows, affectedRows, entries);

        if (!dependencies.UpdateLogger.OptimisticConcurrencyException(
                dependencies.CurrentContext.Context,
                entries,
                exception,
                CreateConcurrencyExceptionEventData).IsSuppressed)
        {
            throw exception;
        }

        return false;
    }

    public static async Task<bool> ValidateAsync(
        ModificationCommandBatchFactoryDependencies dependencies,
        IReadOnlyList<IReadOnlyModificationCommand> commands,
        int affectedRows,
        CancellationToken cancellationToken)
    {
        var expectedRows = commands.Count;
        if (affectedRows == expectedRows)
        {
            return true;
        }

        var entries = GetEntries(commands);
        var exception = CreateConcurrencyException(expectedRows, affectedRows, entries);

        if (!(await dependencies.UpdateLogger.OptimisticConcurrencyExceptionAsync(
                    dependencies.CurrentContext.Context,
                    entries,
                    exception,
                    CreateConcurrencyExceptionEventData,
                    cancellationToken)
                .ConfigureAwait(false)).IsSuppressed)
        {
            throw exception;
        }

        return false;
    }

    private static IReadOnlyList<IUpdateEntry> GetEntries(
        IReadOnlyList<IReadOnlyModificationCommand> commands)
        => commands.SelectMany(command => command.Entries).ToList();

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
}