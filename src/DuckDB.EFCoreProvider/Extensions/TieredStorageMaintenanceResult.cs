namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>A configured match-key value returned by bounded tiered-storage diagnostics.</summary>
public readonly record struct TierConflictKey(
    Type EntityType,
    string Table,
    IReadOnlyDictionary<string, object?> Values);

/// <summary>A bounded page of conflicting hot/cold identities.</summary>
public readonly record struct TierConflictPage(
    long TotalConflicts,
    int Offset,
    int Limit,
    IReadOnlyList<TierConflictKey> Keys)
{
    /// <summary><see langword="true" /> when additional conflict identities exist after this page.</summary>
    public bool HasMore => Offset + Keys.Count < TotalConflicts;
}

/// <summary>The publication evidence attached to an archive generation.</summary>
public enum TierArchiveGenerationState
{
    /// <summary>The generation is currently selected by the provider control row.</summary>
    Active,

    /// <summary>The provider has durable evidence that the generation was published previously.</summary>
    Published,

    /// <summary>Files exist, but provider metadata cannot prove that the generation was ever published.</summary>
    UnpublishedCandidate,

    /// <summary>The generation was discovered without enough evidence to classify it safely.</summary>
    Unknown,
}

/// <summary>Read-only evidence about one immutable archive generation.</summary>
public readonly record struct TierArchiveGenerationInfo(
    string GenerationId,
    TierArchiveGenerationState State,
    string ArchivePath,
    DateTime Watermark,
    DateTime CreatedAtUtc,
    long FileCount,
    long TotalBytes,
    IReadOnlyList<string> RepresentativeFiles);

/// <summary>A read-only inventory of active and historical archive generations.</summary>
public readonly record struct TierArchiveGenerationInventory(
    string ControlKey,
    string ActiveGenerationId,
    IReadOnlyList<TierArchiveGenerationInfo> Generations);

/// <summary>The result of moving a selected cold aggregate set back into mapped hot tables.</summary>
public readonly record struct TierRestoreResult(
    long RootsSelected,
    long RootsInserted,
    long RowsInserted,
    string PreviousGenerationId,
    string ActiveGenerationId,
    TierArchiveResult Publication);

/// <summary>A non-secret connectivity or object-store capability checked by tiered-storage preflight.</summary>
public enum TierStorageCapability
{
    /// <summary>The configured URL scheme is understood by the provider.</summary>
    Scheme,

    /// <summary>Required DuckDB extensions are installed.</summary>
    ExtensionInstalled,

    /// <summary>Required DuckDB extensions are loaded.</summary>
    ExtensionLoaded,

    /// <summary>The configured archive path can be listed.</summary>
    List,

    /// <summary>Existing objects below the configured path can be read.</summary>
    Read,

    /// <summary>A disposable probe object can be written.</summary>
    Write,

    /// <summary>A disposable probe object can be deleted.</summary>
    Delete,
}

/// <summary>The outcome of one non-secret preflight capability check.</summary>
public readonly record struct TierStorageCapabilityResult(
    TierStorageCapability Capability,
    bool Supported,
    string Message)
{
    /// <summary>
    ///     <see langword="true" /> when the capability was exercised or could be decided conclusively.
    ///     A capability can be untested when, for example, an empty archive contains no object to read.
    /// </summary>
    public bool WasTested { get; init; } = true;
}

/// <summary>Options controlling whether preflight remains read-only or performs a disposable write/delete probe.</summary>
public sealed class TierStoragePreflightOptions
{
    /// <summary>
    ///     When <see langword="true" />, write and delete a uniquely named disposable Parquet probe under the
    ///     configured archive prefix. The default is read-only.
    /// </summary>
    public bool ProbeWriteAndDelete { get; init; }
}

/// <summary>A non-secret tier-storage connectivity and capability report.</summary>
public readonly record struct TierStoragePreflightResult(
    string Scheme,
    string RedactedArchivePath,
    IReadOnlyList<TierStorageCapabilityResult> Capabilities)
{
    /// <summary><see langword="true" /> when every capability that was tested succeeded.</summary>
    public bool Succeeded => Capabilities.All(result => !result.WasTested || result.Supported);
}

/// <summary>The kind of archive-contract difference found between persisted and target metadata.</summary>
public enum TierArchiveContractDifferenceKind
{
    /// <summary>The target contract adds a mapped column.</summary>
    ColumnAdded,

    /// <summary>The persisted contract contains a column absent from the target.</summary>
    ColumnRemoved,

    /// <summary>A column's mapped DuckDB store type changed.</summary>
    ColumnTypeChanged,

    /// <summary>A nullable archived column became required.</summary>
    NullabilityTightened,

    /// <summary>The aggregate include/table layout changed.</summary>
    AggregateLayoutChanged,

    /// <summary>The configured match-key layout changed.</summary>
    MatchKeyChanged,

    /// <summary>The lifecycle, partition, granularity, control key, or archive path changed.</summary>
    PhysicalLayoutChanged,
}

/// <summary>One objective difference between persisted and target archive contracts.</summary>
public readonly record struct TierArchiveContractDifference(
    TierArchiveContractDifferenceKind Kind,
    string Node,
    string? Column,
    string Description);

/// <summary>A read-only comparison of persisted and currently configured archive contracts.</summary>
public readonly record struct TierArchiveContractInspection(
    string ControlKey,
    string? PersistedContractJson,
    string TargetContractJson,
    bool IsCompatible,
    IReadOnlyList<TierArchiveContractDifference> Differences);

/// <summary>A caller-specified archived-column source used by a contract rewrite plan.</summary>
public sealed class TierArchiveColumnRewrite
{
    /// <summary>The mapped entity type containing the target property.</summary>
    public required Type EntityType { get; init; }

    /// <summary>The target EF Core property name.</summary>
    public required string TargetProperty { get; init; }

    /// <summary>The persisted archived column to read, or <see langword="null" /> when supplying a constant.</summary>
    public string? SourceColumn { get; init; }

    /// <summary>
    ///     A caller-supplied constant for a new required column. The value is converted using the target EF Core
    ///     relational type mapping. It is ignored when <see cref="SourceColumn" /> is supplied.
    /// </summary>
    public object? ConstantValue { get; init; }
}

/// <summary>Caller-specified semantic mappings required to plan an archive-contract rewrite.</summary>
public sealed class TierArchiveRewriteOptions
{
    /// <summary>Explicit source mappings for renamed, converted, or newly required columns.</summary>
    public IReadOnlyList<TierArchiveColumnRewrite> Columns { get; init; } = [];

    /// <summary>Parquet writer controls for the replacement generation.</summary>
    public TierParquetWriterOptions Writer { get; init; } = TierParquetWriterOptions.Default;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Columns);
        ArgumentNullException.ThrowIfNull(Writer);
        Writer.Validate();
    }
}

/// <summary>An immutable, reviewable archive-contract rewrite plan.</summary>
public readonly record struct TierArchiveRewritePlan(
    string ControlKey,
    string Fingerprint,
    TierArchiveContractInspection Inspection,
    IReadOnlyList<TierArchiveColumnRewrite> ColumnRewrites,
    TierParquetWriterOptions Writer);

/// <summary>A read-only cleanup candidate. The provider assigns no retention or legal meaning to it.</summary>
public readonly record struct TierArchiveCleanupCandidate(
    string GenerationId,
    TierArchiveGenerationState State,
    string ArchivePath,
    long FileCount,
    long TotalBytes);

/// <summary>A reviewed cleanup plan whose fingerprint can be required by a later execution API.</summary>
public readonly record struct TierArchiveCleanupPlan(
    string ControlKey,
    string ActiveGenerationId,
    string Fingerprint,
    IReadOnlyList<TierArchiveCleanupCandidate> Candidates);
