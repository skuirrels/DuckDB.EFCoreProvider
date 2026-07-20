using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DuckDB.EFCoreProvider.Diagnostics;

/// <summary>Structured diagnostic data for a provider-owned DuckDB operation.</summary>
public sealed class DuckDBOperationEventData : DbContextEventData
{
    internal DuckDBOperationEventData(
        EventDefinitionBase eventDefinition,
        Func<EventDefinitionBase, EventData, string> messageGenerator,
        DbContext context,
        string operation,
        string target,
        TimeSpan? duration,
        long? rowsAffected,
        Exception? exception)
        : base(eventDefinition, messageGenerator, context)
    {
        Operation = operation;
        Target = target;
        Duration = duration;
        RowsAffected = rowsAffected;
        Exception = exception;
    }

    /// <summary>The provider operation being performed.</summary>
    public string Operation { get; }

    /// <summary>The non-secret entity, extension, catalog, or destination identifying the operation target.</summary>
    public string Target { get; }

    /// <summary>The elapsed operation time, when the event marks completion or failure.</summary>
    public TimeSpan? Duration { get; }

    /// <summary>The affected row count when the operation produces one.</summary>
    public long? RowsAffected { get; }

    /// <summary>The exception associated with a failed operation.</summary>
    public Exception? Exception { get; }
}