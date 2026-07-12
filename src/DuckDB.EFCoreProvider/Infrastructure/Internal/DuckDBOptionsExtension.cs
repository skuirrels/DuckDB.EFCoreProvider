using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Text;

namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBOptionsExtension : RelationalOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;
    private bool _loadSpatialite;
    private bool _bulkInsertBatching;
    private bool _bulkUpdateBatching;
    private bool _bulkDeleteBatching;
    private string? _memoryLimit;
    private string? _fileSearchPath;
    private TimeSpan? _migrationLockTimeout;
    private bool _migrationTableRebuilds;
    private IReadOnlyList<string> _extensionsToLoad = [];
    private Action<DuckDBConnection>? _connectionInitializer;

    public DuckDBOptionsExtension()
    {
    }

    protected DuckDBOptionsExtension(DuckDBOptionsExtension copyFrom)
        : base(copyFrom)
    {
        ReverseNullOrdering = copyFrom.ReverseNullOrdering;
        _loadSpatialite = copyFrom._loadSpatialite;
        _bulkInsertBatching = copyFrom._bulkInsertBatching;
        _bulkUpdateBatching = copyFrom._bulkUpdateBatching;
        _bulkDeleteBatching = copyFrom._bulkDeleteBatching;
        _memoryLimit = copyFrom._memoryLimit;
        _fileSearchPath = copyFrom._fileSearchPath;
        _migrationLockTimeout = copyFrom._migrationLockTimeout;
        _migrationTableRebuilds = copyFrom._migrationTableRebuilds;
        _extensionsToLoad = copyFrom._extensionsToLoad;
        _connectionInitializer = copyFrom._connectionInitializer;
    }

    /// <summary>
    /// <see langword="true"/> if reverse null ordering is enabled; otherwise, <see langword="false" />.
    /// </summary>
    public virtual bool ReverseNullOrdering { get; private set; }

    /// <summary>
    /// <see langword="true"/> if the DuckDB spatial extension should be loaded; otherwise, <see langword="false" />.
    /// </summary>
    public virtual bool LoadSpatialite => _loadSpatialite;

    /// <summary>
    ///     <see langword="true" /> if consecutive inserts within a <see cref="DbContext.SaveChanges()" /> batch
    ///     should be merged into a single multi-row <c>INSERT ... VALUES (..),(..)</c> statement; otherwise,
    ///     <see langword="false" />.
    /// </summary>
    /// <remarks>
    ///     When enabled, a run of inserts is executed as one statement, which is roughly an order of magnitude
    ///     faster on DuckDB but is <b>atomic</b>: if any row in the run fails, none of the rows in that run are
    ///     persisted, even when no enclosing transaction is used. Disabled by default to preserve EF Core's
    ///     standard per-row insert semantics.
    /// </remarks>
    public virtual bool BulkInsertBatching => _bulkInsertBatching;

    /// <summary>
    ///     <see langword="true" /> if consecutive updates within a <see cref="DbContext.SaveChanges()" /> batch
    ///     should be merged into a single <c>UPDATE ... FROM (VALUES (..),(..))</c> statement; otherwise,
    ///     <see langword="false" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When enabled, a run of eligible updates is executed as one statement, which is roughly an order of
    ///         magnitude faster on DuckDB but is <b>atomic</b>: if any row in the run fails, none of the rows in
    ///         that run are updated, even when no enclosing transaction is used.
    ///     </para>
    ///     <para>
    ///         Only updates whose <c>WHERE</c> clause is the primary key and which read no store-generated or
    ///         computed columns back are merged; updates with concurrency tokens or database-computed columns
    ///         fall back to the standard per-row path so their concurrency and value-propagation semantics are
    ///         preserved. Disabled by default.
    ///     </para>
    /// </remarks>
    public virtual bool BulkUpdateBatching => _bulkUpdateBatching;

    /// <summary>
    ///     <see langword="true" /> if consecutive deletes within a <see cref="DbContext.SaveChanges()" /> batch
    ///     should be merged into a single <c>DELETE ... WHERE key IN (..)</c> (or, for composite keys,
    ///     <c>DELETE ... USING (VALUES (..),(..))</c>) statement; otherwise, <see langword="false" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When enabled, a run of eligible deletes is executed as one statement, which is up to roughly
    ///         twenty times faster on DuckDB but is <b>atomic</b>: if the statement fails, none of the rows in
    ///         that run are deleted, even when no enclosing transaction is used.
    ///     </para>
    ///     <para>
    ///         Only deletes whose <c>WHERE</c> clause is the primary key are merged; deletes with concurrency
    ///         tokens fall back to the standard per-row path so their concurrency-detection semantics are
    ///         preserved. Disabled by default.
    ///     </para>
    /// </remarks>
    public virtual bool BulkDeleteBatching => _bulkDeleteBatching;

    /// <summary>
    ///     The value applied to DuckDB's <c>memory_limit</c> setting when a connection opens (for example
    ///     <c>"4GB"</c>), or <see langword="null" /> to leave DuckDB's default (80% of physical RAM) in place.
    /// </summary>
    public virtual string? MemoryLimit => _memoryLimit;

    /// <summary>
    ///     The value applied to DuckDB's <c>file_search_path</c> setting when a connection opens — one or more
    ///     comma-separated directories that relative file paths (for example in <c>read_parquet</c>) are
    ///     resolved against — or <see langword="null" /> to leave DuckDB's default in place.
    /// </summary>
    public virtual string? FileSearchPath => _fileSearchPath;

    /// <summary>
    ///     The maximum time to wait for the migrations lock (the <c>__EFMigrationsLock</c> table row) before
    ///     failing with a <see cref="TimeoutException" />, <see cref="Timeout.InfiniteTimeSpan" /> to wait
    ///     indefinitely, or <see langword="null" /> to use the default of five minutes.
    /// </summary>
    public virtual TimeSpan? MigrationLockTimeout => _migrationLockTimeout;

    /// <summary>
    ///     <see langword="true" /> when migration operations unsupported by DuckDB's in-place
    ///     <c>ALTER TABLE</c> surface may rebuild the affected table; otherwise, <see langword="false" />.
    /// </summary>
    public virtual bool MigrationTableRebuilds => _migrationTableRebuilds;

    /// <summary>DuckDB extensions installed and loaded whenever a provider-owned connection opens.</summary>
    public virtual IReadOnlyList<string> ExtensionsToLoad => _extensionsToLoad;

    /// <summary>An optional provider-owned connection initializer invoked after extensions are loaded.</summary>
    public virtual Action<DuckDBConnection>? ConnectionInitializer => _connectionInitializer;

    protected override RelationalOptionsExtension Clone()
    {
        return new DuckDBOptionsExtension(this);
    }

    public override void ApplyServices(IServiceCollection services)
    {
        services.AddEntityFrameworkDuckDB();
    }

    public virtual DuckDBOptionsExtension WithLoadSpatialite(bool loadSpatialite)
    {
        var clone = (DuckDBOptionsExtension)Clone();

        clone._loadSpatialite = loadSpatialite;

        return clone;
    }

    /// <summary>
    ///     Returns a copy of the current instance configured with the specified value.
    /// </summary>
    /// <param name="bulkInsertBatching"><see langword="true" /> to merge consecutive inserts into a single multi-row statement; otherwise, <see langword="false" />.</param>
    public virtual DuckDBOptionsExtension WithBulkInsertBatching(bool bulkInsertBatching)
    {
        var clone = (DuckDBOptionsExtension)Clone();

        clone._bulkInsertBatching = bulkInsertBatching;

        return clone;
    }

    /// <summary>
    ///     Returns a copy of the current instance configured with the specified value.
    /// </summary>
    /// <param name="bulkUpdateBatching"><see langword="true" /> to merge consecutive eligible updates into a single multi-row statement; otherwise, <see langword="false" />.</param>
    public virtual DuckDBOptionsExtension WithBulkUpdateBatching(bool bulkUpdateBatching)
    {
        var clone = (DuckDBOptionsExtension)Clone();

        clone._bulkUpdateBatching = bulkUpdateBatching;

        return clone;
    }

    /// <summary>
    ///     Returns a copy of the current instance configured with the specified value.
    /// </summary>
    /// <param name="bulkDeleteBatching"><see langword="true" /> to merge consecutive eligible deletes into a single statement; otherwise, <see langword="false" />.</param>
    public virtual DuckDBOptionsExtension WithBulkDeleteBatching(bool bulkDeleteBatching)
    {
        var clone = (DuckDBOptionsExtension)Clone();

        clone._bulkDeleteBatching = bulkDeleteBatching;

        return clone;
    }

    /// <summary>
    ///     Returns a copy of the current instance configured with the specified DuckDB <c>memory_limit</c>
    ///     value (for example <c>"4GB"</c>), or <see langword="null" /> to leave DuckDB's default in place.
    /// </summary>
    public virtual DuckDBOptionsExtension WithMemoryLimit(string? memoryLimit)
    {
        var clone = (DuckDBOptionsExtension)Clone();

        clone._memoryLimit = memoryLimit;

        return clone;
    }

    /// <summary>
    ///     Returns a copy of the current instance configured with the specified DuckDB <c>file_search_path</c>
    ///     value (one or more comma-separated directories), or <see langword="null" /> to leave the default.
    /// </summary>
    public virtual DuckDBOptionsExtension WithFileSearchPath(string? fileSearchPath)
    {
        var clone = (DuckDBOptionsExtension)Clone();

        clone._fileSearchPath = fileSearchPath;

        return clone;
    }

    /// <summary>
    ///     Returns a copy of the current instance configured with the specified migrations-lock wait limit,
    ///     <see cref="Timeout.InfiniteTimeSpan" /> to wait indefinitely, or <see langword="null" /> to use the
    ///     default of five minutes.
    /// </summary>
    public virtual DuckDBOptionsExtension WithMigrationLockTimeout(TimeSpan? migrationLockTimeout)
    {
        var clone = (DuckDBOptionsExtension)Clone();

        clone._migrationLockTimeout = migrationLockTimeout;

        return clone;
    }

    /// <summary>Returns a copy configured with the specified table-rebuild behaviour.</summary>
    public virtual DuckDBOptionsExtension WithMigrationTableRebuilds(bool migrationTableRebuilds)
    {
        var clone = (DuckDBOptionsExtension)Clone();
        clone._migrationTableRebuilds = migrationTableRebuilds;
        return clone;
    }

    /// <summary>Returns a copy that installs and loads the specified DuckDB extension.</summary>
    public virtual DuckDBOptionsExtension WithExtension(string extension)
    {
        var clone = (DuckDBOptionsExtension)Clone();
        clone._extensionsToLoad = _extensionsToLoad.Contains(extension, StringComparer.OrdinalIgnoreCase)
            ? _extensionsToLoad
            : [.. _extensionsToLoad, extension];
        return clone;
    }

    /// <summary>Returns a copy with an additional connection initializer.</summary>
    public virtual DuckDBOptionsExtension WithConnectionInitializer(Action<DuckDBConnection> initializer)
    {
        var clone = (DuckDBOptionsExtension)Clone();
        clone._connectionInitializer = _connectionInitializer + initializer;
        return clone;
    }

    /// <summary>
    ///     Returns a copy of the current instance configured with the specified value.
    /// </summary>
    /// <param name="reverseNullOrdering"><see langword="true"/> to enable reverse null ordering; otherwise, <see langword="false"/>.</param>
    internal virtual DuckDBOptionsExtension WithReverseNullOrdering(bool reverseNullOrdering)
    {
        var clone = (DuckDBOptionsExtension)Clone();

        clone.ReverseNullOrdering = reverseNullOrdering;

        return clone;
    }

    public override DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : RelationalExtensionInfo(extension)
    {
        private int? _serviceProviderHash;
        private string? _logFragment;

        private new DuckDBOptionsExtension Extension
            => (DuckDBOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider
            => true;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is ExtensionInfo otherInfo
               && Extension.ReverseNullOrdering == otherInfo.Extension.ReverseNullOrdering
               && Extension.BulkInsertBatching == otherInfo.Extension.BulkInsertBatching
               && Extension.BulkUpdateBatching == otherInfo.Extension.BulkUpdateBatching
               && Extension.BulkDeleteBatching == otherInfo.Extension.BulkDeleteBatching;

        public override string LogFragment
        {
            get
            {
                if (_logFragment == null)
                {
                    var builder = new StringBuilder();

                    builder.Append(base.LogFragment);

                    if (Extension.ReverseNullOrdering)
                    {
                        builder.Append(nameof(Extension.ReverseNullOrdering)).Append(' ');
                    }

                    _logFragment = builder.ToString();
                }

                return _logFragment;
            }
        }

        public override int GetServiceProviderHashCode()
        {
            if (_serviceProviderHash == null)
            {
                var hashCode = new HashCode();

                hashCode.Add(Extension.ReverseNullOrdering);
                hashCode.Add(Extension.BulkInsertBatching);
                hashCode.Add(Extension.BulkUpdateBatching);
                hashCode.Add(Extension.BulkDeleteBatching);

                _serviceProviderHash = hashCode.ToHashCode();
            }

            return _serviceProviderHash.Value;
        }

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["DuckDB"] = "1";
            debugInfo["DuckDB.EFCoreProvider:" + nameof(ReverseNullOrdering)] = Extension.ReverseNullOrdering.GetHashCode()
                .ToString(CultureInfo.InvariantCulture);
            debugInfo["DuckDB.EFCoreProvider:" + nameof(BulkInsertBatching)] = Extension.BulkInsertBatching.GetHashCode()
                .ToString(CultureInfo.InvariantCulture);
            debugInfo["DuckDB.EFCoreProvider:" + nameof(BulkUpdateBatching)] = Extension.BulkUpdateBatching.GetHashCode()
                .ToString(CultureInfo.InvariantCulture);
            debugInfo["DuckDB.EFCoreProvider:" + nameof(BulkDeleteBatching)] = Extension.BulkDeleteBatching.GetHashCode()
                .ToString(CultureInfo.InvariantCulture);
        }
    }
}