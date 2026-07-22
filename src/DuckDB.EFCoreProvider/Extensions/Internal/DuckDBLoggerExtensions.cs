using DuckDB.EFCoreProvider.Diagnostics;
using DuckDB.EFCoreProvider.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public static class DuckDBLoggerExtensions
{
    internal static void OperationStarting<TCategory>(
        this IDiagnosticsLogger<TCategory> diagnostics,
        DbContext context,
        DuckDBProviderOperation operation,
        string operationName,
        string target)
        where TCategory : LoggerCategory<TCategory>, new()
    {
        var definition = GetStartingDefinition(diagnostics, operation);

        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics, operationName, target);
        }

        DispatchEventData(diagnostics, definition, context, operationName, target, null, null, null);
    }

    internal static void OperationCompleted<TCategory>(
        this IDiagnosticsLogger<TCategory> diagnostics,
        DbContext context,
        DuckDBProviderOperation operation,
        string operationName,
        string target,
        TimeSpan duration,
        long? rowsAffected = null)
        where TCategory : LoggerCategory<TCategory>, new()
    {
        var definition = GetCompletedDefinition(diagnostics, operation);
        var elapsedMilliseconds = duration.TotalMilliseconds;

        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics, operationName, target, elapsedMilliseconds, rowsAffected);
        }

        DispatchEventData(
            diagnostics,
            definition,
            context,
            operationName,
            target,
            duration,
            rowsAffected,
            null);
    }

    internal static void OperationFailed<TCategory>(
        this IDiagnosticsLogger<TCategory> diagnostics,
        DbContext context,
        DuckDBProviderOperation operation,
        string operationName,
        string target,
        TimeSpan duration,
        Exception exception)
        where TCategory : LoggerCategory<TCategory>, new()
    {
        var definition = GetFailedDefinition(diagnostics, operation);
        var elapsedMilliseconds = duration.TotalMilliseconds;

        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics, operationName, target, elapsedMilliseconds, exception);
        }

        DispatchEventData(
            diagnostics,
            definition,
            context,
            operationName,
            target,
            duration,
            null,
            exception);
    }

    private static EventDefinition<string, string> GetStartingDefinition<TCategory>(
        IDiagnosticsLogger<TCategory> diagnostics,
        DuckDBProviderOperation operation)
        where TCategory : LoggerCategory<TCategory>, new()
    {
        var definitions = (DuckDBLoggingDefinitions)diagnostics.Definitions;
        var index = (int)operation;
        return (EventDefinition<string, string>)(definitions.OperationStarting[index] ??=
            new EventDefinition<string, string>(
                diagnostics.Options,
                StartingEventId(operation),
                LogLevel.Information,
                EventIdCode(StartingEventId(operation)),
                level => LoggerMessage.Define<string, string>(
                    level,
                    StartingEventId(operation),
                    "Starting DuckDB operation '{Operation}' for '{Target}'.")));
    }

    private static EventDefinition<string, string, double, long?> GetCompletedDefinition<TCategory>(
        IDiagnosticsLogger<TCategory> diagnostics,
        DuckDBProviderOperation operation)
        where TCategory : LoggerCategory<TCategory>, new()
    {
        var definitions = (DuckDBLoggingDefinitions)diagnostics.Definitions;
        var index = (int)operation;
        return (EventDefinition<string, string, double, long?>)(definitions.OperationCompleted[index] ??=
            new EventDefinition<string, string, double, long?>(
                diagnostics.Options,
                CompletedEventId(operation),
                LogLevel.Information,
                EventIdCode(CompletedEventId(operation)),
                level => LoggerMessage.Define<string, string, double, long?>(
                    level,
                    CompletedEventId(operation),
                    "Completed DuckDB operation '{Operation}' for '{Target}' in {ElapsedMilliseconds} ms; affected rows: {RowsAffected}.")));
    }

    private static EventDefinition<string, string, double> GetFailedDefinition<TCategory>(
        IDiagnosticsLogger<TCategory> diagnostics,
        DuckDBProviderOperation operation)
        where TCategory : LoggerCategory<TCategory>, new()
    {
        var definitions = (DuckDBLoggingDefinitions)diagnostics.Definitions;
        var index = (int)operation;
        return (EventDefinition<string, string, double>)(definitions.OperationFailed[index] ??=
            new EventDefinition<string, string, double>(
                diagnostics.Options,
                FailedEventId(operation),
                LogLevel.Error,
                EventIdCode(FailedEventId(operation)),
                level => LoggerMessage.Define<string, string, double>(
                    level,
                    FailedEventId(operation),
                    "DuckDB operation '{Operation}' for '{Target}' failed after {ElapsedMilliseconds} ms.")));
    }

    private static void DispatchEventData<TCategory>(
        IDiagnosticsLogger<TCategory> diagnostics,
        EventDefinitionBase definition,
        DbContext context,
        string operation,
        string target,
        TimeSpan? duration,
        long? rowsAffected,
        Exception? exception)
        where TCategory : LoggerCategory<TCategory>, new()
    {
        if (!diagnostics.NeedsEventData(definition, out var diagnosticSourceEnabled, out var simpleLogEnabled))
        {
            return;
        }

        var eventData = new DuckDBOperationEventData(
            definition,
            GenerateMessage,
            context,
            operation,
            target,
            duration,
            rowsAffected,
            exception);

        diagnostics.DispatchEventData(definition, eventData, diagnosticSourceEnabled, simpleLogEnabled);
    }

    private static string GenerateMessage(EventDefinitionBase definition, EventData payload)
    {
        var operation = (DuckDBOperationEventData)payload;
        if (operation.Exception is not null)
        {
            return ((EventDefinition<string, string, double>)definition).GenerateMessage(
                operation.Operation,
                operation.Target,
                operation.Duration?.TotalMilliseconds ?? 0,
                operation.Exception);
        }

        if (operation.Duration is not null)
        {
            return ((EventDefinition<string, string, double, long?>)definition).GenerateMessage(
                operation.Operation,
                operation.Target,
                operation.Duration.Value.TotalMilliseconds,
                operation.RowsAffected);
        }

        return ((EventDefinition<string, string>)definition).GenerateMessage(operation.Operation, operation.Target);
    }

    private static EventId StartingEventId(DuckDBProviderOperation operation)
        => operation switch
        {
            DuckDBProviderOperation.BulkInsert => DuckDBEventId.BulkInsertStarting,
            DuckDBProviderOperation.Upsert => DuckDBEventId.UpsertStarting,
            DuckDBProviderOperation.ParquetExport => DuckDBEventId.ParquetExportStarting,
            DuckDBProviderOperation.TieredStorage => DuckDBEventId.TieredStorageOperationStarting,
            DuckDBProviderOperation.ExtensionLoad => DuckDBEventId.ExtensionLoadStarting,
            DuckDBProviderOperation.DuckLakeAttachment => DuckDBEventId.DuckLakeAttachmentStarting,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

    private static EventId CompletedEventId(DuckDBProviderOperation operation)
        => operation switch
        {
            DuckDBProviderOperation.BulkInsert => DuckDBEventId.BulkInsertCompleted,
            DuckDBProviderOperation.Upsert => DuckDBEventId.UpsertCompleted,
            DuckDBProviderOperation.ParquetExport => DuckDBEventId.ParquetExportCompleted,
            DuckDBProviderOperation.TieredStorage => DuckDBEventId.TieredStorageOperationCompleted,
            DuckDBProviderOperation.ExtensionLoad => DuckDBEventId.ExtensionLoadCompleted,
            DuckDBProviderOperation.DuckLakeAttachment => DuckDBEventId.DuckLakeAttachmentCompleted,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

    private static EventId FailedEventId(DuckDBProviderOperation operation)
        => operation switch
        {
            DuckDBProviderOperation.BulkInsert => DuckDBEventId.BulkInsertFailed,
            DuckDBProviderOperation.Upsert => DuckDBEventId.UpsertFailed,
            DuckDBProviderOperation.ParquetExport => DuckDBEventId.ParquetExportFailed,
            DuckDBProviderOperation.TieredStorage => DuckDBEventId.TieredStorageOperationFailed,
            DuckDBProviderOperation.ExtensionLoad => DuckDBEventId.ExtensionLoadFailed,
            DuckDBProviderOperation.DuckLakeAttachment => DuckDBEventId.DuckLakeAttachmentFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

    private static string EventIdCode(EventId eventId)
    {
        var name = eventId.Name ?? throw new InvalidOperationException("DuckDB diagnostic event IDs must have a name.");
        return $"{nameof(DuckDBEventId)}.{name[(name.LastIndexOf('.') + 1)..]}";
    }
}