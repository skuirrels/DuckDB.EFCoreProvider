using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Concurrent;
using System.Data;
using System.Text;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     High-throughput upsert helpers built on DuckDB's appender API plus a set-based native-DuckDB or DuckLake merge.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Upsert{TEntity}" /> / <see cref="UpsertAsync{TEntity}" /> insert the supplied entities,
///         updating any rows whose primary key already exists. Each batch is staged into a temporary table
///         via DuckDB's appender API and then merged into the target table with a set-based
///         <c>INSERT ... ON CONFLICT</c> (native DuckDB) or <c>MERGE INTO</c> (DuckLake). This is roughly an order of magnitude faster than the usual
///         read-then-insert-or-update pattern because it removes the existence-check round-trip and batches
///         the writes.
///     </para>
///     <para>
///         Like <see cref="DuckDBBulkExtensions.BulkInsert{TEntity}" />, this is a raw fast path:
///     </para>
///     <list type="bullet">
///         <item><description>no change tracking, concurrency checks, interceptors, or events;</description></item>
///         <item><description>the conflict target is the entity's primary key, whose values must be supplied
///             (store-generated keys are not supported — conflict detection needs the key);</description></item>
///         <item><description>all mapped non-key columns are overwritten from the supplied values;</description></item>
///         <item><description>EF column mappings and value converters are applied; shadow properties and
///             database-computed columns are not supported.</description></item>
///     </list>
/// </remarks>
public static class DuckDBUpsertExtensions
{
    /// <summary>Default number of rows staged into one temporary table before a set-based upsert.</summary>
    private const int DefaultBatchSize = 100;

    private static readonly ConcurrentDictionary<
        (IEntityType EntityType, string? Schema, string Table, bool IsDuckLake),
        UpsertPlan> PlanCache = new();

    /// <summary>
    ///     Inserts the supplied entities, updating any whose primary key already exists, using appender-staged
    ///     batches and set-based native-DuckDB <c>INSERT ... ON CONFLICT</c> or DuckLake <c>MERGE INTO</c> statements.
    /// </summary>
    /// <returns>The number of rows processed.</returns>
    public static int Upsert<TEntity>(this DbContext context, IEnumerable<TEntity> entities, int batchSize = DefaultBatchSize)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var plan = GetPlan(context, typeof(TEntity));
        var connection = (DuckDBConnection)context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            context.Database.OpenConnection();
        }

        try
        {
            var count = 0;
            var batch = new List<TEntity>(batchSize);
            foreach (var entity in entities)
            {
                batch.Add(entity);
                if (batch.Count == batchSize)
                {
                    UpsertBatch(connection, plan, batch);
                    count += batch.Count;
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                UpsertBatch(connection, plan, batch);
                count += batch.Count;
            }

            return count;
        }
        finally
        {
            if (openedHere)
            {
                context.Database.CloseConnection();
            }
        }
    }

    /// <summary>
    ///     Asynchronously inserts the supplied entities, updating any whose primary key already exists, using
    ///     appender-staged batches and set-based native-DuckDB <c>INSERT ... ON CONFLICT</c> or DuckLake <c>MERGE INTO</c> statements.
    /// </summary>
    /// <returns>The number of rows processed.</returns>
    public static async Task<int> UpsertAsync<TEntity>(
        this DbContext context,
        IEnumerable<TEntity> entities,
        int batchSize = DefaultBatchSize,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var plan = GetPlan(context, typeof(TEntity));
        var connection = (DuckDBConnection)context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var count = 0;
            var batch = new List<TEntity>(batchSize);
            foreach (var entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();

                batch.Add(entity);
                if (batch.Count == batchSize)
                {
                    await UpsertBatchAsync(connection, plan, batch, cancellationToken).ConfigureAwait(false);
                    count += batch.Count;
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await UpsertBatchAsync(connection, plan, batch, cancellationToken).ConfigureAwait(false);
                count += batch.Count;
            }

            return count;
        }
        finally
        {
            if (openedHere)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static void UpsertBatch<TEntity>(DuckDBConnection connection, UpsertPlan plan, List<TEntity> batch)
        where TEntity : class
    {
        var tempTable = CreateTemporaryTable(connection, plan);
        try
        {
            AppendTemporaryRows(connection, plan, tempTable, batch, cancellationToken: null);
            using var command = connection.CreateCommand();
            command.CommandText = plan.UpsertFromTemporaryTableSql(tempTable);
            command.ExecuteNonQuery();
        }
        finally
        {
            DropTemporaryTable(connection, tempTable);
        }
    }

    private static async Task UpsertBatchAsync<TEntity>(
        DuckDBConnection connection,
        UpsertPlan plan,
        List<TEntity> batch,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var tempTable = CreateTemporaryTable(connection, plan);
        try
        {
            AppendTemporaryRows(connection, plan, tempTable, batch, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = plan.UpsertFromTemporaryTableSql(tempTable);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DropTemporaryTable(connection, tempTable);
        }
    }

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

            var row = appender.CreateRow();
            for (var i = 0; i < plan.Accessors.Count; i++)
            {
                AppendValue(row, plan.Accessors[i](entity!));
            }

            row.EndRow();
        }
    }

    private static void DropTemporaryTable(DuckDBConnection connection, string tempTable)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP TABLE IF EXISTS {DelimitTemporaryIdentifier(tempTable)};";
        command.ExecuteNonQuery();
    }

    private static void AppendValue(IDuckDBAppenderRow row, object? value)
    {
        switch (value)
        {
            case null: row.AppendNullValue(); break;
            case bool v: row.AppendValue(v); break;
            case byte v: row.AppendValue(v); break;
            case sbyte v: row.AppendValue(v); break;
            case short v: row.AppendValue(v); break;
            case ushort v: row.AppendValue(v); break;
            case int v: row.AppendValue(v); break;
            case uint v: row.AppendValue(v); break;
            case long v: row.AppendValue(v); break;
            case ulong v: row.AppendValue(v); break;
            case float v: row.AppendValue(v); break;
            case double v: row.AppendValue(v); break;
            case decimal v: row.AppendValue(v); break;
            case string v: row.AppendValue(v); break;
            case Guid v: row.AppendValue(v); break;
            case DateTime v: row.AppendValue(v); break;
            case DateTimeOffset v: row.AppendValue(v); break;
            case TimeSpan v: row.AppendValue(v); break;
            case byte[] v: row.AppendValue(v); break;
            default:
                throw new NotSupportedException(
                    $"DuckDB upsert does not support values of type '{value.GetType()}'. Use SaveChanges for this entity.");
        }
    }

    private static string DelimitTemporaryIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static UpsertPlan GetPlan(DbContext context, Type clrType)
    {
        var entityType = context.Model.FindEntityType(clrType)
            ?? throw new InvalidOperationException($"'{clrType.Name}' is not part of the model.");

        var table = entityType.GetTableName()
            ?? throw new InvalidOperationException($"'{clrType.Name}' is not mapped to a table; upsert is not supported.");
        var schema = entityType.GetSchema();
        var isDuckLake = context.GetService<IDbContextOptions>()
            .FindExtension<DuckDBOptionsExtension>()?.DuckLakeOptions is not null;
        var cacheKey = (entityType, schema, table, isDuckLake);

        return PlanCache.GetOrAdd(cacheKey, _ => BuildPlan(context, entityType, table, schema, isDuckLake));
    }

    private static UpsertPlan BuildPlan(
        DbContext context,
        IEntityType entityType,
        string table,
        string? schema,
        bool isDuckLake)
    {
        var helper = context.GetService<ISqlGenerationHelper>();
        var storeObject = StoreObjectIdentifier.Table(table, schema);

        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"'{entityType.ClrType.Name}' has no primary key; upsert requires a primary key as the conflict target.");

        var keyColumns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyProperty in primaryKey.Properties)
        {
            var keyColumn = keyProperty.GetColumnName(storeObject)
                ?? throw new InvalidOperationException(
                    $"Key property '{keyProperty.Name}' on '{entityType.ClrType.Name}' is not mapped to a column.");
            keyColumns.Add(keyColumn);
        }

        var insertColumns = new List<string>();
        var updateColumns = new List<string>();
        var accessors = new List<Func<object, object?>>();

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

            if (property.GetComputedColumnSql() is not null)
            {
                // Database-computed columns cannot be inserted or assigned.
                continue;
            }

            insertColumns.Add(columnName);

            var getter = property.GetGetter();
            var converter = property.GetTypeMapping().Converter;
            accessors.Add(converter is null
                ? entity => getter.GetClrValue(entity)
                : entity => converter.ConvertToProvider(getter.GetClrValue(entity)));

            if (!keyColumns.Contains(columnName))
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

        if (isDuckLake)
        {
            var keyPredicates = primaryKey.Properties.Select(property =>
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

            return new UpsertPlan(targetTable, insertColumnList, null, mergeSuffix.ToString(), accessors);
        }

        // On a key conflict, overwrite the non-key columns from the proposed row; if the entity is all-key,
        // there is nothing to update, so do nothing.
        var conflictSuffix = new StringBuilder()
            .Append(" ON CONFLICT (")
            .AppendJoin(", ", primaryKey.Properties.Select(p => helper.DelimitIdentifier(p.GetColumnName(storeObject)!)))
            .Append(')')
            .Append(updateColumns.Count == 0
                ? " DO NOTHING"
                : " DO UPDATE SET " + string.Join(
                    ", ",
                    updateColumns.Select(c => $"{helper.DelimitIdentifier(c)} = excluded.{helper.DelimitIdentifier(c)}")))
            .ToString();

        return new UpsertPlan(targetTable, insertColumnList, conflictSuffix, null, accessors);
    }

    private sealed record UpsertPlan(
        string TargetTable,
        string InsertColumnList,
        string? ConflictSuffix,
        string? MergeSuffix,
        IReadOnlyList<Func<object, object?>> Accessors)
    {
        public string CreateTemporaryTableSql(string tempTable)
            => $"CREATE TEMPORARY TABLE {DelimitTemporaryIdentifier(tempTable)} AS SELECT {InsertColumnList} FROM {TargetTable} WHERE false;";

        public string UpsertFromTemporaryTableSql(string tempTable)
            => MergeSuffix is null
                ? $"INSERT INTO {TargetTable} ({InsertColumnList}) SELECT {InsertColumnList} FROM {DelimitTemporaryIdentifier(tempTable)}{ConflictSuffix};"
                : $"MERGE INTO {TargetTable} AS target USING {DelimitTemporaryIdentifier(tempTable)} AS source{MergeSuffix};";
    }
}
