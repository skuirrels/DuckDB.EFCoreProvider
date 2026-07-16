using System.Collections.ObjectModel;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>The amount of file-level evidence returned by a tiered-storage maintenance operation.</summary>
public enum TierManifestDetail
{
    /// <summary>Return counts and byte totals without individual file paths.</summary>
    Summary,

    /// <summary>Return a bounded representative set of file paths together with counts and byte totals.</summary>
    RepresentativeFiles,

    /// <summary>Return every file path. This preserves the behaviour of tiered-storage operations before 1.7.</summary>
    AllFiles,
}

/// <summary>Controls the file-level evidence returned by tiered-storage maintenance.</summary>
public sealed class TierManifestOptions
{
    /// <summary>The default manifest behaviour, which returns every file path.</summary>
    public static TierManifestOptions Default { get; } = new();

    /// <summary>The requested level of file detail.</summary>
    public TierManifestDetail Detail { get; init; } = TierManifestDetail.AllFiles;

    /// <summary>
    ///     The maximum number of representative paths returned per table when <see cref="Detail" /> is
    ///     <see cref="TierManifestDetail.RepresentativeFiles" />.
    /// </summary>
    public int MaxFilesPerNode { get; init; } = 25;

    internal void Validate()
    {
        if (!Enum.IsDefined(Detail))
        {
            throw new ArgumentOutOfRangeException(nameof(Detail), Detail, null);
        }

        if (MaxFilesPerNode <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxFilesPerNode),
                MaxFilesPerNode,
                "The representative-file limit must be greater than zero.");
        }
    }
}

/// <summary>Strongly typed DuckDB Parquet writer controls used by provider-owned immutable generations.</summary>
public sealed class TierParquetWriterOptions
{
    private static readonly HashSet<string> SupportedCompressions = new(StringComparer.OrdinalIgnoreCase)
    {
        "uncompressed",
        "snappy",
        "gzip",
        "zstd",
        "brotli",
        "lz4",
        "lz4_raw",
    };

    /// <summary>DuckDB's default Parquet writer settings.</summary>
    public static TierParquetWriterOptions Default { get; } = new();

    /// <summary>
    ///     Parquet compression codec: <c>uncompressed</c>, <c>snappy</c>, <c>gzip</c>, <c>zstd</c>,
    ///     <c>brotli</c>, <c>lz4</c>, or <c>lz4_raw</c>. <see langword="null" /> uses DuckDB's default.
    /// </summary>
    public string? Compression { get; init; }

    /// <summary>The zstd compression level from 1 through 22, or <see langword="null" /> for DuckDB's default.</summary>
    public int? CompressionLevel { get; init; }

    /// <summary>The target number of rows per Parquet row group.</summary>
    public long? RowGroupSize { get; init; }

    /// <summary>
    ///     A partitioned filename pattern containing at least one of <c>{i}</c>, <c>{uuid}</c>,
    ///     <c>{uuidv4}</c>, or <c>{uuidv7}</c>. <see langword="null" /> uses DuckDB's default.
    /// </summary>
    public string? FilenamePattern { get; init; }

    internal void Validate()
    {
        if (Compression is not null && !SupportedCompressions.Contains(Compression))
        {
            throw new ArgumentException(
                $"Unsupported Parquet compression '{Compression}'.",
                nameof(Compression));
        }

        if (CompressionLevel is < 1 or > 22)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CompressionLevel),
                CompressionLevel,
                "The zstd compression level must be between 1 and 22.");
        }

        if (CompressionLevel is not null
            && !string.Equals(Compression, "zstd", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "CompressionLevel is supported only when Compression is 'zstd'.",
                nameof(CompressionLevel));
        }

        if (RowGroupSize is < 2048)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RowGroupSize),
                RowGroupSize,
                "DuckDB requires a Parquet row-group size of at least 2,048 rows.");
        }

        if (FilenamePattern is not null
            && !FilenamePattern.Contains("{i}", StringComparison.Ordinal)
            && !FilenamePattern.Contains("{uuid}", StringComparison.Ordinal)
            && !FilenamePattern.Contains("{uuidv4}", StringComparison.Ordinal)
            && !FilenamePattern.Contains("{uuidv7}", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "FilenamePattern must contain {i}, {uuid}, {uuidv4}, or {uuidv7}.",
                nameof(FilenamePattern));
        }

        if (FilenamePattern is not null
            && (FilenamePattern.Contains('/')
                || FilenamePattern.Contains('\\')
                || FilenamePattern is "." or ".."))
        {
            throw new ArgumentException(
                "FilenamePattern must be a file name, not a path.",
                nameof(FilenamePattern));
        }
    }
}

/// <summary>Options shared by normal forward archive operations.</summary>
public sealed class TierArchiveOptions
{
    /// <summary>Parquet writer controls for files created by the operation.</summary>
    public TierParquetWriterOptions Writer { get; init; } = TierParquetWriterOptions.Default;

    /// <summary>Controls file evidence returned in the operation result.</summary>
    public TierManifestOptions Manifest { get; init; } = TierManifestOptions.Default;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Writer);
        ArgumentNullException.ThrowIfNull(Manifest);
        Writer.Validate();
        Manifest.Validate();
    }
}

/// <summary>
///     One exact identity expressed using the configured tier match-key property names for an EF Core entity.
/// </summary>
public sealed class TierRowIdentity
{
    /// <summary>Creates an identity for the specified mapped entity type.</summary>
    public TierRowIdentity(Type entityType, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("At least one match-key value is required.", nameof(values));
        }

        EntityType = entityType;
        Values = new ReadOnlyDictionary<string, object?>(
            values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    /// <summary>The EF Core entity CLR type whose configured match key is represented.</summary>
    public Type EntityType { get; }

    /// <summary>Configured match-key property names and their exact values.</summary>
    public IReadOnlyDictionary<string, object?> Values { get; }

    /// <summary>Creates an identity for <typeparamref name="TEntity" />.</summary>
    public static TierRowIdentity For<TEntity>(IReadOnlyDictionary<string, object?> values)
        where TEntity : class
        => new(typeof(TEntity), values);
}

/// <summary>The kind of explicit technical boundary applied to a tiered-storage maintenance operation.</summary>
public enum TierMaintenanceScopeKind
{
    /// <summary>The complete published cold range.</summary>
    All,

    /// <summary>One or more exact aggregate-root match keys.</summary>
    RootMatchKeys,

    /// <summary>Exact values for a leading prefix of the declared root partition plan.</summary>
    PartitionValues,
}

/// <summary>
///     A generic maintenance boundary expressed through configured root match keys or declared partition values.
/// </summary>
public sealed class TierMaintenanceScope
{
    private TierMaintenanceScope(
        TierMaintenanceScopeKind kind,
        IReadOnlyList<TierRowIdentity> rootIdentities,
        IReadOnlyDictionary<string, object?> partitionValues)
    {
        Kind = kind;
        RootIdentities = rootIdentities;
        PartitionValues = partitionValues;
    }

    /// <summary>The complete published cold range.</summary>
    public static TierMaintenanceScope All { get; } = new(
        TierMaintenanceScopeKind.All,
        [],
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>()));

    /// <summary>The selected boundary kind.</summary>
    public TierMaintenanceScopeKind Kind { get; }

    /// <summary>Exact aggregate-root identities when <see cref="Kind" /> is <see cref="TierMaintenanceScopeKind.RootMatchKeys" />.</summary>
    public IReadOnlyList<TierRowIdentity> RootIdentities { get; }

    /// <summary>Declared partition property values when <see cref="Kind" /> is <see cref="TierMaintenanceScopeKind.PartitionValues" />.</summary>
    public IReadOnlyDictionary<string, object?> PartitionValues { get; }

    /// <summary>Creates a scope from exact aggregate-root match keys.</summary>
    public static TierMaintenanceScope ForRootMatchKeys(params TierRowIdentity[] identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        if (identities.Length == 0)
        {
            throw new ArgumentException("At least one root identity is required.", nameof(identities));
        }

        return new TierMaintenanceScope(
            TierMaintenanceScopeKind.RootMatchKeys,
            Array.AsReadOnly(identities.ToArray()),
            new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>()));
    }

    /// <summary>Creates a scope from a leading prefix of exact declared root partition values.</summary>
    public static TierMaintenanceScope ForPartitionValues(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("At least one partition value is required.", nameof(values));
        }

        return new TierMaintenanceScope(
            TierMaintenanceScopeKind.PartitionValues,
            [],
            new ReadOnlyDictionary<string, object?>(
                values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
    }
}

/// <summary>Options for immutable-generation reconciliation.</summary>
public sealed class TierReconciliationOptions
{
    internal bool OmitScopeFromCold { get; init; }

    internal bool ForceRewrite { get; init; }

    internal bool UseExistingTransaction { get; init; }

    /// <summary>The exact technical boundary whose hot representations may replace cold rows.</summary>
    public TierMaintenanceScope Scope { get; init; } = TierMaintenanceScope.All;

    /// <summary>
    ///     Caller-authorised root or child identities to omit from the replacement generation. The provider
    ///     validates identities against configured match keys and never infers deletion from an absent collection.
    /// </summary>
    public IReadOnlyList<TierRowIdentity> Tombstones { get; init; } = [];

    /// <summary>Parquet writer controls for the replacement generation.</summary>
    public TierParquetWriterOptions Writer { get; init; } = TierParquetWriterOptions.Default;

    /// <summary>Controls file evidence returned in the operation result.</summary>
    public TierManifestOptions Manifest { get; init; } = TierManifestOptions.Default;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Scope);
        ArgumentNullException.ThrowIfNull(Tombstones);
        ArgumentNullException.ThrowIfNull(Writer);
        ArgumentNullException.ThrowIfNull(Manifest);
        Writer.Validate();
        Manifest.Validate();
    }
}

/// <summary>Options for a complete immutable-generation Parquet compaction.</summary>
public sealed class TierCompactionOptions
{
    /// <summary>Parquet writer controls used for the compacted replacement generation.</summary>
    public TierParquetWriterOptions Writer { get; init; } = TierParquetWriterOptions.Default;

    /// <summary>Controls file evidence returned in the operation result.</summary>
    public TierManifestOptions Manifest { get; init; } = TierManifestOptions.Default;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Writer);
        ArgumentNullException.ThrowIfNull(Manifest);
        Writer.Validate();
        Manifest.Validate();
    }
}

/// <summary>Options for moving an exact cold selection back to mapped hot tables.</summary>
public sealed class TierRestoreOptions
{
    /// <summary>The exact root match-key or declared-partition boundary to restore.</summary>
    public required TierMaintenanceScope Scope { get; init; }

    /// <summary>Parquet writer controls for the replacement cold generation.</summary>
    public TierParquetWriterOptions Writer { get; init; } = TierParquetWriterOptions.Default;

    /// <summary>Controls file evidence returned in the operation result.</summary>
    public TierManifestOptions Manifest { get; init; } = TierManifestOptions.Default;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Scope);
        if (Scope.Kind == TierMaintenanceScopeKind.All)
        {
            throw new ArgumentException("Restoration requires an exact match-key or partition boundary.", nameof(Scope));
        }

        ArgumentNullException.ThrowIfNull(Writer);
        ArgumentNullException.ThrowIfNull(Manifest);
        Writer.Validate();
        Manifest.Validate();
    }
}
