using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Query.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data.Common;

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

    /// <summary>Executes trusted SQL with named ADO.NET parameters and a runtime-defined result shape.</summary>
    /// <remarks>
    ///     Parameter names may be supplied with or without DuckDB's <c>$</c> prefix. The SQL is passed through
    ///     unchanged, so literal braces are safe. Parameters are copied and are not mutated by the provider.
    ///     The returned result must be disposed.
    /// </remarks>
    public static Task<DuckDBDynamicQueryResult> SqlQueryDynamicCommandAsync(
        this DatabaseFacade database,
        string sql,
        IReadOnlyList<DbParameter> parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);
        return database.GetService<DuckDBDynamicCommandExecutor>()
            .ExecuteNamedAsync(sql, parameters, cancellationToken);
    }

    /// <summary>Executes a previously extracted provider command plan as a dynamic result.</summary>
    public static Task<DuckDBDynamicQueryResult> SqlQueryDynamicCommandAsync(
        this DatabaseFacade database,
        DuckDBCommandPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(plan);
        return database.GetService<DuckDBDynamicCommandExecutor>()
            .ExecutePlanAsync(plan, cancellationToken);
    }

    /// <summary>Extracts the single database command generated for a query without opening its connection.</summary>
    /// <remarks>
    ///     The plan describes the server command and parameters only; it does not represent EF's client-side
    ///     result shaper. Split queries and other multi-command query shapes are rejected.
    /// </remarks>
    public static DuckDBCommandPlan GetDuckDBCommandPlan<T>(
        this DatabaseFacade database,
        IQueryable<T> query)
    {
        ArgumentNullException.ThrowIfNull(database);
        return database.GetService<DuckDBCommandPlanFactory>().Create(query);
    }

    /// <summary>Extracts the database command generated for a terminal Count operation.</summary>
    public static DuckDBCommandPlan GetDuckDBCountCommandPlan<T>(
        this DatabaseFacade database,
        IQueryable<T> query)
    {
        ArgumentNullException.ThrowIfNull(database);
        return database.GetService<DuckDBCommandPlanFactory>().CreateCount(query);
    }

    /// <summary>Extracts the database command generated for a terminal Any operation.</summary>
    public static DuckDBCommandPlan GetDuckDBAnyCommandPlan<T>(
        this DatabaseFacade database,
        IQueryable<T> query)
    {
        ArgumentNullException.ThrowIfNull(database);
        return database.GetService<DuckDBCommandPlanFactory>().CreateAny(query);
    }

    /// <summary>Reports the provider's EF-property support for a DuckDB store type.</summary>
    public static DuckDBStoreTypeMappingInfo GetDuckDBStoreTypeMapping(
        this DatabaseFacade database,
        string storeType)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeType);
        return database.GetService<DuckDBStoreTypeInspector>().Inspect(storeType);
    }

    private static Task<DuckDBDynamicQueryResult> ExecuteDynamicQueryAsync(
        DatabaseFacade database,
        string sql,
        IReadOnlyList<object?> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);

        return database.GetService<DuckDBDynamicCommandExecutor>()
            .ExecuteRawAsync(sql, parameters, cancellationToken);
    }
}