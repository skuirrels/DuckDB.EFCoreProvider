using DuckDB.EFCoreProvider.Diagnostics;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class ProviderOperationDiagnosticsTest
{
    private static readonly HashSet<EventId> ProviderOperationEventIds =
    [
        DuckDBEventId.BulkInsertStarting,
        DuckDBEventId.BulkInsertCompleted,
        DuckDBEventId.BulkInsertFailed,
        DuckDBEventId.UpsertStarting,
        DuckDBEventId.UpsertCompleted,
        DuckDBEventId.UpsertFailed,
        DuckDBEventId.ParquetExportStarting,
        DuckDBEventId.ParquetExportCompleted,
        DuckDBEventId.ParquetExportFailed,
        DuckDBEventId.TieredStorageOperationStarting,
        DuckDBEventId.TieredStorageOperationCompleted,
        DuckDBEventId.TieredStorageOperationFailed,
        DuckDBEventId.ExtensionLoadStarting,
        DuckDBEventId.ExtensionLoadCompleted,
        DuckDBEventId.ExtensionLoadFailed,
        DuckDBEventId.DuckLakeAttachmentStarting,
        DuckDBEventId.DuckLakeAttachmentCompleted,
        DuckDBEventId.DuckLakeAttachmentFailed,
    ];

    [Fact]
    public async Task Raw_and_tiered_operations_emit_structured_events_through_LogTo()
    {
        var databasePath = CreateTemporaryPath("diagnostics", ".duckdb");
        var parquetPath = CreateTemporaryPath("diagnostics-export", ".parquet");
        var events = new List<EventData>();

        try
        {
            await using var context = CreateContext(databasePath, events);
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
            events.Clear();

            var inserted = await context.BulkInsertAsync(
                new[]
                {
                    new DiagnosticEntity { Id = 1, Value = "one" },
                    new DiagnosticEntity { Id = 2, Value = "two" },
                });
            var upserted = await context.UpsertAsync(
                new[]
                {
                    new DiagnosticEntity { Id = 2, Value = "updated" },
                    new DiagnosticEntity { Id = 3, Value = "three" },
                });
            await context.Database.ExportToParquetAsync(context.Entities.OrderBy(entity => entity.Id), parquetPath);

            Assert.Equal(2, inserted);
            Assert.Equal(2, upserted);
            Assert.True(File.Exists(parquetPath));
            Assert.Equal(3, await context.Entities.CountAsync());

            Assert.Collection(
                events,
                eventData => AssertOperation(eventData, DuckDBEventId.BulkInsertStarting, nameof(DuckDBBulkExtensions.BulkInsert), null),
                eventData => AssertOperation(eventData, DuckDBEventId.BulkInsertCompleted, nameof(DuckDBBulkExtensions.BulkInsert), 2),
                eventData => AssertOperation(eventData, DuckDBEventId.UpsertStarting, nameof(DuckDBUpsertExtensions.Upsert), null),
                eventData => AssertOperation(eventData, DuckDBEventId.UpsertCompleted, nameof(DuckDBUpsertExtensions.Upsert), 2),
                eventData => AssertOperation(eventData, DuckDBEventId.ParquetExportStarting, "ParquetExport", null, parquetPath),
                eventData => AssertOperation(eventData, DuckDBEventId.ParquetExportCompleted, "ParquetExport", null, parquetPath));
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
            File.Delete(parquetPath);
        }
    }

    [Fact]
    public void Failed_raw_operation_emits_error_event_with_exception()
    {
        var databasePath = CreateTemporaryPath("diagnostics-failure", ".duckdb");
        var events = new List<EventData>();

        try
        {
            using var context = CreateContext(databasePath, events);
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
            context.Database.ExecuteSqlRaw("DROP TABLE diagnostic_entities;");
            events.Clear();

            var exception = Assert.Throws<InvalidOperationException>(
                () => context.BulkInsert(new[] { new DiagnosticEntity { Id = 1, Value = "one" } }));

            Assert.Collection(
                events,
                eventData => AssertOperation(eventData, DuckDBEventId.BulkInsertStarting, nameof(DuckDBBulkExtensions.BulkInsert), null),
                eventData =>
                {
                    var operation = AssertOperation(
                        eventData,
                        DuckDBEventId.BulkInsertFailed,
                        nameof(DuckDBBulkExtensions.BulkInsert),
                        null);
                    Assert.Same(exception, operation.Exception);
                    Assert.Equal(LogLevel.Error, operation.LogLevel);
                });
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public void Provider_events_are_dispatched_to_DiagnosticSource()
    {
        var databasePath = CreateTemporaryPath("diagnostic-source", ".duckdb");
        var eventData = new List<KeyValuePair<string, object?>>();

        try
        {
            using var context = CreateContext(databasePath, []);
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
            var diagnostics = context.GetService<IDiagnosticsLogger<DbLoggerCategory.Database.Command>>();
            var diagnosticSource = Assert.IsType<DiagnosticListener>(diagnostics.DiagnosticSource);
            using var subscription = diagnosticSource.Subscribe(
                new DiagnosticObserver(eventData),
                eventName => eventName == DuckDBEventId.BulkInsertCompleted.Name);

            context.BulkInsert(new[] { new DiagnosticEntity { Id = 1, Value = "one" } });

            var received = Assert.Single(
                eventData.Where(item => item.Key == DuckDBEventId.BulkInsertCompleted.Name));
            Assert.Equal(DuckDBEventId.BulkInsertCompleted.Name, received.Key);
            var operation = Assert.IsType<DuckDBOperationEventData>(received.Value);
            Assert.Equal(1, operation.RowsAffected);
            Assert.NotNull(operation.Duration);
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public void Failed_tiered_operation_emits_structured_failure_event()
    {
        var databasePath = CreateTemporaryPath("diagnostics-tiered", ".duckdb");
        var events = new List<EventData>();

        try
        {
            using var context = CreateContext(databasePath, events);
            context.Database.EnsureDeleted();
            context.Database.EnsureCreated();
            events.Clear();

            var exception = Assert.Throws<InvalidOperationException>(
                () => context.Database.PurgeArchiveOlderThan<DiagnosticEntity>(DateTime.UtcNow));

            Assert.Collection(
                events,
                eventData => AssertOperation(
                    eventData,
                    DuckDBEventId.TieredStorageOperationStarting,
                    "PurgeArchiveOlderThan",
                    null),
                eventData =>
                {
                    var operation = AssertOperation(
                        eventData,
                        DuckDBEventId.TieredStorageOperationFailed,
                        "PurgeArchiveOlderThan",
                        null);
                    Assert.Same(exception, operation.Exception);
                    Assert.Equal(LogLevel.Error, operation.LogLevel);
                });
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    [Fact]
    public void Failed_extension_load_emits_infrastructure_diagnostics()
    {
        const string extensionName = "missing_diagnostic_extension";
        var databasePath = CreateTemporaryPath("diagnostics-extension", ".duckdb");
        var events = new List<EventData>();

        try
        {
            var options = new DbContextOptionsBuilder<DiagnosticContext>()
                .UseDuckDB(
                    $"Data Source={databasePath}",
                    duckdb => duckdb.LoadExtension(extensionName, DuckDBExtensionLoadMode.LoadOnly))
                .LogTo((eventId, _) => ProviderOperationEventIds.Contains(eventId), events.Add)
                .Options;
            using var context = new DiagnosticContext(options);

            Assert.ThrowsAny<Exception>(() => context.Database.OpenConnection());

            Assert.Collection(
                events,
                eventData => AssertOperation(
                    eventData,
                    DuckDBEventId.ExtensionLoadStarting,
                    "ExtensionLoad",
                    null,
                    extensionName),
                eventData =>
                {
                    var operation = AssertOperation(
                        eventData,
                        DuckDBEventId.ExtensionLoadFailed,
                        "ExtensionLoad",
                        null,
                        extensionName);
                    Assert.NotNull(operation.Exception);
                    Assert.Equal(LogLevel.Error, operation.LogLevel);
                });
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    private static DiagnosticContext CreateContext(string databasePath, List<EventData> events)
    {
        var options = new DbContextOptionsBuilder<DiagnosticContext>()
            .UseDuckDB($"Data Source={databasePath}")
            .LogTo((eventId, _) => ProviderOperationEventIds.Contains(eventId), events.Add)
            .Options;
        return new DiagnosticContext(options);
    }

    private static DuckDBOperationEventData AssertOperation(
        EventData eventData,
        EventId eventId,
        string operationName,
        long? rowsAffected,
        string? target = null)
    {
        var operation = Assert.IsType<DuckDBOperationEventData>(eventData);
        Assert.Equal(eventId, operation.EventId);
        Assert.Equal(operationName, operation.Operation);
        Assert.Equal(rowsAffected, operation.RowsAffected);
        Assert.Equal(target ?? typeof(DiagnosticEntity).Name, operation.Target);

        if (eventId == DuckDBEventId.BulkInsertCompleted
            || eventId == DuckDBEventId.UpsertCompleted
            || eventId == DuckDBEventId.ParquetExportCompleted
            || eventId == DuckDBEventId.TieredStorageOperationCompleted
            || eventId == DuckDBEventId.TieredStorageOperationFailed
            || eventId == DuckDBEventId.BulkInsertFailed
            || eventId == DuckDBEventId.ExtensionLoadFailed)
        {
            Assert.NotNull(operation.Duration);
        }
        else
        {
            Assert.Null(operation.Duration);
        }

        return operation;
    }

    private static string CreateTemporaryPath(string prefix, string extension)
        => Path.Combine(Path.GetTempPath(), $"duckdb-efcore-{prefix}-{Guid.NewGuid():N}{extension}");

    private static void DeleteDatabaseFiles(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        var fileName = Path.GetFileName(databasePath);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(directory, fileName + "*"))
        {
            File.Delete(path);
        }
    }

    private sealed class DiagnosticObserver(List<KeyValuePair<string, object?>> events)
        : IObserver<KeyValuePair<string, object?>>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(KeyValuePair<string, object?> value)
            => events.Add(value);
    }

    private sealed class DiagnosticContext(DbContextOptions<DiagnosticContext> options) : DbContext(options)
    {
        public DbSet<DiagnosticEntity> Entities => Set<DiagnosticEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<DiagnosticEntity>(entity =>
            {
                entity.ToTable("diagnostic_entities");
                entity.HasKey(value => value.Id);
                entity.Property(value => value.Id).ValueGeneratedNever();
            });
    }

    private sealed class DiagnosticEntity
    {
        public int Id { get; set; }
        public required string Value { get; set; }
    }
}