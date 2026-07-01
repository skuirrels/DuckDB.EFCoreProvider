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

    /// <summary>Provider roadmap (capability map §3): could be supported, not implemented yet.</summary>
    public const string NotYetImplemented = "Not yet implemented by the DuckDB provider.";

    /// <summary>Failure under investigation; not yet classified as limitation vs roadmap.</summary>
    public const string Investigating = "Under investigation; not yet classified.";
}
