using DuckDB.EFCoreProvider.Diagnostics.Internal;
using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Metadata;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Concurrent;
using System.Data;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     High-throughput upsert helpers built on DuckDB's appender API plus a set-based native-DuckDB or DuckLake merge.
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
///         <item><description>no change tracking, concurrency checks, or EF command interceptors; provider lifecycle
///             diagnostics are emitted for the complete upsert operation;</description></item>
///         <item><description>the default conflict target is the entity's primary key, whose values must be supplied;
///             the alternate-target overload permits store-generated-on-add columns and does not populate their generated
///             values back into the supplied entities;</description></item>
///         <item><description>all staged non-key and non-conflict columns are overwritten from the supplied values;</description></item>
///         <item><description>callers own duplicate conflict-target handling within an input batch and can wrap the
///             operation in an explicit transaction when all batches must commit atomically;</description></item>
///         <item><description>EF column mappings and value converters are applied; shadow properties and
///             database-computed columns are not supported.</description></item>
///     </list>
/// </remarks>
public static class DuckDBUpsertExtensions
{
    private static readonly ConditionalWeakTable<
        IEntityType,
        ConcurrentDictionary<(string? Schema, string Table, DuckDBUpsertStrategy Strategy, object ConflictTarget), UpsertPlan>>
        PlanCaches = new();

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
            var plan = GetPlan(context, typeof(TEntity));
            var connection = (DuckDBConnection)context.Database.GetDbConnection();
            var openedHere = connection.State != ConnectionState.Open;

            if (openedHere)
            {
                context.Database.OpenConnection();
            }

            var count = 0;
            string? tempTable = null;
            try
            {
                var effectiveBatchSize = EffectiveBatchSize(plan, batchSize);
                var batch = new List<TEntity>(InitialBatchCapacity(entities, effectiveBatchSize));
                foreach (var entity in entities)
                {
                    batch.Add(entity);
                    if (batch.Count == effectiveBatchSize)
                    {
                        tempTable ??= CreateTemporaryTable(connection, plan);
                        UpsertBatch(connection, plan, tempTable, batch);
                        count += batch.Count;
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    tempTable ??= CreateTemporaryTable(connection, plan);
                    UpsertBatch(connection, plan, tempTable, batch);
                    count += batch.Count;
                }
            }
            finally
            {
                try
                {
                    if (tempTable is not null)
                    {
                        DropTemporaryTable(connection, tempTable);
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
    ///     values before invoking this method.
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
            var plan = GetPlan(context, typeof(TEntity), conflictPropertyNames);
            var connection = (DuckDBConnection)context.Database.GetDbConnection();
            var openedHere = connection.State != ConnectionState.Open;

            if (openedHere)
            {
                await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            var count = 0;
            string? tempTable = null;
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
                        tempTable ??= CreateTemporaryTable(connection, plan);
                        await UpsertBatchAsync(connection, plan, tempTable, batch, cancellationToken).ConfigureAwait(false);
                        count += batch.Count;
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    tempTable ??= CreateTemporaryTable(connection, plan);
                    await UpsertBatchAsync(connection, plan, tempTable, batch, cancellationToken).ConfigureAwait(false);
                    count += batch.Count;
                }
            }
            finally
            {
                try
                {
                    if (tempTable is not null)
                    {
                        DropTemporaryTable(connection, tempTable);
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

    private static void UpsertBatch<TEntity>(
        DuckDBConnection connection,
        UpsertPlan plan,
        string tempTable,
        List<TEntity> batch)
        where TEntity : class
    {
        AppendTemporaryRows(connection, plan, tempTable, batch, cancellationToken: null);
        using var command = connection.CreateCommand();
        command.CommandText = plan.UpsertFromTemporaryTableSql(tempTable)
                              + $" DELETE FROM {DelimitTemporaryIdentifier(tempTable)};";
        command.ExecuteNonQuery();
    }

    private static async Task UpsertBatchAsync<TEntity>(
        DuckDBConnection connection,
        UpsertPlan plan,
        string tempTable,
        List<TEntity> batch,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        AppendTemporaryRows(connection, plan, tempTable, batch, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = plan.UpsertFromTemporaryTableSql(tempTable)
                              + $" DELETE FROM {DelimitTemporaryIdentifier(tempTable)};";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static int EffectiveBatchSize(UpsertPlan plan, int requestedBatchSize)
        => DuckDBUpsertBatching.EffectiveBatchSize(plan.ColumnCount, requestedBatchSize);

    private static int InitialBatchCapacity<TEntity>(IEnumerable<TEntity> entities, int effectiveBatchSize)
        => DuckDBUpsertBatching.InitialBatchCapacity(
            effectiveBatchSize,
            entities.TryGetNonEnumeratedCount(out var sourceCount) ? sourceCount : null);

    private static string CreateTemporaryTable(DuckDBConnection connection, UpsertPlan plan)
    {
        var tempTable = "__duckdb_upsert_" + Guid.NewGuid().ToString("N");
        using var command = connection.CreateCommand();
        command.CommandText = plan.CreateTemporaryTableSql(tempTable);
        command.ExecuteNonQuery();
        return tempTable;
    }

    private static void AppendTemporaryRows<TEntity>(
        DuckDBConnection connection,
        UpsertPlan plan,
        string tempTable,
        List<TEntity> batch,
        CancellationToken? cancellationToken)
        where TEntity : class
    {
        using var appender = connection.CreateAppender(tempTable);
        foreach (var entity in batch)
        {
            cancellationToken?.ThrowIfCancellationRequested();

            // AppendRow reuses DuckDB.NET's managed row wrapper and owns EndRow/error finalization.
            // Calling CreateRow directly here allocates one wrapper per entity.
            appender.AppendRow<object>(entity, plan.WriteRow);
        }
    }

    private static void DropTemporaryTable(DuckDBConnection connection, string tempTable)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS {DelimitTemporaryIdentifier(tempTable)};";
        command.ExecuteNonQuery();
    }

    private static string DelimitTemporaryIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static UpsertPlan GetPlan(
        DbContext context,
        Type clrType,
        IReadOnlyList<string>? conflictPropertyNames = null)
    {
        var entityType = context.Model.FindEntityType(clrType)
            ?? throw new InvalidOperationException($"'{clrType.Name}' is not part of the model.");

        var table = entityType.GetTableName()
            ?? throw new InvalidOperationException($"'{clrType.Name}' is not mapped to a table; upsert is not supported.");
        var schema = entityType.GetSchema();
        var strategy = context.GetService<IDuckDBEngineCapabilities>().UpsertStrategy;
        var conflictTarget = ResolveConflictTarget(entityType, conflictPropertyNames);
        var planCache = PlanCaches.GetValue(entityType, static _ => new());
        var cacheKey = (schema, table, strategy, conflictTarget.MetadataIdentity);

        return planCache.GetOrAdd(
            cacheKey,
            static (_, state) => BuildPlan(
                state.Context,
                state.EntityType,
                state.Table,
                state.Schema,
                state.Strategy,
                state.ConflictTarget),
            (Context: context, EntityType: entityType, Table: table, Schema: schema, Strategy: strategy, ConflictTarget: conflictTarget));
    }

    private static UpsertConflictTarget ResolveConflictTarget(
        IEntityType entityType,
        IReadOnlyList<string>? conflictPropertyNames)
    {
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"'{entityType.ClrType.Name}' has no primary key; upsert requires a primary key.");

        if (conflictPropertyNames is null)
        {
            return new UpsertConflictTarget(primaryKey.Properties, primaryKey, UsesPrimaryKey: true);
        }

        var properties = conflictPropertyNames.Select(name => entityType.FindProperty(name)
            ?? throw new InvalidOperationException(
                $"Conflict-target property '{entityType.DisplayName()}.{name}' is not a mapped scalar property."))
            .ToArray();

        var key = entityType.GetKeys().FirstOrDefault(candidate => candidate.Properties.SequenceEqual(properties));
        if (key is not null)
        {
            return new UpsertConflictTarget(key.Properties, key, key.IsPrimaryKey());
        }

        var index = entityType.GetIndexes()
            .FirstOrDefault(candidate => candidate.IsUnique && candidate.Properties.SequenceEqual(properties));
        if (index is not null)
        {
            return new UpsertConflictTarget(index.Properties, index, UsesPrimaryKey: false);
        }

        throw new InvalidOperationException(
            $"The conflict target for '{entityType.DisplayName()}' must be a primary key, alternate key, or unique index.");
    }

    private static UpsertPlan BuildPlan(
        DbContext context,
        IEntityType entityType,
        string table,
        string? schema,
        DuckDBUpsertStrategy strategy,
        UpsertConflictTarget conflictTarget)
    {
        var helper = context.GetService<ISqlGenerationHelper>();
        var storeObject = StoreObjectIdentifier.Table(table, schema);

        if (entityType.GetStructMetadata() is not null)
        {
            throw new NotSupportedException(
                $"Upsert does not support entity '{entityType.ClrType.Name}' because it contains struct-mapped complex properties. "
                + "STRUCT columns are consolidated at the physical layer and cannot be staged via the DuckDB Appender API. "
                + "Use SaveChanges instead.");
        }

        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"'{entityType.ClrType.Name}' has no primary key; upsert requires a primary key.");
        var conflictProperties = conflictTarget.Properties;

        var keyColumns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyProperty in primaryKey.Properties)
        {
            var keyColumn = keyProperty.GetColumnName(storeObject)
                ?? throw new InvalidOperationException(
                    $"Key property '{keyProperty.Name}' on '{entityType.ClrType.Name}' is not mapped to a column.");
            keyColumns.Add(keyColumn);
        }

        var conflictColumns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var conflictProperty in conflictProperties)
        {
            var conflictColumn = conflictProperty.GetColumnName(storeObject)
                ?? throw new InvalidOperationException(
                    $"Conflict-target property '{conflictProperty.Name}' on '{entityType.ClrType.Name}' is not mapped to table '{table}'.");
            conflictColumns.Add(conflictColumn);
        }

        var insertColumns = new List<string>();
        var updateColumns = new List<string>();
        var writableProperties = new List<IProperty>();

        foreach (var property in entityType.GetProperties())
        {
            var columnName = property.GetColumnName(storeObject);
            if (columnName is null)
            {
                continue;
            }

            if (property.IsShadowProperty())
            {
                throw new NotSupportedException(
                    $"Upsert does not support shadow property '{property.Name}' on '{entityType.ClrType.Name}'. Use SaveChanges instead.");
            }

            if (property.GetComputedColumnSql(storeObject) is not null)
            {
                // Database-computed columns cannot be inserted or assigned.
                continue;
            }

            if (!conflictTarget.UsesPrimaryKey
                && !conflictColumns.Contains(columnName)
                && IsStoreGeneratedOnAdd(property, storeObject))
            {
                // Omit sequence/default-backed columns from staging and INSERT so DuckDB applies DEFAULT.
                continue;
            }

            insertColumns.Add(columnName);

            writableProperties.Add(property);

            if (!keyColumns.Contains(columnName) && !conflictColumns.Contains(columnName))
            {
                updateColumns.Add(columnName);
            }
        }

        if (insertColumns.Count == 0)
        {
            throw new InvalidOperationException($"No writable columns were found for table '{table}'.");
        }

        var delimitedInsertColumns = insertColumns.Select(helper.DelimitIdentifier).ToArray();
        var insertColumnList = string.Join(", ", delimitedInsertColumns);
        var targetTable = helper.DelimitIdentifier(table, schema);

        if (strategy == DuckDBUpsertStrategy.Merge)
        {
            var keyPredicates = conflictProperties.Select(property =>
            {
                var column = helper.DelimitIdentifier(property.GetColumnName(storeObject)!);
                return $"target.{column} = source.{column}";
            });
            var updateAssignments = updateColumns.Select(column =>
            {
                var delimited = helper.DelimitIdentifier(column);
                return $"{delimited} = source.{delimited}";
            });
            var sourceValues = delimitedInsertColumns.Select(column => $"source.{column}");

            var mergeSuffix = new StringBuilder()
                .Append(" ON ")
                .AppendJoin(" AND ", keyPredicates);
            if (updateColumns.Count > 0)
            {
                mergeSuffix.Append(" WHEN MATCHED THEN UPDATE SET ").AppendJoin(", ", updateAssignments);
            }

            mergeSuffix
                .Append(" WHEN NOT MATCHED THEN INSERT (")
                .Append(insertColumnList)
                .Append(") VALUES (")
                .AppendJoin(", ", sourceValues)
                .Append(')');

            return new UpsertPlan(
                targetTable,
                insertColumnList,
                null,
                mergeSuffix.ToString(),
                writableProperties.Count,
                DuckDBCompiledAppenderRowWriter.Create(entityType.ClrType, writableProperties));
        }

        if (strategy != DuckDBUpsertStrategy.InsertOnConflict)
        {
            throw new NotSupportedException($"DuckDB upsert strategy '{strategy}' is not supported.");
        }

        // On a key conflict, overwrite the non-key columns from the proposed row; if the entity is all-key,
        // there is nothing to update, so do nothing.
        var conflictSuffix = new StringBuilder()
            .Append(" ON CONFLICT (")
            .AppendJoin(", ", conflictProperties.Select(p => helper.DelimitIdentifier(p.GetColumnName(storeObject)!)))
            .Append(')')
            .Append(updateColumns.Count == 0
                ? " DO NOTHING"
                : " DO UPDATE SET " + string.Join(
                    ", ",
                    updateColumns.Select(c => $"{helper.DelimitIdentifier(c)} = excluded.{helper.DelimitIdentifier(c)}")))
            .ToString();

        return new UpsertPlan(
            targetTable,
            insertColumnList,
            conflictSuffix,
            null,
            writableProperties.Count,
            DuckDBCompiledAppenderRowWriter.Create(entityType.ClrType, writableProperties));
    }

    private static bool IsStoreGeneratedOnAdd(IProperty property, in StoreObjectIdentifier storeObject)
        => property.ValueGenerated == ValueGenerated.OnAdd
           && (UsesAutoIncrement(property, storeObject)
               || property.TryGetDefaultValue(storeObject, out _)
               || property.GetDefaultValueSql(storeObject) is not null);

    private static bool UsesAutoIncrement(IProperty property, in StoreObjectIdentifier storeObject)
        => property.FindOverrides(storeObject)
                ?.FindAnnotation(DuckDBAnnotationNames.ValueGenerationStrategy)
                ?.Value is DuckDBValueGenerationStrategy.AutoIncrement
           || property.GetValueGenerationStrategy() == DuckDBValueGenerationStrategy.AutoIncrement;

    private readonly record struct UpsertConflictTarget(
        IReadOnlyList<IProperty> Properties,
        object MetadataIdentity,
        bool UsesPrimaryKey);

    private sealed record UpsertPlan(
        string TargetTable,
        string InsertColumnList,
        string? ConflictSuffix,
        string? MergeSuffix,
        int ColumnCount,
        Action<IDuckDBAppenderRow, object> WriteRow)
    {
        public string CreateTemporaryTableSql(string tempTable)
            => $"CREATE TEMPORARY TABLE {DelimitTemporaryIdentifier(tempTable)} AS SELECT {InsertColumnList} FROM {TargetTable} WHERE false;";

        public string UpsertFromTemporaryTableSql(string tempTable)
            => MergeSuffix is null
                ? $"INSERT INTO {TargetTable} ({InsertColumnList}) SELECT {InsertColumnList} FROM {DelimitTemporaryIdentifier(tempTable)}{ConflictSuffix};"
                : $"MERGE INTO {TargetTable} AS target USING {DelimitTemporaryIdentifier(tempTable)} AS source{MergeSuffix};";
    }
}