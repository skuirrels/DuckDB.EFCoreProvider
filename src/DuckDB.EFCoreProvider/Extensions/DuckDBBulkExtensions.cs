using DuckDB.EFCoreProvider.Diagnostics.Internal;
using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Data;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     High-throughput bulk-insert helpers backed by the DuckDB.NET <c>Appender</c> API.
/// </summary>
/// <remarks>
///     <para>
///         These methods bypass the EF Core change tracker and update pipeline and append rows directly to
///         the underlying table via DuckDB's columnar appender. This is dramatically faster than
///         <see cref="DbContext.SaveChanges()" /> for loading large batches, but it is intentionally a
///         raw fast path:
///     </para>
///     <list type="bullet">
///         <item><description>no change tracking, concurrency checks, or EF command interceptors; provider lifecycle
///             diagnostics are emitted for the complete bulk operation;</description></item>
///         <item><description>no store-generated values — every mapped column must be given a value;</description></item>
///         <item><description>the target table must already exist;</description></item>
///         <item><description>EF column mappings and value converters are applied; shadow properties,
///             computed/generated columns, and unmapped columns are not supported.</description></item>
///     </list>
///     <para>
///         The per-call setup (resolving the physical column order and compiling the row writer) is
///         cached per entity type + table, so repeated bulk-insert calls avoid that fixed overhead.
///     </para>
/// </remarks>
public static class DuckDBBulkExtensions
{
    /// <summary>
    ///     Bulk-inserts the supplied entities into their mapped table using the DuckDB appender.
    /// </summary>
    /// <returns>The number of rows appended.</returns>
    public static int BulkInsert<TEntity>(this DbContext context, IEnumerable<TEntity> entities)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        var operation = DuckDBOperationDiagnostics.StartCommand(
            context,
            DuckDBProviderOperation.BulkInsert,
            nameof(BulkInsert),
            typeof(TEntity).Name);

        try
        {
            var (entityType, table, schema) = ResolveTarget(context, typeof(TEntity));
            var connection = (DuckDBConnection)context.Database.GetDbConnection();
            var openedHere = connection.State != ConnectionState.Open;

            if (openedHere)
            {
                context.Database.OpenConnection();
            }

            int count;
            try
            {
                var plan = DuckDBBulkInsertPlanner<TEntity>.GetOrCreate(connection, entityType, table, schema);
                count = Append(connection, plan, entities);
            }
            finally
            {
                if (openedHere)
                {
                    context.Database.CloseConnection();
                }
            }

            operation.Complete(count);
            return count;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>
    ///     Asynchronously bulk-inserts the supplied entities into their mapped table using the DuckDB appender.
    /// </summary>
    /// <returns>The number of rows appended.</returns>
    public static async Task<int> BulkInsertAsync<TEntity>(
        this DbContext context,
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        var operation = DuckDBOperationDiagnostics.StartCommand(
            context,
            DuckDBProviderOperation.BulkInsert,
            nameof(BulkInsert),
            typeof(TEntity).Name);

        try
        {
            var (entityType, table, schema) = ResolveTarget(context, typeof(TEntity));
            var connection = (DuckDBConnection)context.Database.GetDbConnection();
            var openedHere = connection.State != ConnectionState.Open;

            if (openedHere)
            {
                await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            int count;
            try
            {
                var plan = DuckDBBulkInsertPlanner<TEntity>.GetOrCreate(connection, entityType, table, schema);
                count = Append(connection, plan, entities);
            }
            finally
            {
                if (openedHere)
                {
                    await context.Database.CloseConnectionAsync().ConfigureAwait(false);
                }
            }

            operation.Complete(count);
            return count;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    private static int Append<TEntity>(
        DuckDBConnection connection,
        DuckDBBulkInsertPlan<TEntity> plan,
        IEnumerable<TEntity> entities)
        where TEntity : class
    {
        using var appender = connection.CreateAppender(plan.Schema, plan.Table);
        var count = 0;

        foreach (var entity in entities)
        {
            appender.AppendRow(entity, plan.WriteRow);
            count++;
        }

        return count;
    }

    private static (IEntityType EntityType, string Table, string Schema) ResolveTarget(DbContext context, Type clrType)
    {
        var entityType = context.Model.FindEntityType(clrType)
            ?? throw new InvalidOperationException($"'{clrType.Name}' is not part of the model.");

        var table = entityType.GetTableName()
            ?? throw new InvalidOperationException($"'{clrType.Name}' is not mapped to a table; bulk insert is not supported.");
        var schema = entityType.GetSchema() ?? "main";

        return (entityType, table, schema);
    }
}