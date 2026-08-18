namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Selects how an <c>UpsertAsync</c> conflict target matches existing rows.
/// </summary>
public enum DuckDBUpsertMatchMode
{
    /// <summary>
    ///     Match through a declared primary key, alternate key, or unique index. On native DuckDB this executes a
    ///     set-based <c>INSERT ... ON CONFLICT</c> against the backing ART index; DuckLake merges on its logical
    ///     key metadata. This is the default and requires the selected properties to be backed by a declared key
    ///     or unique index.
    /// </summary>
    UniqueConflictTarget,

    /// <summary>
    ///     Match the selected properties as a logical key using a set-based <c>MERGE INTO</c>, without requiring a
    ///     physical unique constraint or index. Uniqueness is not engine-enforced: each staged batch fails before
    ///     mutation when one staged key matches multiple existing rows, and callers own preventing concurrent
    ///     duplicate inserts. Because no ART index is maintained, sustained ingest avoids the per-row cost and
    ///     memory footprint that grow with indexed table size; each batch instead joins against the target table,
    ///     so very small batches against very large tables favor <see cref="UniqueConflictTarget" />.
    /// </summary>
    LogicalKeyMerge,
}
