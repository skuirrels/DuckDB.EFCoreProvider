using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Infrastructure;

/// <summary>
///     Allows DuckDB specific configuration to be performed on <see cref="DbContextOptions" />.
/// </summary>
/// <remarks>
///     <para>
///         Instances of this class are returned from a call to
///         <see cref="DuckDBDbContextOptionsBuilderExtensions.UseDuckDB(DbContextOptionsBuilder, string, System.Action{DuckDBDbContextOptionsBuilder})" />
///         and it is not designed to be directly constructed in your application code.
///     </para>
///     <para>
///         See <see href="https://aka.ms/efcore-docs-dbcontext-options">Using DbContextOptions</see>.
///     </para>
/// </remarks>
public class DuckDBDbContextOptionsBuilder : RelationalDbContextOptionsBuilder<DuckDBDbContextOptionsBuilder, DuckDBOptionsExtension>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DuckDBDbContextOptionsBuilder" /> class.
    /// </summary>
    /// <param name="optionsBuilder">The core options builder being wrapped.</param>
    public DuckDBDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
        : base(optionsBuilder)
    {
    }

    /// <summary>
    ///     Appends NULLS FIRST to all ORDER BY clauses. This is important for the tests which were written
    ///     for SQL Server. Note that to fully implement null-first ordering indexes also need to be generated
    ///     accordingly, and since this isn't done this feature isn't publicly exposed.
    /// </summary>
    /// <param name="reverseNullOrdering">True to enable reverse null ordering; otherwise, false.</param>
    internal virtual DuckDBDbContextOptionsBuilder ReverseNullOrdering(bool reverseNullOrdering = true)
        => WithOption(e => e.WithReverseNullOrdering(reverseNullOrdering));

    /// <summary>
    ///     Merges consecutive inserts within a <see cref="DbContext.SaveChanges()" /> batch into a single
    ///     multi-row <c>INSERT ... VALUES (..),(..)</c> statement. This is roughly an order of magnitude faster
    ///     than the default per-row insert path on DuckDB's columnar engine.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Disabled by default. When enabled, each run of consecutive inserts is executed as one statement
    ///         and is therefore <b>atomic</b>: if any row in the run violates a constraint, none of the rows in
    ///         that run are persisted — even when <see cref="DbContext.SaveChanges()" /> runs without an
    ///         enclosing transaction. This differs from EF Core's default behaviour, where a failed save without
    ///         a transaction may leave earlier rows committed.
    ///     </para>
    ///     <para>
    ///         For pure bulk loads that do not need change tracking or store-generated values, prefer the even
    ///         faster appender-based <c>BulkInsert</c> / <c>BulkInsertAsync</c> extension methods.
    ///     </para>
    /// </remarks>
    /// <param name="enable"><see langword="true" /> to enable insert batching; otherwise, <see langword="false" />.</param>
    public virtual DuckDBDbContextOptionsBuilder EnableBulkInsertBatching(bool enable = true)
        => WithOption(e => e.WithBulkInsertBatching(enable));

    /// <summary>
    ///     Merges consecutive eligible updates within a <see cref="DbContext.SaveChanges()" /> batch into a
    ///     single <c>UPDATE ... FROM (VALUES (..),(..))</c> statement. This is roughly an order of magnitude
    ///     faster than the default per-row update path on DuckDB's columnar engine.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Disabled by default. When enabled, each run of consecutive eligible updates is executed as one
    ///         statement and is therefore <b>atomic</b>: if any row in the run fails, none of the rows in that
    ///         run are updated — even when <see cref="DbContext.SaveChanges()" /> runs without an enclosing
    ///         transaction.
    ///     </para>
    ///     <para>
    ///         Only updates whose <c>WHERE</c> clause is the primary key and which do not read database-computed
    ///         or store-generated values back are merged. Updates that use concurrency tokens or have computed
    ///         columns fall back to the standard per-row path, preserving their concurrency-detection and
    ///         value-propagation behaviour.
    ///     </para>
    /// </remarks>
    /// <param name="enable"><see langword="true" /> to enable update batching; otherwise, <see langword="false" />.</param>
    public virtual DuckDBDbContextOptionsBuilder EnableBulkUpdateBatching(bool enable = true)
        => WithOption(e => e.WithBulkUpdateBatching(enable));

    /// <summary>
    ///     Merges consecutive eligible deletes within a <see cref="DbContext.SaveChanges()" /> batch into a
    ///     single <c>DELETE ... WHERE key IN (..)</c> statement (or, for composite keys,
    ///     <c>DELETE ... USING (VALUES (..),(..))</c>). This is up to roughly twenty times faster than the
    ///     default per-row delete path on DuckDB and is particularly effective for orphan cleanup and
    ///     child-collection replacement.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Disabled by default. When enabled, each run of consecutive eligible deletes is executed as one
    ///         statement and is therefore <b>atomic</b>: if the statement fails, none of the rows in that run
    ///         are deleted — even when <see cref="DbContext.SaveChanges()" /> runs without an enclosing
    ///         transaction.
    ///     </para>
    ///     <para>
    ///         Only deletes whose <c>WHERE</c> clause is the primary key are merged. Deletes that use
    ///         concurrency tokens fall back to the standard per-row path, preserving their concurrency-detection
    ///         behaviour.
    ///     </para>
    /// </remarks>
    /// <param name="enable"><see langword="true" /> to enable delete batching; otherwise, <see langword="false" />.</param>
    public virtual DuckDBDbContextOptionsBuilder EnableBulkDeleteBatching(bool enable = true)
        => WithOption(e => e.WithBulkDeleteBatching(enable));

    /// <summary>
    ///     Sets DuckDB's <c>memory_limit</c> — the maximum memory the DuckDB buffer manager may use — applied
    ///     when a connection opens.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When not configured, DuckDB uses its default of 80% of physical RAM. Setting a value caps that,
    ///         which is useful when DuckDB shares a host with other services. DuckDB spills larger-than-memory
    ///         intermediates to its temp directory, so a lower limit trades memory for more disk spilling on
    ///         large analytical queries rather than failing.
    ///     </para>
    ///     <para>
    ///         The value uses DuckDB's size syntax, for example <c>"4GB"</c>, <c>"512MB"</c>, or <c>"75%"</c>.
    ///     </para>
    /// </remarks>
    /// <param name="memoryLimit">The memory limit (for example <c>"4GB"</c>).</param>
    public virtual DuckDBDbContextOptionsBuilder MemoryLimit(string memoryLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryLimit);

        return WithOption(e => e.WithMemoryLimit(memoryLimit));
    }

    /// <summary>
    ///     Sets DuckDB's <c>file_search_path</c> — one or more comma-separated directories that relative file
    ///     paths are resolved against — applied when a connection opens.
    /// </summary>
    /// <remarks>
    ///     Useful when querying file sources (for example <c>read_parquet</c> via <c>[FromParquet]</c>) with
    ///     relative paths: they are resolved against this base directory instead of the process working
    ///     directory. When not configured, DuckDB's default is left in place. Multiple directories may be
    ///     supplied comma-separated, for example <c>"/data,/data/archive"</c>.
    /// </remarks>
    /// <param name="fileSearchPath">The directory (or comma-separated directories) to search.</param>
    public virtual DuckDBDbContextOptionsBuilder FileSearchPath(string fileSearchPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileSearchPath);

        return WithOption(e => e.WithFileSearchPath(fileSearchPath));
    }

    /// <summary>
    ///     Sets the maximum time to wait for the migrations lock before failing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Migrations take a database-wide lock (a row in the <c>__EFMigrationsLock</c> table) so that two
    ///         processes cannot apply migrations concurrently. If a process crashes while holding the lock, the
    ///         row is left behind and later migrators would otherwise wait forever. After this timeout the wait
    ///         fails with a <see cref="TimeoutException" /> explaining how to clear a stale lock.
    ///     </para>
    ///     <para>
    ///         Defaults to five minutes when not configured. Pass <see cref="Timeout.InfiniteTimeSpan" /> to
    ///         wait indefinitely.
    ///     </para>
    /// </remarks>
    /// <param name="timeout">
    ///     The maximum wait time (must be positive), or <see cref="Timeout.InfiniteTimeSpan" />.
    /// </param>
    public virtual DuckDBDbContextOptionsBuilder MigrationLockTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The migration lock timeout must be positive, or Timeout.InfiniteTimeSpan to wait indefinitely.");
        }

        return WithOption(e => e.WithMigrationLockTimeout(timeout));
    }
}
