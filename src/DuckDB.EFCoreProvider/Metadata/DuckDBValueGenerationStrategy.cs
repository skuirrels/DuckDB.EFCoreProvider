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
    None = 0,

    AutoIncrement = 1
}
