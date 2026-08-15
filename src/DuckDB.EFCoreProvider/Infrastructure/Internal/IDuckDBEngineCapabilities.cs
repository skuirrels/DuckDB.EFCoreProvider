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

    DuckDBUpsertStrategy UpsertStrategy { get; }
}