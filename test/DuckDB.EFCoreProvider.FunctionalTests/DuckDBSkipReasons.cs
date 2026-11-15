namespace Microsoft.EntityFrameworkCore;

// Skip-reason taxonomy. See docs/CAPABILITY-MAP.md.
// Prefer NotSupportedByDuckDB / NotYetImplemented / Investigating for new skips so the capability map
// stays accurate; Tbd is the legacy generic backlog marker.
internal static class DuckDBSkipReasons
{
    /// <summary>Legacy/generic backlog marker. Prefer a specific reason below for new skips.</summary>
    public const string Tbd = "TBD";

    /// <summary>Confirmed DuckDB engine limitation (capability map §2). Not planned.</summary>
    public const string NotSupportedByDuckDB = "No support for that ALTER TABLE option yet!";

    /// <summary>
    ///     DuckDB accepts ASC/DESC in CREATE INDEX but does not retain the column sort direction: a persisted
    ///     descending index reads back without DESC, so the direction cannot be round-tripped. Engine
    ///     limitation (capability map §2).
    /// </summary>
    public const string IndexDirectionNotRetained = "DuckDB does not retain index column sort direction (DESC is discarded).";

    /// <summary>DuckDB engine limitation: "Creating partial indexes is not supported currently".</summary>
    public const string PartialIndexesNotSupported = "DuckDB does not support partial/filtered indexes.";

    /// <summary>DuckDB engine limitation: renaming an index is not supported (ALTER INDEX ... RENAME).</summary>
    public const string RenameIndexNotSupported = "DuckDB does not support renaming indexes.";

    /// <summary>
    ///     DuckDB engine limitation: a referenced row cannot currently be updated or deleted while the
    ///     referencing foreign-key row exists, even when the update does not change the referenced key.
    /// </summary>
    public const string ReferencedRowsCannotBeUpdated =
        "DuckDB cannot update or delete a row while it is referenced by a foreign key.";

    /// <summary>Provider roadmap (capability map §3): could be supported, not implemented yet.</summary>
    public const string NotYetImplemented = "Not yet implemented by the DuckDB provider.";

    /// <summary>Failure under investigation; not yet classified as limitation vs roadmap.</summary>
    public const string Investigating = "Under investigation; not yet classified.";

    /// <summary>
    ///     DuckDB engine limitation: <c>TIMESTAMPTZ</c> stores the instant but not the original offset, so members that
    ///     depend on that offset, such as <c>DateTimeOffset.DateTime</c> and <c>ToOffset</c>, cannot be translated.
    /// </summary>
    public const string OffsetNotStored = "DuckDB TIMESTAMPTZ does not store the original DateTimeOffset offset.";

    /// <summary>
    ///     EF Core renders an unordered <c>First()</c> over a group as a correlated <c>TOP(1)</c> subquery without
    ///     <c>ORDER BY</c>. The row it picks is undefined; DuckDB does not keep insertion order there, while the
    ///     expected results assume the first row in key order.
    /// </summary>
    public const string UnorderedFirstInGroup =
        "EF Core renders First() over a group as an unordered TOP(1) subquery, so the row DuckDB picks is undefined.";

    /// <summary>
    ///     Provider design: owned values stored in a separate table (owned collections and owned references mapped
    ///     to another table) are not auto-included, because those joins change set-operation shapes. A no-tracking
    ///     projection of an owned value therefore leaves its separately stored owned values unloaded.
    /// </summary>
    public const string SeparateTableOwnedValuesNotAutoIncluded =
        "Owned values stored in a separate table are not auto-included by the DuckDB provider.";

    /// <summary>Specification coverage added in EF Core 11.0.0 that the provider does not support yet.</summary>
    public const string NewEF11Coverage =
        "New EF Core 11 coverage not yet supported by the DuckDB provider; see docs/EF10-EF11-SUPPORT.md.";
}