namespace DuckDB.EFCoreProvider.Infrastructure.Internal;

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

    bool SupportsSaveChangesBatching { get; }

    bool SupportsSequences { get; }

    bool SupportsGeneratedColumns { get; }

    bool SupportsSqlDefaultExpressions { get; }

    bool SupportsIndexes { get; }

    bool SupportsSchemaConstraints { get; }

    bool SupportsTieredStorage { get; }
}