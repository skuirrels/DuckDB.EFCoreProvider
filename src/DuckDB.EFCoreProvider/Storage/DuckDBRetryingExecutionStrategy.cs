using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace DuckDB.EFCoreProvider.Storage;

/// <summary>
///     An execution strategy that retries a complete unit of work when DuckDB, or a DuckLake catalog, rejects it
///     because of a write-write conflict with a concurrent transaction.
/// </summary>
/// <remarks>
///     <para>
///         DuckDB uses optimistic concurrency control: when two transactions update or delete the same row, or
///         change the same catalog entry, the later one fails with a conflict instead of waiting. The failed
///         transaction has applied nothing, so running the same operation again in a new transaction normally
///         succeeds. Constraint violations, aborted transactions, and every other error are not retried.
///     </para>
///     <para>
///         As with every retrying EF Core execution strategy, an explicit transaction must be started inside
///         <see cref="IExecutionStrategy.Execute{TState,TResult}" />, for example through
///         <c>context.Database.CreateExecutionStrategy().ExecuteAsync(...)</c>, so the strategy can replay the whole
///         transaction. <c>BulkInsert</c> and <c>Upsert</c> called outside a transaction are retried automatically: each
///         attempt runs in a provider-owned transaction, so the whole call commits or rolls back as one unit. Inside a
///         caller's transaction they run once, and the caller's strategy replays that transaction instead. To classify
///         a conflict without retrying, use <c>exception.IsWriteConflict()</c>.
///     </para>
/// </remarks>
public class DuckDBRetryingExecutionStrategy : ExecutionStrategy
{
    /// <summary>
    ///     Creates a strategy that retries up to <see cref="ExecutionStrategy.DefaultMaxRetryCount" /> times with
    ///     at most <see cref="ExecutionStrategy.DefaultMaxDelay" /> between attempts.
    /// </summary>
    /// <param name="dependencies">Parameter object containing service dependencies.</param>
    public DuckDBRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : this(dependencies, DefaultMaxRetryCount)
    {
    }

    /// <summary>
    ///     Creates a strategy that retries up to <paramref name="maxRetryCount" /> times with at most
    ///     <see cref="ExecutionStrategy.DefaultMaxDelay" /> between attempts.
    /// </summary>
    /// <param name="dependencies">Parameter object containing service dependencies.</param>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    public DuckDBRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies, int maxRetryCount)
        : this(dependencies, maxRetryCount, DefaultMaxDelay)
    {
    }

    /// <summary>
    ///     Creates a strategy that retries up to <paramref name="maxRetryCount" /> times with at most
    ///     <paramref name="maxRetryDelay" /> between attempts.
    /// </summary>
    /// <param name="dependencies">Parameter object containing service dependencies.</param>
    /// <param name="maxRetryCount">The maximum number of retry attempts.</param>
    /// <param name="maxRetryDelay">The maximum delay between retries.</param>
    public DuckDBRetryingExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        int maxRetryCount,
        TimeSpan maxRetryDelay)
        : base(dependencies, maxRetryCount, maxRetryDelay)
    {
    }

    /// <summary>
    ///     Returns <see langword="true" /> when <paramref name="exception" /> is a DuckDB or DuckLake write-write
    ///     conflict. Override to retry additional, application-specific failures.
    /// </summary>
    /// <param name="exception">The exception thrown by the operation.</param>
    /// <returns><see langword="true" /> if the operation should be retried.</returns>
    protected override bool ShouldRetryOn(Exception exception)
        => DuckDBTransientExceptionDetector.ShouldRetryOn(exception);
}