using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DuckDB.EFCoreProvider.Diagnostics;

/// <summary>
///     Event IDs for DuckDB provider operations that are logged through Entity Framework Core diagnostics.
/// </summary>
/// <remarks>
///     These IDs can be used with <see cref="WarningsConfigurationBuilder" />, <c>LogTo</c>,
///     <see cref="DiagnosticSource" />, and standard <see cref="ILogger" /> filtering.
/// </remarks>
public static class DuckDBEventId
{
    // Values are part of the public diagnostics contract. Append only; never reorder existing members.
    private enum Id
    {
        BulkInsertStarting = CoreEventId.ProviderBaseId,
        BulkInsertCompleted,
        BulkInsertFailed,
        UpsertStarting,
        UpsertCompleted,
        UpsertFailed,
        ParquetExportStarting,
        ParquetExportCompleted,
        ParquetExportFailed,
        TieredStorageOperationStarting,
        TieredStorageOperationCompleted,
        TieredStorageOperationFailed,
        QuackDiagnosticsStarting,
        QuackDiagnosticsCompleted,
        QuackDiagnosticsFailed,

        ExtensionLoadStarting = CoreEventId.ProviderBaseId + 100,
        ExtensionLoadCompleted,
        ExtensionLoadFailed,
        DuckLakeAttachmentStarting,
        DuckLakeAttachmentCompleted,
        DuckLakeAttachmentFailed,
        QuackServerStarting,
        QuackServerCompleted,
        QuackServerFailed,
        EncryptedDatabaseAttachmentStarting,
        EncryptedDatabaseAttachmentCompleted,
        EncryptedDatabaseAttachmentFailed,

        SaveChangesBatch = CoreEventId.ProviderBaseId + 200,
    }

    private static readonly string CommandPrefix = DbLoggerCategory.Database.Command.Name + ".";
    private static readonly string InfrastructurePrefix = DbLoggerCategory.Infrastructure.Name + ".";

    private static EventId MakeCommandId(Id id)
        => new((int)id, CommandPrefix + id);

    private static EventId MakeInfrastructureId(Id id)
        => new((int)id, InfrastructurePrefix + id);

    /// <summary>A raw bulk-insert operation is starting.</summary>
    public static readonly EventId BulkInsertStarting = MakeCommandId(Id.BulkInsertStarting);

    /// <summary>A raw bulk-insert operation completed.</summary>
    public static readonly EventId BulkInsertCompleted = MakeCommandId(Id.BulkInsertCompleted);

    /// <summary>A raw bulk-insert operation failed.</summary>
    public static readonly EventId BulkInsertFailed = MakeCommandId(Id.BulkInsertFailed);

    /// <summary>A raw upsert operation is starting.</summary>
    public static readonly EventId UpsertStarting = MakeCommandId(Id.UpsertStarting);

    /// <summary>A raw upsert operation completed.</summary>
    public static readonly EventId UpsertCompleted = MakeCommandId(Id.UpsertCompleted);

    /// <summary>A raw upsert operation failed.</summary>
    public static readonly EventId UpsertFailed = MakeCommandId(Id.UpsertFailed);

    /// <summary>A Parquet export is starting.</summary>
    public static readonly EventId ParquetExportStarting = MakeCommandId(Id.ParquetExportStarting);

    /// <summary>A Parquet export completed.</summary>
    public static readonly EventId ParquetExportCompleted = MakeCommandId(Id.ParquetExportCompleted);

    /// <summary>A Parquet export failed.</summary>
    public static readonly EventId ParquetExportFailed = MakeCommandId(Id.ParquetExportFailed);

    /// <summary>A tiered-storage operation is starting.</summary>
    public static readonly EventId TieredStorageOperationStarting = MakeCommandId(Id.TieredStorageOperationStarting);

    /// <summary>A tiered-storage operation completed.</summary>
    public static readonly EventId TieredStorageOperationCompleted = MakeCommandId(Id.TieredStorageOperationCompleted);

    /// <summary>A tiered-storage operation failed.</summary>
    public static readonly EventId TieredStorageOperationFailed = MakeCommandId(Id.TieredStorageOperationFailed);

    /// <summary>A Quack health and diagnostics probe is starting.</summary>
    public static readonly EventId QuackDiagnosticsStarting = MakeCommandId(Id.QuackDiagnosticsStarting);

    /// <summary>A Quack health and diagnostics probe completed.</summary>
    public static readonly EventId QuackDiagnosticsCompleted = MakeCommandId(Id.QuackDiagnosticsCompleted);

    /// <summary>A Quack health and diagnostics probe failed.</summary>
    public static readonly EventId QuackDiagnosticsFailed = MakeCommandId(Id.QuackDiagnosticsFailed);

    /// <summary>A configured DuckDB extension is being loaded.</summary>
    public static readonly EventId ExtensionLoadStarting = MakeInfrastructureId(Id.ExtensionLoadStarting);

    /// <summary>A configured DuckDB extension was loaded.</summary>
    public static readonly EventId ExtensionLoadCompleted = MakeInfrastructureId(Id.ExtensionLoadCompleted);

    /// <summary>A configured DuckDB extension failed to load.</summary>
    public static readonly EventId ExtensionLoadFailed = MakeInfrastructureId(Id.ExtensionLoadFailed);

    /// <summary>A DuckLake catalog attachment is starting.</summary>
    public static readonly EventId DuckLakeAttachmentStarting = MakeInfrastructureId(Id.DuckLakeAttachmentStarting);

    /// <summary>A DuckLake catalog attachment completed.</summary>
    public static readonly EventId DuckLakeAttachmentCompleted = MakeInfrastructureId(Id.DuckLakeAttachmentCompleted);

    /// <summary>A DuckLake catalog attachment failed.</summary>
    public static readonly EventId DuckLakeAttachmentFailed = MakeInfrastructureId(Id.DuckLakeAttachmentFailed);

    /// <summary>A provider-managed Quack server operation is starting.</summary>
    public static readonly EventId QuackServerStarting = MakeInfrastructureId(Id.QuackServerStarting);

    /// <summary>A provider-managed Quack server operation completed.</summary>
    public static readonly EventId QuackServerCompleted = MakeInfrastructureId(Id.QuackServerCompleted);

    /// <summary>A provider-managed Quack server operation failed.</summary>
    public static readonly EventId QuackServerFailed = MakeInfrastructureId(Id.QuackServerFailed);

    /// <summary>An encrypted database attachment is starting.</summary>
    public static readonly EventId EncryptedDatabaseAttachmentStarting =
        MakeInfrastructureId(Id.EncryptedDatabaseAttachmentStarting);

    /// <summary>An encrypted database attachment completed.</summary>
    public static readonly EventId EncryptedDatabaseAttachmentCompleted =
        MakeInfrastructureId(Id.EncryptedDatabaseAttachmentCompleted);

    /// <summary>An encrypted database attachment failed.</summary>
    public static readonly EventId EncryptedDatabaseAttachmentFailed =
        MakeInfrastructureId(Id.EncryptedDatabaseAttachmentFailed);

    /// <summary>A SaveChanges batch was flushed or rejected by a provider batching guard.</summary>
    public static readonly EventId SaveChangesBatch = MakeCommandId(Id.SaveChangesBatch);
}