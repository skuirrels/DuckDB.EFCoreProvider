using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     DuckDB specific extension methods for <see cref="DbContext.Database" />.
/// </summary>
public static class DuckDBDatabaseFacadeExtensions
{
    /// <summary>
    ///     Returns <see langword="true" /> if the database provider currently in use is the DuckDB provider.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This method can only be used after the <see cref="DbContext" /> has been configured because
    ///         it is only then that the provider is known. This means that this method cannot be used
    ///         in <see cref="DbContext.OnConfiguring" /> because this is where application code sets the
    ///         provider to use as part of configuring the context.
    ///     </para>
    /// </remarks>
    /// <param name="database">The facade from <see cref="DbContext.Database" />.</param>
    /// <returns><see langword="true" /> if DuckDB is being used; <see langword="false" /> otherwise.</returns>
    public static bool IsDuckDB(this DatabaseFacade database)
        => database.ProviderName == typeof(DuckDBOptionsExtension).Assembly.GetName().Name;

    /// <summary>Executes trusted raw SQL whose result shape is not known until execution.</summary>
    /// <remarks>
    ///     Use <c>{0}</c>, <c>{1}</c>, and so on for value parameters. The SQL text itself is not sanitized; never
    ///     concatenate untrusted input into it. The returned result must be disposed.
    /// </remarks>
    /// <param name="database">The database facade for the current context.</param>
    /// <param name="sql">The raw DuckDB SQL text.</param>
    /// <param name="cancellationToken">A token used to cancel command execution.</param>
    /// <returns>An owned streaming result with runtime column metadata.</returns>
    public static Task<DuckDBDynamicQueryResult> SqlQueryDynamicRawAsync(
        this DatabaseFacade database,
        string sql,
        CancellationToken cancellationToken = default)
        => ExecuteDynamicQueryAsync(database, sql, [], cancellationToken);

    /// <summary>Executes trusted parameterized raw SQL whose result shape is not known until execution.</summary>
    /// <remarks>
    ///     Use <c>{0}</c>, <c>{1}</c>, and so on for value parameters. The SQL text itself is not sanitized; never
    ///     concatenate untrusted input into it. The returned result must be disposed.
    /// </remarks>
    /// <param name="database">The database facade for the current context.</param>
    /// <param name="sql">The raw DuckDB SQL text containing composite-format parameter placeholders.</param>
    /// <param name="parameters">Values or provider parameters to bind to the SQL placeholders.</param>
    /// <param name="cancellationToken">A token used to cancel command execution.</param>
    /// <returns>An owned streaming result with runtime column metadata.</returns>
    public static Task<DuckDBDynamicQueryResult> SqlQueryDynamicRawAsync(
        this DatabaseFacade database,
        string sql,
        IReadOnlyList<object?> parameters,
        CancellationToken cancellationToken = default)
        => ExecuteDynamicQueryAsync(database, sql, parameters, cancellationToken);

    /// <summary>Executes interpolated SQL whose result shape is not known until execution.</summary>
    /// <remarks>Interpolated values are parameterized. The returned result must be disposed.</remarks>
    /// <param name="database">The database facade for the current context.</param>
    /// <param name="sql">The interpolated SQL and values to parameterize.</param>
    /// <param name="cancellationToken">A token used to cancel command execution.</param>
    /// <returns>An owned streaming result with runtime column metadata.</returns>
    public static Task<DuckDBDynamicQueryResult> SqlQueryDynamicAsync(
        this DatabaseFacade database,
        FormattableString sql,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sql);
        return ExecuteDynamicQueryAsync(database, sql.Format, sql.GetArguments(), cancellationToken);
    }

    private static async Task<DuckDBDynamicQueryResult> ExecuteDynamicQueryAsync(
        DatabaseFacade database,
        string sql,
        IReadOnlyList<object?> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);

        var context = database.GetService<ICurrentDbContext>().Context;
        var dependencies = database.GetService<IRelationalDatabaseFacadeDependencies>();
        var concurrencyDetector = database.GetService<IConcurrencyDetector>();
        RawSqlCommand command;

        if (parameters.Count == 0)
        {
            command = new RawSqlCommand(
                dependencies.RawSqlCommandBuilder.Build(sql),
                new Dictionary<string, object?>());
        }
        else
        {
            command = dependencies.RawSqlCommandBuilder.Build(sql, parameters, context.Model);
        }

        using var criticalSection = concurrencyDetector.EnterCriticalSection();
        var parameterObject = new RelationalCommandParameterObject(
            dependencies.RelationalConnection,
            command.ParameterValues,
            readerColumns: null,
            context,
            dependencies.CommandLogger,
            CommandSource.FromSqlQuery);

        var reader = await command.RelationalCommand
            .ExecuteReaderAsync(parameterObject, cancellationToken)
            .ConfigureAwait(false);

        return new DuckDBDynamicQueryResult(reader, concurrencyDetector);
    }
}