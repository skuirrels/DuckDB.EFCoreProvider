using DuckDB.EFCoreProvider.Storage.Internal;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Classifies exceptions raised by DuckDB and DuckLake writes.
/// </summary>
public static class DuckDBExceptionExtensions
{
    /// <summary>
    ///     Returns <see langword="true" /> when <paramref name="exception" />, or any exception it wraps, is a DuckDB or
    ///     DuckLake write-write conflict with a concurrent transaction.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         DuckDB reports overlapping updates as an optimistic write-write conflict, which EF Core surfaces as a
    ///         <see cref="Microsoft.EntityFrameworkCore.DbUpdateException" />, not a
    ///         <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException" />. The failed transaction applied
    ///         nothing, so the whole unit of work can be run again in a new transaction.
    ///     </para>
    ///     <para>
    ///         This is the same classification <see cref="Storage.DuckDBRetryingExecutionStrategy" /> retries.
    ///         Constraint violations, aborted transactions, and every other error return <see langword="false" />.
    ///     </para>
    /// </remarks>
    /// <param name="exception">The exception to classify.</param>
    /// <returns><see langword="true" /> if the exception is a write-write conflict.</returns>
    public static bool IsWriteConflict(this Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return DuckDBTransientExceptionDetector.ShouldRetryOn(exception);
    }
}