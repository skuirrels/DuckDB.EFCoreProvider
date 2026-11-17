namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

/// <summary>
///     Selects the set-based SQL strategy used by the provider's upsert fast path.
/// </summary>
public enum DuckDBUpsertStrategy
{
    InsertOnConflict,
    Merge
}

/// <summary>
///     Selects how EF Core migrators coordinate through the <c>__EFMigrationsLock</c> table.
/// </summary>
public enum DuckDBMigrationLockStrategy
{
    /// <summary>
    ///     Claim the lock by inserting a row under an enforced primary key with
    ///     <c>INSERT OR IGNORE ... RETURNING</c>, and release it by deleting the row.
    /// </summary>
    KeyedInsert,

    /// <summary>
    ///     Claim one pre-seeded lock row with a conditional <c>UPDATE</c> in its own committed transaction, and release
    ///     it with an <c>UPDATE</c> that matches the holder. Overlapping claims are serialised by the engine's optimistic
    ///     commit conflicts, so the lock needs neither an enforced key nor <c>RETURNING</c>.
    /// </summary>
    ConditionalUpdate
}

/// <summary>
///     Describes the SQL and schema capabilities of the configured DuckDB engine profile.
/// </summary>
/// <remarks>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </remarks>
public interface IDuckDBEngineCapabilities
{
    bool SupportsReturning { get; }

    /// <summary>Whether the configured profile can bind and read store-generated schema values.</summary>
    bool SupportsStoreGeneratedValues => SupportsReturning;

    /// <summary>
    ///     Indicates whether tracked updates to tables with inbound foreign keys can safely use
    ///     <c>UPDATE ... RETURNING</c>.
    /// </summary>
    bool SupportsReturningOnReferencedTableUpdates => false;

    /// <summary>
    ///     Indicates whether <c>UPDATE ... RETURNING</c> on a table with a primary key reports a write-write conflict
    ///     when an overlapping transaction changed the same row. DuckDB 1.5.6 does not: both transactions commit and
    ///     one write is lost. When this is <see langword="false" />, tracked updates use a plain <c>UPDATE</c>, which
    ///     does report the conflict, and read store-generated values back with a separate query.
    /// </summary>
    bool SupportsConflictDetectingReturningUpdates => false;

    /// <summary>
    ///     Indicates whether the engine can update outbound foreign-key columns on a table that is also
    ///     referenced by dependent rows.
    /// </summary>
    bool SupportsReferencedTableForeignKeyUpdates => !SupportsSchemaConstraints;

    bool SupportsSaveChangesBatching { get; }

    bool SupportsSequences { get; }

    bool SupportsGeneratedColumns { get; }

    bool SupportsSqlDefaultExpressions { get; }

    bool SupportsIndexes { get; }

    bool SupportsSchemaConstraints { get; }

    bool SupportsTieredStorage { get; }

    bool SupportsEfMigrations { get; }

    /// <summary>The strategy EF Core migrators use to hold the migrations lock.</summary>
    DuckDBMigrationLockStrategy MigrationLockStrategy
        => SupportsSchemaConstraints && SupportsReturning
            ? DuckDBMigrationLockStrategy.KeyedInsert
            : DuckDBMigrationLockStrategy.ConditionalUpdate;

    /// <summary>
    ///     Whether <c>ALTER COLUMN ... TYPE</c> can convert a column to any castable type in place. When
    ///     <see langword="false" />, only widening type promotions are applied in place; other type changes need a
    ///     table rebuild.
    /// </summary>
    bool SupportsColumnTypeConversions => true;

    /// <summary>Whether a table can move to another schema with <c>ALTER TABLE ... SET SCHEMA</c>.</summary>
    bool SupportsTableSchemaTransfer => true;

    /// <summary>
    ///     Whether <c>ALTER TABLE ... ADD COLUMN</c> accepts a typed default literal such as <c>true</c> or
    ///     <c>TIMESTAMP '...'</c>. When <see langword="false" />, an added column's default is rendered as an untyped
    ///     numeric or string literal that the engine casts to the column type. <c>CREATE TABLE</c> defaults are unaffected.
    /// </summary>
    bool SupportsTypedAddColumnDefaults => true;

    /// <summary>
    ///     Whether <c>SET NOT NULL</c> can be applied to a column added by <c>ALTER TABLE ... ADD COLUMN</c>. When
    ///     <see langword="false" />, a required column can only be introduced by creating the table, so adding one to an
    ///     existing table needs a table rebuild.
    /// </summary>
    bool SupportsSetNotNullOnAddedColumns => true;

    /// <summary>Whether EF can provision the configured database schema.</summary>
    bool SupportsSchemaManagement => true;

    /// <summary>Whether EF can delete the configured database.</summary>
    bool SupportsDatabaseDeletion => true;

    /// <summary>Whether commands are replayed against a remote DuckDB session.</summary>
    bool SupportsRemoteCommandExecution => false;

    /// <summary>Whether the remote transport supports typed chunk append.</summary>
    bool SupportsRemoteBulkInsert => false;

    /// <summary>Whether one provider command can expose result sets from multiple SQL statements.</summary>
    bool SupportsMultipleStatementsPerCommand => true;

    /// <summary>
    ///     Whether <c>BulkInsert</c> can reserve database-generated key values from column defaults, such as
    ///     sequences, and write them back to the entities before appending.
    /// </summary>
    bool SupportsBulkInsertKeyReservation => SupportsSqlDefaultExpressions;

    /// <summary>
    ///     Whether client-side Hi-Lo key generation can reserve blocks from a counter table on a connection
    ///     independent of the caller's transaction.
    /// </summary>
    bool SupportsHiLoKeyGeneration => !SupportsRemoteCommandExecution;

    DuckDBUpsertStrategy UpsertStrategy { get; }
}