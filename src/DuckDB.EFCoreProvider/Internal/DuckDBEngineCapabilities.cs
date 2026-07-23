using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Internal;

/// <summary>
///     Immutable capability set derived once from the configured engine profile.
/// </summary>
internal sealed class DuckDBEngineCapabilities : IDuckDBEngineCapabilities
{
    internal static IDuckDBEngineCapabilities Native { get; } = new DuckDBEngineCapabilities(false);

    public DuckDBEngineCapabilities(IDuckLakeSingletonOptions duckLakeOptions)
        : this(duckLakeOptions.IsDuckLake)
    {
    }

    internal static IDuckDBEngineCapabilities FromDuckLakeProfile(bool isDuckLake)
        => isDuckLake ? new DuckDBEngineCapabilities(true) : Native;

    internal static IDuckDBEngineCapabilities FromDuckLakeOptions(IDuckLakeSingletonOptions? options)
        => FromDuckLakeProfile(options?.IsDuckLake == true);

    internal static IDuckDBEngineCapabilities FromOptions(IDbContextOptions options)
        => FromDuckLakeProfile(options.FindExtension<DuckDBOptionsExtension>()?.DuckLakeOptions is not null);

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
