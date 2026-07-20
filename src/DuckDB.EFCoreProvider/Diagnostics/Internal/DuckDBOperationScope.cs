using DuckDB.EFCoreProvider.Extensions.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Diagnostics;

namespace DuckDB.EFCoreProvider.Diagnostics.Internal;

internal readonly struct DuckDBOperationScope<TCategory>
    where TCategory : LoggerCategory<TCategory>, new()
{
    private readonly IDiagnosticsLogger<TCategory> _diagnostics;
    private readonly DbContext _context;
    private readonly DuckDBProviderOperation _operation;
    private readonly string _operationName;
    private readonly string _target;
    private readonly long _startedAt;

    private DuckDBOperationScope(
        IDiagnosticsLogger<TCategory> diagnostics,
        DbContext context,
        DuckDBProviderOperation operation,
        string operationName,
        string target)
    {
        _diagnostics = diagnostics;
        _context = context;
        _operation = operation;
        _operationName = operationName;
        _target = target;
        _startedAt = Stopwatch.GetTimestamp();

        diagnostics.OperationStarting(context, operation, operationName, target);
    }

    internal static DuckDBOperationScope<TCategory> Start(
        IDiagnosticsLogger<TCategory> diagnostics,
        DbContext context,
        DuckDBProviderOperation operation,
        string operationName,
        string target)
        => new(diagnostics, context, operation, operationName, target);

    internal void Complete(long? rowsAffected = null)
        => _diagnostics.OperationCompleted(
            _context,
            _operation,
            _operationName,
            _target,
            Stopwatch.GetElapsedTime(_startedAt),
            rowsAffected);

    internal void Fail(Exception exception)
        => _diagnostics.OperationFailed(
            _context,
            _operation,
            _operationName,
            _target,
            Stopwatch.GetElapsedTime(_startedAt),
            exception);
}

internal static class DuckDBOperationDiagnostics
{
    internal static DuckDBOperationScope<DbLoggerCategory.Database.Command> StartCommand(
        DbContext context,
        DuckDBProviderOperation operation,
        string operationName,
        string target)
        => DuckDBOperationScope<DbLoggerCategory.Database.Command>.Start(
            context.GetService<IDiagnosticsLogger<DbLoggerCategory.Database.Command>>(),
            context,
            operation,
            operationName,
            target);
}