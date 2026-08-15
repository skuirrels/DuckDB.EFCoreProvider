using DuckDB.EFCoreProvider.Diagnostics.Internal;
using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     High-throughput upsert helpers built on staged set-based native-DuckDB, DuckLake, or Quack commands.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Upsert{TEntity}" /> and the default <c>UpsertAsync</c> overload insert the supplied entities,
///         updating any rows whose primary key already exists. The alternate <c>UpsertAsync</c> overload can target
///         a primary key, alternate key, or unique index selected by the caller. Chunks are appended through one temporary staging
///         table per operation and then merged into the target table with a set-based
///         <c>INSERT ... ON CONFLICT</c> (native DuckDB) or <c>MERGE INTO</c> (DuckLake). This is roughly an order of magnitude faster than the usual
///         read-then-insert-or-update pattern because it removes the existence-check round-trip and batches
///         the writes.
///     </para>
///     <para>
///         The default batch size adapts to the insert shape, using the largest row count that stays within a
///         100,000 staged-cell budget. An explicit <c>batchSize</c> remains a maximum row count and can reduce
///         per-statement work when required.
///     </para>
///     <para>
///         Like <see cref="DuckDBBulkExtensions.BulkInsert{TEntity}" />, this is a raw fast path:
///     </para>
///     <list type="bullet">
///         <item><description>no change tracking, EF optimistic-concurrency checks, or EF command interceptors;
///             provider lifecycle diagnostics are emitted for the complete upsert operation;</description></item>
///         <item><description>the default conflict target is the entity's primary key, whose values must be supplied;
///             the alternate-target overload permits store-generated-on-add columns and does not populate their generated
///             values back into the supplied entities;</description></item>
///         <item><description>all staged non-key and non-conflict columns are overwritten from the supplied values;</description></item>
///         <item><description>callers own duplicate conflict-target handling within an input batch and can wrap the
///             operation in an explicit transaction when all batches must commit atomically;</description></item>
///         <item><description>DuckLake rejects the affected staged batch before mutation when a key matches multiple
///             existing rows because its logical keys are not physically enforced;</description></item>
///         <item><description>EF column mappings and value converters are applied; shadow properties and
///             database-computed columns are not supported.</description></item>
///     </list>
/// </remarks>
public static class DuckDBUpsertExtensions
{
    /// <summary>
    ///     Inserts the supplied entities, updating any whose primary key already exists, using appender-staged
    ///     batches and set-based native-DuckDB <c>INSERT ... ON CONFLICT</c> or DuckLake <c>MERGE INTO</c> statements.
    /// </summary>
    /// <returns>The number of rows processed.</returns>
    public static int Upsert<TEntity>(
        this DbContext context,
        IEnumerable<TEntity> entities,
        int batchSize = DuckDBUpsertBatching.DefaultRequestedBatchSize)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var operation = DuckDBOperationDiagnostics.StartCommand(
            context,
            DuckDBProviderOperation.Upsert,
            nameof(Upsert),
            typeof(TEntity).Name);

        try
        {
            var plan = DuckDBUpsertPlanner.GetOrCreate(context, typeof(TEntity));
            var sqlRenderer = new DuckDBUpsertSqlRenderer(context.GetService<ISqlGenerationHelper>());
            var dbConnection = context.Database.GetDbConnection();
            var openedHere = dbConnection.State != ConnectionState.Open;

            if (openedHere)
            {
                context.Database.OpenConnection();
            }

            if (dbConnection is QuackDbConnection
                {
                    EngineCapabilities.SupportsRemoteCommandExecution: true
                } quackConnection)
            {
                try
                {
                    var remoteCount = UpsertRemote(
                        quackConnection,
                        plan,
                        sqlRenderer,
                        entities,
                        batchSize);
                    operation.Complete(remoteCount);
                    return remoteCount;
                }
                finally
                {
                    if (openedHere)
                    {
                        context.Database.CloseConnection();
                    }
                }
            }

            var connection = dbConnection as DuckDBConnection
                ?? throw new NotSupportedException(
                    $"Upsert requires a DuckDB or Quack connection, but found '{dbConnection.GetType().Name}'.");

            var count = 0;
            string? tempTable = null;
            var upsertFailed = false;
            try
            {
                var effectiveBatchSize = EffectiveBatchSize(plan, batchSize);
                var batch = new List<TEntity>(InitialBatchCapacity(entities, effectiveBatchSize));
                foreach (var entity in entities)
                {
                    batch.Add(entity);
                    if (batch.Count == effectiveBatchSize)
                    {
                        tempTable ??= CreateTemporaryTable(connection, plan, sqlRenderer);
                        UpsertBatch(connection, plan, sqlRenderer, tempTable, batch);
                        count += batch.Count;
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    tempTable ??= CreateTemporaryTable(connection, plan, sqlRenderer);
                    UpsertBatch(connection, plan, sqlRenderer, tempTable, batch);
                    count += batch.Count;
                }
            }
            catch
            {
                upsertFailed = true;
                throw;
            }
            finally
            {
                try
                {
                    if (tempTable is not null)
                    {
                        DropTemporaryTable(connection, sqlRenderer, tempTable, suppressFailure: upsertFailed);
                    }
                }
                finally
                {
                    if (openedHere)
                    {
                        context.Database.CloseConnection();
                    }
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
    ///     Asynchronously inserts the supplied entities, updating any whose primary key already exists, using
    ///     appender-staged batches and set-based native-DuckDB <c>INSERT ... ON CONFLICT</c> or DuckLake <c>MERGE INTO</c> statements.
    /// </summary>
    /// <returns>The number of rows processed.</returns>
    public static Task<int> UpsertAsync<TEntity>(
        this DbContext context,
        IEnumerable<TEntity> entities,
        int batchSize = DuckDBUpsertBatching.DefaultRequestedBatchSize,
        CancellationToken cancellationToken = default)
        where TEntity : class
        => UpsertAsyncCore(
            context,
            entities,
            conflictPropertyNames: null,
            batchSize,
            cancellationToken);

    /// <summary>
    ///     Asynchronously inserts the supplied entities, updating rows that match the selected primary key,
    ///     alternate key, or unique index. Store-generated-on-add columns are omitted when the selector is not
    ///     the primary key, allowing DuckDB defaults such as sequences to generate their values for inserted rows.
    /// </summary>
    /// <remarks>
    ///     This raw fast path does not populate store-generated values back into the supplied entities. The
    ///     selector must contain direct property accesses, for example <c>entity =&gt; entity.ExternalId</c> or
    ///     <c>entity =&gt; new { entity.ParentId, entity.Sequence }</c>. Callers should resolve duplicate selected-key
    ///     values before invoking this method. DuckLake rejects the current batch before mutating it when a staged key
    ///     matches multiple existing rows, but callers remain responsible for preventing concurrent duplicate inserts.
    /// </remarks>
    /// <returns>The number of rows processed.</returns>
    public static Task<int> UpsertAsync<TEntity>(
        this DbContext context,
        IEnumerable<TEntity> entities,
        Expression<Func<TEntity, object?>> conflictTarget,
        int batchSize = DuckDBUpsertBatching.DefaultRequestedBatchSize,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(conflictTarget);
        var conflictPropertyNames = DuckDBPropertySelector.GetPropertyNames(
            conflictTarget,
            "conflict-target",
            nameof(conflictTarget));

        return UpsertAsyncCore(context, entities, conflictPropertyNames, batchSize, cancellationToken);
    }

    private static async Task<int> UpsertAsyncCore<TEntity>(
        DbContext context,
        IEnumerable<TEntity> entities,
        IReadOnlyList<string>? conflictPropertyNames,
        int batchSize,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var operation = DuckDBOperationDiagnostics.StartCommand(
            context,
            DuckDBProviderOperation.Upsert,
            nameof(Upsert),
            typeof(TEntity).Name);

        try
        {
            var plan = DuckDBUpsertPlanner.GetOrCreate(context, typeof(TEntity), conflictPropertyNames);
            var sqlRenderer = new DuckDBUpsertSqlRenderer(context.GetService<ISqlGenerationHelper>());
            var dbConnection = context.Database.GetDbConnection();
            var openedHere = dbConnection.State != ConnectionState.Open;

            if (openedHere)
            {
                await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            if (dbConnection is QuackDbConnection
                {
                    EngineCapabilities.SupportsRemoteCommandExecution: true
                } quackConnection)
            {
                try
                {
                    var remoteCount = await UpsertRemoteAsync(
                        quackConnection,
                        plan,
                        sqlRenderer,
                        entities,
                        batchSize,
                        cancellationToken).ConfigureAwait(false);
                    operation.Complete(remoteCount);
                    return remoteCount;
                }
                finally
                {
                    if (openedHere)
                    {
                        await context.Database.CloseConnectionAsync().ConfigureAwait(false);
                    }
                }
            }

            var connection = dbConnection as DuckDBConnection
                ?? throw new NotSupportedException(
                    $"Upsert requires a DuckDB or Quack connection, but found '{dbConnection.GetType().Name}'.");

            var count = 0;
            string? tempTable = null;
            var upsertFailed = false;
            try
            {
                var effectiveBatchSize = EffectiveBatchSize(plan, batchSize);
                var batch = new List<TEntity>(InitialBatchCapacity(entities, effectiveBatchSize));
                foreach (var entity in entities)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    batch.Add(entity);
                    if (batch.Count == effectiveBatchSize)
                    {
                        tempTable ??= CreateTemporaryTable(connection, plan, sqlRenderer);
                        await UpsertBatchAsync(connection, plan, sqlRenderer, tempTable, batch, cancellationToken).ConfigureAwait(false);
                        count += batch.Count;
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    tempTable ??= CreateTemporaryTable(connection, plan, sqlRenderer);
                    await UpsertBatchAsync(connection, plan, sqlRenderer, tempTable, batch, cancellationToken).ConfigureAwait(false);
                    count += batch.Count;
                }
            }
            catch
            {
                upsertFailed = true;
                throw;
            }
            finally
            {
                try
                {
                    if (tempTable is not null)
                    {
                        DropTemporaryTable(connection, sqlRenderer, tempTable, suppressFailure: upsertFailed);
                    }
                }
                finally
                {
                    if (openedHere)
                    {
                        await context.Database.CloseConnectionAsync().ConfigureAwait(false);
                    }
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

    private static int UpsertRemote<TEntity>(
        QuackDbConnection connection,
        DuckDBUpsertPlan plan,
        DuckDBUpsertSqlRenderer sqlRenderer,
        IEnumerable<TEntity> entities,
        int batchSize)
        where TEntity : class
    {
        RemoteStagingTables? staging = null;
        var failed = false;
        try
        {
            var count = 0;
            var effectiveBatchSize = EffectiveBatchSize(plan, batchSize);
            var batch = new List<TEntity>(InitialBatchCapacity(entities, effectiveBatchSize));
            foreach (var entity in entities)
            {
                batch.Add(entity);
                if (batch.Count == effectiveBatchSize)
                {
                    staging ??= CreateRemoteStagingTables(connection, plan, sqlRenderer);
                    UpsertRemoteBatch(connection, plan, sqlRenderer, staging, batch, cancellationToken: null);
                    count += batch.Count;
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                staging ??= CreateRemoteStagingTables(connection, plan, sqlRenderer);
                UpsertRemoteBatch(connection, plan, sqlRenderer, staging, batch, cancellationToken: null);
                count += batch.Count;
            }

            return count;
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            if (staging is not null)
            {
                DropRemoteStagingTables(connection, sqlRenderer, staging, suppressFailure: failed);
            }
        }
    }

    private static async Task<int> UpsertRemoteAsync<TEntity>(
        QuackDbConnection connection,
        DuckDBUpsertPlan plan,
        DuckDBUpsertSqlRenderer sqlRenderer,
        IEnumerable<TEntity> entities,
        int batchSize,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        RemoteStagingTables? staging = null;
        var failed = false;
        try
        {
            var count = 0;
            var effectiveBatchSize = EffectiveBatchSize(plan, batchSize);
            var batch = new List<TEntity>(InitialBatchCapacity(entities, effectiveBatchSize));
            foreach (var entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                batch.Add(entity);
                if (batch.Count == effectiveBatchSize)
                {
                    staging ??= await CreateRemoteStagingTablesAsync(
                        connection,
                        plan,
                        sqlRenderer,
                        cancellationToken).ConfigureAwait(false);
                    await UpsertRemoteBatchAsync(
                        connection,
                        plan,
                        sqlRenderer,
                        staging,
                        batch,
                        cancellationToken).ConfigureAwait(false);
                    count += batch.Count;
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                staging ??= await CreateRemoteStagingTablesAsync(
                    connection,
                    plan,
                    sqlRenderer,
                    cancellationToken).ConfigureAwait(false);
                await UpsertRemoteBatchAsync(
                    connection,
                    plan,
                    sqlRenderer,
                    staging,
                    batch,
                    cancellationToken).ConfigureAwait(false);
                count += batch.Count;
            }

            return count;
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            if (staging is not null)
            {
                await DropRemoteStagingTablesAsync(connection, sqlRenderer, staging, suppressFailure: failed)
                    .ConfigureAwait(false);
            }
        }
    }

    private static RemoteStagingTables CreateRemoteStagingTables(
        QuackDbConnection connection,
        DuckDBUpsertPlan plan,
        DuckDBUpsertSqlRenderer sqlRenderer)
    {
        var staging = BuildRemoteStagingTables(plan);
        try
        {
            ExecuteCommand(
                connection,
                sqlRenderer.RenderCreateStagingTable(
                    plan,
                    staging.RemoteTable,
                    temporary: false,
                    staging.RemoteSchema));
            return staging;
        }
        catch
        {
            DropRemoteStagingTables(connection, sqlRenderer, staging, suppressFailure: true);
            throw;
        }
    }

    private static async Task<RemoteStagingTables> CreateRemoteStagingTablesAsync(
        QuackDbConnection connection,
        DuckDBUpsertPlan plan,
        DuckDBUpsertSqlRenderer sqlRenderer,
        CancellationToken cancellationToken)
    {
        var staging = BuildRemoteStagingTables(plan);
        try
        {
            await ExecuteCommandAsync(
                connection,
                sqlRenderer.RenderCreateStagingTable(
                    plan,
                    staging.RemoteTable,
                    temporary: false,
                    staging.RemoteSchema),
                cancellationToken).ConfigureAwait(false);
            return staging;
        }
        catch
        {
            await DropRemoteStagingTablesAsync(connection, sqlRenderer, staging, suppressFailure: true)
                .ConfigureAwait(false);
            throw;
        }
    }

    private static RemoteStagingTables BuildRemoteStagingTables(DuckDBUpsertPlan plan)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var remoteTable = "__duckdb_upsert_" + suffix;
        var schema = plan.Schema ?? "main";
        return new RemoteStagingTables(
            remoteTable,
            schema);
    }

    private static void UpsertRemoteBatch<TEntity>(
        QuackDbConnection connection,
        DuckDBUpsertPlan plan,
        DuckDBUpsertSqlRenderer sqlRenderer,
        RemoteStagingTables staging,
        List<TEntity> batch,
        CancellationToken? cancellationToken)
        where TEntity : class
    {
        cancellationToken?.ThrowIfCancellationRequested();
        ExecuteCommand(
            connection,
            sqlRenderer.RenderInsertValues(plan, staging.RemoteTable, staging.RemoteSchema, batch));
        ExecuteCommand(
            connection,
            sqlRenderer.RenderUpsertBatch(plan, staging.RemoteTable, staging.RemoteSchema));
    }

    private static async Task UpsertRemoteBatchAsync<TEntity>(
        QuackDbConnection connection,
        DuckDBUpsertPlan plan,
        DuckDBUpsertSqlRenderer sqlRenderer,
        RemoteStagingTables staging,
        List<TEntity> batch,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        await ExecuteCommandAsync(
            connection,
            sqlRenderer.RenderInsertValues(plan, staging.RemoteTable, staging.RemoteSchema, batch),
            cancellationToken).ConfigureAwait(false);
        await ExecuteCommandAsync(
            connection,
            sqlRenderer.RenderUpsertBatch(plan, staging.RemoteTable, staging.RemoteSchema),
            cancellationToken).ConfigureAwait(false);
    }

    private static void DropRemoteStagingTables(
        QuackDbConnection connection,
        DuckDBUpsertSqlRenderer sqlRenderer,
        RemoteStagingTables staging,
        bool suppressFailure)
    {
        try
        {
            ExecuteCommand(
                connection,
                sqlRenderer.RenderDropTemporaryTable(staging.RemoteTable, staging.RemoteSchema));
        }
        catch (Exception) when (suppressFailure)
        {
        }
    }

    private static async Task DropRemoteStagingTablesAsync(
        QuackDbConnection connection,
        DuckDBUpsertSqlRenderer sqlRenderer,
        RemoteStagingTables staging,
        bool suppressFailure)
    {
        try
        {
            await ExecuteCommandAsync(
                connection,
                sqlRenderer.RenderDropTemporaryTable(staging.RemoteTable, staging.RemoteSchema),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) when (suppressFailure)
        {
        }
    }

    private static void ExecuteCommand(DbConnection connection, string sql, bool suppressFailure = false)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        catch when (suppressFailure)
        {
        }
    }

    private static async Task ExecuteCommandAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record RemoteStagingTables(
        string RemoteTable,
        string RemoteSchema);

    private static void UpsertBatch<TEntity>(
        DuckDBConnection connection,
        DuckDBUpsertPlan plan,
        DuckDBUpsertSqlRenderer sqlRenderer,
        string tempTable,
        List<TEntity> batch)
        where TEntity : class
    {
        AppendTemporaryRows(connection, plan, tempTable, batch, cancellationToken: null);
        using var command = connection.CreateCommand();
        command.CommandText = sqlRenderer.RenderUpsertBatch(plan, tempTable);
        command.ExecuteNonQuery();
    }

    private static async Task UpsertBatchAsync<TEntity>(
        DuckDBConnection connection,
        DuckDBUpsertPlan plan,
        DuckDBUpsertSqlRenderer sqlRenderer,
        string tempTable,
        List<TEntity> batch,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        AppendTemporaryRows(connection, plan, tempTable, batch, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sqlRenderer.RenderUpsertBatch(plan, tempTable);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int EffectiveBatchSize(DuckDBUpsertPlan plan, int requestedBatchSize)
        => DuckDBUpsertBatching.EffectiveBatchSize(plan.ColumnCount, requestedBatchSize);

    private static int InitialBatchCapacity<TEntity>(IEnumerable<TEntity> entities, int effectiveBatchSize)
        => DuckDBUpsertBatching.InitialBatchCapacity(
            effectiveBatchSize,
            entities.TryGetNonEnumeratedCount(out var sourceCount) ? sourceCount : null);

    private static string CreateTemporaryTable(
        DuckDBConnection connection,
        DuckDBUpsertPlan plan,
        DuckDBUpsertSqlRenderer sqlRenderer)
    {
        var tempTable = "__duckdb_upsert_" + Guid.NewGuid().ToString("N");
        using var command = connection.CreateCommand();
        command.CommandText = sqlRenderer.RenderCreateTemporaryTable(plan, tempTable);
        command.ExecuteNonQuery();
        return tempTable;
    }

    private static void AppendTemporaryRows<TEntity>(
        DuckDBConnection connection,
        DuckDBUpsertPlan plan,
        string tempTable,
        List<TEntity> batch,
        CancellationToken? cancellationToken)
        where TEntity : class
    {
        var writeRow = plan.WriteRow
            ?? throw new InvalidOperationException("The configured upsert plan does not contain an appender writer.");
        using var appender = connection.CreateAppender(tempTable);
        foreach (var entity in batch)
        {
            cancellationToken?.ThrowIfCancellationRequested();

            // AppendRow reuses DuckDB.NET's managed row wrapper and owns EndRow/error finalization.
            // Calling CreateRow directly here allocates one wrapper per entity.
            appender.AppendRow<object>(entity, writeRow);
        }
    }

    private static void DropTemporaryTable(
        DuckDBConnection connection,
        DuckDBUpsertSqlRenderer sqlRenderer,
        string tempTable,
        bool suppressFailure)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sqlRenderer.RenderDropTemporaryTable(tempTable);
            command.ExecuteNonQuery();
        }
        catch when (suppressFailure)
        {
            // A statement error aborts an explicit DuckDB transaction until rollback. Preserve the actionable
            // upsert exception; rolling back the transaction also removes its temporary staging table.
        }
    }
}