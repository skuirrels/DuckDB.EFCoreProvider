using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;
using System.Diagnostics.CodeAnalysis;

namespace DuckDB.EFCoreProvider.Update.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
[SuppressMessage("ReSharper", "ClassWithVirtualMembersNeverInherited.Global")]
public class DuckDBModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    /// <summary>
    ///     Default maximum number of commands per batch when the application does not configure
    ///     <c>MaxBatchSize</c>. Consecutive inserts within a batch are merged into a single multi-row
    ///     statement; ~100 was measured to be the throughput sweet spot for DuckDB (larger batches regress as
    ///     per-statement parameter-binding cost grows).
    /// </summary>
    private const int DefaultMaxBatchSize = 100;

    /// <summary>
    ///     Upper bound applied to a configured <c>MaxBatchSize</c> to keep merged statement size and parameter
    ///     counts within sane limits for DuckDB.
    /// </summary>
    private const int MaxMaxBatchSize = 1000;

    private readonly int _maxBatchSize;
    private readonly bool _bulkInsertBatching;
    private readonly bool _bulkUpdateBatching;
    private readonly bool _bulkDeleteBatching;
    private readonly bool _isDuckLake;

    public DuckDBModificationCommandBatchFactory(
        ModificationCommandBatchFactoryDependencies dependencies,
        IDbContextOptions options)
    {
        Dependencies = dependencies;

        var optionsExtension = options.Extensions.OfType<DuckDBOptionsExtension>().FirstOrDefault();
        _maxBatchSize = Math.Min(optionsExtension?.MaxBatchSize ?? DefaultMaxBatchSize, MaxMaxBatchSize);
        _bulkInsertBatching = optionsExtension?.BulkInsertBatching ?? false;
        _bulkUpdateBatching = optionsExtension?.BulkUpdateBatching ?? false;
        _bulkDeleteBatching = optionsExtension?.BulkDeleteBatching ?? false;
        _isDuckLake = optionsExtension?.DuckLakeOptions is not null;

        if (_maxBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _maxBatchSize,
                "MaxBatchSize must be a positive integer.");
        }
    }

    protected virtual ModificationCommandBatchFactoryDependencies Dependencies { get; }

    public ModificationCommandBatch Create()
    {
        if (_isDuckLake)
        {
            if (_bulkInsertBatching || _bulkUpdateBatching || _bulkDeleteBatching)
            {
                throw new NotSupportedException(
                    "SaveChanges batching is not supported by the DuckLake profile because DuckLake does not expose "
                    + "RETURNING result sets. Use the appender-based BulkInsert or the MERGE-based Upsert API instead.");
            }

            return new DuckLakeModificationCommandBatch(Dependencies);
        }

        // Insert/update/delete batching changes failure semantics to be atomic per merged run, so it is
        // opt-in. When none is enabled, fall back to EF Core's one-command-per-batch behaviour, preserving
        // standard semantics.
        return _bulkInsertBatching || _bulkUpdateBatching || _bulkDeleteBatching
            ? new DuckDBModificationCommandBatch(
                Dependencies, _maxBatchSize, _bulkInsertBatching, _bulkUpdateBatching, _bulkDeleteBatching)
            : new SingularModificationCommandBatch(Dependencies);
    }
}