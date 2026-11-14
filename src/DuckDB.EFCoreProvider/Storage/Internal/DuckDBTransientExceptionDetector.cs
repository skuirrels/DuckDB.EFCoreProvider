using DuckDB.NET.Data;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     Classifies DuckDB failures that a complete retry of the same unit of work can resolve: optimistic
///     write-write conflicts between concurrent transactions, raised by DuckDB itself or by the DuckLake catalog.
/// </summary>
/// <remarks>
///     DuckDB reports these with the <c>TransactionContext</c> error type, but other errors of that type, such as an
///     aborted transaction or a constraint violation found at commit, are not transient. Quack relays remote errors
///     through a local exception whose error type may differ. Classification therefore matches the conflict wording
///     DuckDB and DuckLake use, anywhere in the exception chain.
/// </remarks>
internal static class DuckDBTransientExceptionDetector
{
    private static readonly string[] ConflictMessages =
    [
        // DuckDB MVCC: "Conflict on update!", "Conflict on tuple deletion!".
        "Conflict on update",
        "Conflict on tuple deletion",
        // DuckDB catalog: "Catalog write-write conflict on create with ...".
        "write-write conflict",
        // DuckLake catalog commits.
        "Transaction conflict - ",
        "but another transaction has",
        "Exceeded the maximum retry count of",
    ];

    internal static bool ShouldRetryOn(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DuckDBException && IsConflict(current.Message))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsConflict(string message)
    {
        foreach (var conflict in ConflictMessages)
        {
            if (message.Contains(conflict, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}