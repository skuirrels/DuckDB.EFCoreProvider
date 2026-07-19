namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Persistable, provider-neutral evidence for one table in an active immutable archive generation. File paths
///     are deliberately excluded; the Provider resolves its physical layout during recovery.
/// </summary>
public readonly record struct TierArchiveRecoveryNodeCheckpoint(
    string Table,
    string? Schema,
    string BindingId,
    long RowCount,
    long FileCount,
    long TotalBytes,
    string FileCatalogueFingerprint);

/// <summary>
///     Exportable evidence required to restore Provider control and generation-catalogue state after local metadata
///     loss. Applications should persist this checkpoint outside the DuckDB database without interpreting archive
///     paths or Provider tables.
/// </summary>
public sealed record TierArchiveRecoveryCheckpoint(
    int FormatVersion,
    string ControlKey,
    string BindingId,
    string ActiveGenerationId,
    DateTime Watermark,
    DateTime? BootstrapFromInclusive,
    DateTime? BootstrapToExclusive,
    string ArchiveContractFingerprint,
    string PartitionContractFingerprint,
    IReadOnlyList<TierArchiveRecoveryNodeCheckpoint> Nodes,
    string Fingerprint);

/// <summary>
///     A reviewed recovery plan produced after the Provider has re-derived its physical generation path and
///     revalidated the checkpoint against current model and object evidence.
/// </summary>
public sealed class TierArchiveRecoveryPlan
{
    internal TierArchiveRecoveryPlan(
        TierArchiveRecoveryCheckpoint checkpoint,
        TieredStorageBindingInfo binding,
        string archivePath,
        string providerArchivePath,
        string fingerprint)
    {
        Checkpoint = checkpoint;
        Binding = binding;
        ArchivePath = archivePath;
        ProviderArchivePath = providerArchivePath;
        Fingerprint = fingerprint;
    }

    /// <summary>The externally persisted checkpoint revalidated by this plan.</summary>
    public TierArchiveRecoveryCheckpoint Checkpoint { get; }

    /// <summary>The exact root-scoped binding represented by this plan.</summary>
    public TieredStorageBindingInfo Binding { get; }

    /// <summary>The credential-redacted Provider-resolved active archive path.</summary>
    public string ArchivePath { get; }

    /// <summary>A fingerprint of the checkpoint, current binding, resolved generation, and exact file evidence.</summary>
    public string Fingerprint { get; }

    internal string ProviderArchivePath { get; }
}