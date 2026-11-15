namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     Defines strategies to use across the EF Core stack when generating key values
///     from DuckDB database columns.
/// </summary>
/// <remarks>
///     See <see href="https://aka.ms/efcore-docs-conventions">Model building conventions</see>.
/// </remarks>
public enum DuckDBValueGenerationStrategy
{
    /// <summary>
    ///     No DuckDB-specific strategy.
    /// </summary>
    None = 0,

    /// <summary>
    ///     A sequence-backed column default, evaluated by the database on insert.
    /// </summary>
    AutoIncrement = 1,

    /// <summary>
    ///     Client-side Hi-Lo key generation: blocks of values are reserved from a counter table, so keys are
    ///     assigned before insert and no sequence or <c>RETURNING</c> is needed. Use it where sequences are not
    ///     available, such as DuckLake.
    /// </summary>
    HiLo = 2
}