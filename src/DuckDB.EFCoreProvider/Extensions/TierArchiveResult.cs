namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>The kind of tiered-storage maintenance represented by an archive result.</summary>
public enum TierArchiveOperation
{
    /// <summary>Moves the next forward watermark window from hot tables to the active cold generation.</summary>
    Archive,

    /// <summary>Rebuilds and publishes a complete cold generation, with approved hot representations winning.</summary>
    Reconcile,

    /// <summary>Moves an explicitly selected cold aggregate set back into mapped hot tables.</summary>
    Restore,

    /// <summary>Rewrites the complete active cold range into a verified immutable replacement generation.</summary>
    Compact,

    /// <summary>Rewrites archived files to the currently configured archive contract.</summary>
    RewriteContract,

    /// <summary>Publishes a replacement cold generation that omits rows before a caller-supplied boundary.</summary>
    RetentionTrim,
}

/// <summary>The last archive stage reached by a successful or failed tiered-storage operation.</summary>
public enum TierArchiveStage
{
    /// <summary>Configuration, conflict, and match-key checks.</summary>
    Preflight,

    /// <summary>Writing Parquet files.</summary>
    Copy,

    /// <summary>Verifying copied row counts and collecting file evidence.</summary>
    Verify,

    /// <summary>Publishing the watermark or active cold generation and refreshing views.</summary>
    Publish,

    /// <summary>Deleting representations that are safely present in published Parquet.</summary>
    DeleteHot,

    /// <summary>Checkpointing the DuckDB database after publication and cleanup.</summary>
    Checkpoint,

    /// <summary>The operation completed successfully.</summary>
    Completed,
}

/// <summary>Table-level evidence produced by a tiered-storage archive or reconciliation operation.</summary>
/// <param name="Table">The physical table name.</param>
/// <param name="Schema">The physical schema, or <see langword="null" /> for the default schema.</param>
/// <param name="SelectedRows">Rows selected for this table's Parquet output.</param>
/// <param name="CopiedRows">Rows verified in this table's relevant Parquet output.</param>
/// <param name="DeletedRows">Rows removed from the hot table after publication.</param>
/// <param name="ArchivePath">The table's active archive path.</param>
/// <param name="Files">Parquet files visible below <paramref name="ArchivePath" /> after the operation.</param>
public readonly record struct TierArchiveNodeResult(
    string Table,
    string? Schema,
    long SelectedRows,
    long CopiedRows,
    long DeletedRows,
    string ArchivePath,
    IReadOnlyList<string> Files)
{
    /// <summary>The deterministic relationship binding used to select and maintain this table.</summary>
    public string? BindingId { get; init; }

    /// <summary>The complete number of Parquet files represented by this result.</summary>
    public long FileCount { get; init; } = Files.Count;

    /// <summary>The combined size in bytes of the represented Parquet files when available.</summary>
    public long TotalBytes { get; init; }

    /// <summary><see langword="true" /> when <see cref="Files" /> is a bounded representative subset.</summary>
    public bool FilesTruncated { get; init; }
}

/// <summary>
///     The outcome and operational evidence of a tiered-storage archive or reconciliation call.
/// </summary>
/// <param name="RowsArchived">
///     The number of aggregate roots selected for Parquet output. For reconciliation this is the size of the
///     complete replacement cold root generation, not only the number of corrected hot roots.
/// </param>
/// <param name="Watermark">The published archive watermark after the operation.</param>
/// <param name="ArchivePath">The active root-table archive path after the operation.</param>
/// <param name="NoOp"><see langword="true" /> when no new Parquet output was required.</param>
public readonly record struct TierArchiveResult(
    long RowsArchived,
    DateTime Watermark,
    string ArchivePath,
    bool NoOp)
{
    /// <summary>The root-scoped binding that produced this result.</summary>
    public TieredStorageBindingInfo? Binding { get; init; }

    /// <summary>The operation that produced the result.</summary>
    public TierArchiveOperation Operation { get; init; }

    /// <summary>The watermark before the operation, or <see langword="null" /> before the first publication.</summary>
    public DateTime? PreviousWatermark { get; init; }

    /// <summary>The inclusive start of the archive window represented by this result.</summary>
    public DateTime WindowStart { get; init; }

    /// <summary>The exclusive end of the archive window represented by this result.</summary>
    public DateTime WindowEnd { get; init; }

    /// <summary>The published cold-generation revision, or <see langword="null" /> for the original layout.</summary>
    public string? Revision { get; init; }

    /// <summary>The last stage reached. Successful results use <see cref="TierArchiveStage.Completed" />.</summary>
    public TierArchiveStage Stage { get; init; }

    /// <summary>Per-table archive evidence in aggregate order.</summary>
    public IReadOnlyList<TierArchiveNodeResult> Nodes { get; init; } = [];
}

/// <summary>
///     Raised when an archive operation fails after its workflow has started. The partial result identifies the
///     failure stage and any table-level evidence collected before the failure.
/// </summary>
public sealed class TierArchiveOperationException : InvalidOperationException
{
    /// <summary>Creates an archive failure with its safe partial manifest.</summary>
    public TierArchiveOperationException(TierArchiveStage stage, TierArchiveResult partialResult, Exception innerException)
        : base(
            $"Tiered-storage operation{BindingEvidence(partialResult)} failed during the '{stage}' stage. "
            + "The active archive remains recoverable; "
            + "inspect PartialResult and retry after correcting the underlying error.",
            innerException)
    {
        Stage = stage;
        PartialResult = partialResult;
    }

    /// <summary>The stage at which the workflow failed.</summary>
    public TierArchiveStage Stage { get; }

    /// <summary>The evidence collected before the failure.</summary>
    public TierArchiveResult PartialResult { get; }

    private static string BindingEvidence(TierArchiveResult result)
        => result.Binding is { } binding
            ? $" for binding {TieredStorageBindingEvidence.Describe(binding)}"
            : string.Empty;
}
