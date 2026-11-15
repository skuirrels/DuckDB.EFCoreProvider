using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

/// <summary>
///     Runs a raw provider write, such as <c>BulkInsert</c> or <c>Upsert</c>, through the context's execution strategy
///     so a retrying strategy can replay it after a write-write conflict.
/// </summary>
/// <remarks>
///     A replay is only safe when the whole write can be undone, so the write is retried only when it can own its
///     transaction: a retrying strategy is configured and no transaction is already active on the context. Each
///     attempt then runs in a provider-owned transaction, which also makes a multi-batch write atomic. Inside a
///     caller's strategy delegate the write runs once in its own transaction and the caller's strategy replays the
///     delegate. Inside a caller's transaction, or without a retrying strategy, the write runs once exactly as before.
///     Inputs that can only be enumerated once are buffered before the first attempt so a replay sees the same
///     entities.
/// </remarks>
internal static class DuckDBRetryableWrite
{
    internal static int Execute<TEntity>(
        DbContext context,
        IEnumerable<TEntity> entities,
        Func<DbContext, IEnumerable<TEntity>, int> write)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        if (!CanOwnTransaction(context, strategy))
        {
            return write(context, entities);
        }

        return strategy.Execute(
            (Write: write, Entities: Replayable(entities)),
            static (attemptContext, state) =>
            {
                using var transaction = attemptContext.Database.BeginTransaction();
                var count = state.Write(attemptContext, state.Entities);
                transaction.Commit();
                return count;
            },
            verifySucceeded: null);
    }

    internal static Task<int> ExecuteAsync<TEntity>(
        DbContext context,
        IEnumerable<TEntity> entities,
        Func<DbContext, IEnumerable<TEntity>, CancellationToken, Task<int>> write,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        if (!CanOwnTransaction(context, strategy))
        {
            return write(context, entities, cancellationToken);
        }

        return strategy.ExecuteAsync(
            (Write: write, Entities: Replayable(entities)),
            static async (attemptContext, state, token) =>
            {
                await using var transaction = await attemptContext.Database.BeginTransactionAsync(token)
                    .ConfigureAwait(false);
                var count = await state.Write(attemptContext, state.Entities, token).ConfigureAwait(false);
                await transaction.CommitAsync(token).ConfigureAwait(false);
                return count;
            },
            verifySucceeded: null,
            cancellationToken);
    }

    // EF Core suspends RetriesOnFailure while a strategy is already executing a delegate, so a write nested in the
    // caller's strategy is recognised by the configured retry count. It still runs in its own transaction, making it
    // atomic, and the caller's strategy replays the delegate.
    private static bool CanOwnTransaction(DbContext context, IExecutionStrategy strategy)
        => (strategy.RetriesOnFailure || strategy is ExecutionStrategy { MaxRetryCount: > 0 })
            && context.Database.CurrentTransaction is null
            && System.Transactions.Transaction.Current is null;

    private static IEnumerable<TEntity> Replayable<TEntity>(IEnumerable<TEntity> entities)
        => entities is ICollection<TEntity> or IReadOnlyCollection<TEntity> ? entities : entities.ToList();
}