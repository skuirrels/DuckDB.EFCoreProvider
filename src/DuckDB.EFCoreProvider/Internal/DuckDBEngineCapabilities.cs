using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DuckDB.EFCoreProvider.Internal;

/// <summary>
///     Immutable capability set derived once from the configured engine profile and the loaded native engine.
/// </summary>
internal sealed class DuckDBEngineCapabilities : IDuckDBEngineCapabilities
{
    private static readonly Lazy<IDuckDBEngineCapabilities> NativeCapabilities =
        new(() => new DuckDBEngineCapabilities(false, false));

    internal static IDuckDBEngineCapabilities Native => NativeCapabilities.Value;

    public DuckDBEngineCapabilities(IDuckLakeSingletonOptions duckLakeOptions, IDuckDBSingletonOptions duckDbOptions)
        : this(duckLakeOptions.IsDuckLake, duckDbOptions.IsQuack)
    {
    }

    internal static IDuckDBEngineCapabilities FromDuckLakeProfile(bool isDuckLake)
        => isDuckLake ? new DuckDBEngineCapabilities(true, false) : Native;

    internal static IDuckDBEngineCapabilities FromDuckLakeOptions(IDuckLakeSingletonOptions? options)
        => FromDuckLakeProfile(options?.IsDuckLake == true);

    internal static IDuckDBEngineCapabilities FromOptions(IDbContextOptions options)
    {
        var extension = options.FindExtension<DuckDBOptionsExtension>();
        return extension?.QuackOptions is not null
            ? new DuckDBEngineCapabilities(false, true)
            : FromDuckLakeProfile(extension?.DuckLakeOptions is not null);
    }

    internal DuckDBEngineCapabilities(bool isDuckLake, bool isQuack)
        : this(isDuckLake, isQuack, DuckDBNativeEngine.Version)
    {
    }

    internal DuckDBEngineCapabilities(bool isDuckLake, bool isQuack, Version engineVersion)
    {
        ArgumentNullException.ThrowIfNull(engineVersion);

        // Quack replays commands on a remote server whose engine version is not the local library's, so the
        // version-dependent schema capabilities stay at the baseline for it. DuckLake has no constraints,
        // defaults or sequences, so they never apply there either.
        var isLocalNativeEngine = !isDuckLake && !isQuack;
        var isCyanoptera = isLocalNativeEngine && engineVersion >= DuckDBNativeEngine.Cyanoptera;

        SupportsReturning = !isDuckLake;
        SupportsStoreGeneratedValues = !isDuckLake;
        SupportsReturningOnReferencedTableUpdates = false;
        SupportsReferencedTableForeignKeyUpdates = isDuckLake;
        SupportsSaveChangesBatching = !isDuckLake;
        SupportsSequences = !isDuckLake;
        SupportsGeneratedColumns = !isDuckLake;
        SupportsSqlDefaultExpressions = !isDuckLake;
        SupportsIndexes = !isDuckLake;
        SupportsSchemaConstraints = !isDuckLake;
        SupportsTieredStorage = !isDuckLake && !isQuack;
        SupportsEfMigrations = !isDuckLake && !isQuack;
        SupportsSchemaManagement = true;
        SupportsDatabaseDeletion = !isDuckLake && !isQuack;
        SupportsRemoteCommandExecution = isQuack;
        SupportsRemoteBulkInsert = isQuack;
        SupportsMultipleStatementsPerCommand = !isQuack;
        SupportsAddColumnConstraints = isCyanoptera;
        SupportsAddUniqueConstraint = isCyanoptera;
        SupportsSequenceSetValue = isCyanoptera;
        UpsertStrategy = isDuckLake
            ? DuckDBUpsertStrategy.Merge
            : DuckDBUpsertStrategy.InsertOnConflict;
    }

    public bool SupportsReturning { get; }

    public bool SupportsStoreGeneratedValues { get; }

    public bool SupportsReturningOnReferencedTableUpdates { get; }

    public bool SupportsReferencedTableForeignKeyUpdates { get; }

    public bool SupportsSaveChangesBatching { get; }

    public bool SupportsSequences { get; }

    public bool SupportsGeneratedColumns { get; }

    public bool SupportsSqlDefaultExpressions { get; }

    public bool SupportsIndexes { get; }

    public bool SupportsSchemaConstraints { get; }

    public bool SupportsTieredStorage { get; }

    public bool SupportsEfMigrations { get; }

    public bool SupportsSchemaManagement { get; }

    public bool SupportsDatabaseDeletion { get; }

    public bool SupportsRemoteCommandExecution { get; }

    public bool SupportsRemoteBulkInsert { get; }

    public bool SupportsMultipleStatementsPerCommand { get; }

    public bool SupportsAddColumnConstraints { get; }

    public bool SupportsAddUniqueConstraint { get; }

    public bool SupportsSequenceSetValue { get; }

    public DuckDBUpsertStrategy UpsertStrategy { get; }
}
