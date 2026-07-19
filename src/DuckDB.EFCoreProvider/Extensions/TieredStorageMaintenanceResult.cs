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

/// <summary>
///     A bounded read-only page of hot descendants whose complete configured parent chain exists only in the
///     active cold generation and whose stable identity is not already represented there.
/// </summary>
public readonly record struct TierDetachedDescendantPage(
    long TotalDescendants,
    int Offset,
    int Limit,
    IReadOnlyList<TierConflictKey> Keys)
{
    /// <summary><see langword="true" /> when additional diagnostic identities exist after this page.</summary>
    public bool HasMore => Offset + Keys.Count < TotalDescendants;
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
    IReadOnlyList<string> RepresentativeFiles)
{
    /// <summary>The provider operation that created a recoverable unpublished candidate, when known.</summary>
    public TierArchiveOperation? Operation { get; init; }

    /// <summary><see langword="true" /> when the Provider found its durable candidate marker.</summary>
    public bool HasCandidateMarker { get; init; }

    /// <summary>
    ///     <see langword="true" /> when the marker proves that the candidate belongs to the exact configured
    ///     root binding, aggregate graph, archive contract, and partition contract.
    /// </summary>
    public bool ContractCompatible { get; init; }

    /// <summary>Per-node file evidence for a contract-compatible unpublished candidate.</summary>
    public IReadOnlyList<TierArchiveGenerationNodeInfo> Nodes { get; init; } = [];

    internal string? ProviderArchivePath { get; init; }
}

/// <summary>Read-only per-node evidence for a recoverable unpublished archive candidate.</summary>
public readonly record struct TierArchiveGenerationNodeInfo(
    string Table,
    string? Schema,
    string BindingId,
    long FileCount,
    long TotalBytes,
    IReadOnlyList<string> RepresentativeFiles);

/// <summary>A read-only inventory of active and historical archive generations.</summary>
public readonly record struct TierArchiveGenerationInventory(
    string ControlKey,
    string ActiveGenerationId,
    IReadOnlyList<TierArchiveGenerationInfo> Generations)
{
    /// <summary>The root-scoped binding represented by this inventory.</summary>
    public TieredStorageBindingInfo? Binding { get; init; }

    /// <summary>
    ///     <see langword="true" /> when the Provider has an authoritative watermark and active-generation
    ///     selection for this binding. Cleanup must fail closed when this evidence is absent.
    /// </summary>
    public bool HasAuthoritativeActiveGeneration { get; init; }
}

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
    Scheme = 0,

    /// <summary>Required DuckDB extensions are installed.</summary>
    ExtensionInstalled = 1,

    /// <summary>Required DuckDB extensions are loaded.</summary>
    ExtensionLoaded = 2,

    /// <summary>The configured archive path can be listed.</summary>
    List = 3,

    /// <summary>Existing objects below the configured path can be read.</summary>
    Read = 4,

    /// <summary>A disposable probe object can be written.</summary>
    Write = 5,

    /// <summary>A disposable probe object can be deleted.</summary>
    Delete = 6,

    /// <summary>Every physical shared child row belongs to at most one configured root binding.</summary>
    BindingOwnership = 7,
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
    /// <summary>The root-scoped binding checked by this preflight.</summary>
    public TieredStorageBindingInfo? Binding { get; init; }

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

/// <summary>Per-table evidence captured by a retention plan.</summary>
public readonly record struct TierArchiveRetentionNodePlan(
    Type EntityType,
    string Table,
    string? Schema,
    long InputRows,
    long RetainedRows,
    long ExcludedRows,
    long InputFileCount,
    long InputBytes)
{
    /// <summary>The deterministic root or descendant relationship binding represented by this node.</summary>
    public string BindingId { get; init; } = string.Empty;
}

/// <summary>
///     An immutable, reviewable plan for publishing a retention-trimmed cold generation. The provider gives no
///     business meaning to the boundary or retained partition scopes.
/// </summary>
public sealed class TierArchiveRetentionPlan
{
    internal TierArchiveRetentionPlan(
        string controlKey,
        string fingerprint,
        TieredStorageBindingInfo binding,
        DateTime requestedRetainFrom,
        DateTime effectiveRetainFrom,
        DateTime watermark,
        string inputGenerationId,
        string expectedOutputGenerationId,
        string inputArchivePath,
        IReadOnlyList<TierMaintenanceScope> retainedPartitionScopes,
        IReadOnlyList<TierArchiveRetentionNodePlan> nodes,
        long inputFileCount,
        long inputBytes,
        TierParquetWriterOptions writer,
        TierManifestOptions manifest)
    {
        ControlKey = controlKey;
        Fingerprint = fingerprint;
        Binding = binding;
        RequestedRetainFrom = requestedRetainFrom;
        EffectiveRetainFrom = effectiveRetainFrom;
        Watermark = watermark;
        InputGenerationId = inputGenerationId;
        ExpectedOutputGenerationId = expectedOutputGenerationId;
        InputArchivePath = inputArchivePath;
        RetainedPartitionScopes = retainedPartitionScopes;
        Nodes = nodes;
        InputFileCount = inputFileCount;
        InputBytes = inputBytes;
        Writer = writer;
        Manifest = manifest;
    }

    /// <summary>The provider control key for the exact root-scoped binding.</summary>
    public string ControlKey { get; }

    /// <summary>A deterministic fingerprint of the active generation, contracts, scopes, counts, and exact files.</summary>
    public string Fingerprint { get; }

    /// <summary>The exact root-scoped binding represented by this plan.</summary>
    public TieredStorageBindingInfo Binding { get; }

    /// <summary>The boundary supplied by the caller.</summary>
    public DateTime RequestedRetainFrom { get; }

    /// <summary>The boundary aligned down to the configured lifecycle granularity.</summary>
    public DateTime EffectiveRetainFrom { get; }

    /// <summary>The active archive watermark retained by the replacement generation.</summary>
    public DateTime Watermark { get; }

    /// <summary>The active generation from which this plan was produced.</summary>
    public string InputGenerationId { get; }

    /// <summary>The deterministic generation expected after a non-empty trim.</summary>
    public string ExpectedOutputGenerationId { get; }

    /// <summary>The redacted active archive path at planning time.</summary>
    public string InputArchivePath { get; }

    /// <summary>Caller-supplied exact technical partition scopes retained below the boundary.</summary>
    public IReadOnlyList<TierMaintenanceScope> RetainedPartitionScopes { get; }

    /// <summary>Per-table input, retained, excluded, and file evidence.</summary>
    public IReadOnlyList<TierArchiveRetentionNodePlan> Nodes { get; }

    /// <summary>The exact number of files in the input generation.</summary>
    public long InputFileCount { get; }

    /// <summary>The combined size of input-generation files when reported by DuckDB.</summary>
    public long InputBytes { get; }

    /// <summary>Parquet writer controls bound into the plan fingerprint.</summary>
    public TierParquetWriterOptions Writer { get; }

    /// <summary>Manifest controls used by publication.</summary>
    public TierManifestOptions Manifest { get; }

    /// <summary><see langword="true" /> when the planned publication would exclude no cold rows.</summary>
    public bool IsNoOp => Nodes.All(node => node.ExcludedRows == 0);
}

/// <summary>A read-only cleanup candidate. The provider assigns no retention or legal meaning to it.</summary>
public readonly record struct TierArchiveCleanupCandidate(
    string GenerationId,
    TierArchiveGenerationState State,
    string ArchivePath,
    long FileCount,
    long TotalBytes)
{
    /// <summary>A SHA-256 fingerprint of the Provider-enumerated exact Parquet file catalogue.</summary>
    public string FileCatalogueFingerprint { get; init; } = string.Empty;
}

/// <summary>A reviewed cleanup plan whose fingerprint can be required by a later execution API.</summary>
public readonly record struct TierArchiveCleanupPlan(
    string ControlKey,
    string ActiveGenerationId,
    string Fingerprint,
    IReadOnlyList<TierArchiveCleanupCandidate> Candidates)
{
    /// <summary>The root-scoped binding represented by this plan.</summary>
    public TieredStorageBindingInfo? Binding { get; init; }
}