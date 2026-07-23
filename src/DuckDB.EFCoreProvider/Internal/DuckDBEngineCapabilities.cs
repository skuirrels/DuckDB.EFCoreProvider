using DuckDB.EFCoreProvider.Infrastructure.Internal;

namespace DuckDB.EFCoreProvider.Internal;

/// <summary>
///     Immutable capability set derived once from the configured engine profile.
/// </summary>
internal sealed class DuckDBEngineCapabilities : IDuckDBEngineCapabilities
{
    public DuckDBEngineCapabilities(IDuckLakeSingletonOptions duckLakeOptions)
        : this(duckLakeOptions.IsDuckLake)
    {
    }

    internal DuckDBEngineCapabilities(bool isDuckLake)
    {
        SupportsReturning = !isDuckLake;
        SupportsSaveChangesBatching = !isDuckLake;
        SupportsSequences = !isDuckLake;
        SupportsGeneratedColumns = !isDuckLake;
        SupportsSqlDefaultExpressions = !isDuckLake;
        SupportsIndexes = !isDuckLake;
        SupportsSchemaConstraints = !isDuckLake;
        SupportsTieredStorage = !isDuckLake;
        SupportsEfMigrations = !isDuckLake;
        UpsertStrategy = isDuckLake
            ? DuckDBUpsertStrategy.Merge
            : DuckDBUpsertStrategy.InsertOnConflict;
    }

    public bool SupportsReturning { get; }

    public bool SupportsSaveChangesBatching { get; }

    public bool SupportsSequences { get; }

    public bool SupportsGeneratedColumns { get; }

    public bool SupportsSqlDefaultExpressions { get; }

    public bool SupportsIndexes { get; }

    public bool SupportsSchemaConstraints { get; }

    public bool SupportsTieredStorage { get; }

    public bool SupportsEfMigrations { get; }

    public DuckDBUpsertStrategy UpsertStrategy { get; }
}