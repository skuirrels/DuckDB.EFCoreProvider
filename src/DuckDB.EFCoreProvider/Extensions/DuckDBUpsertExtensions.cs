using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     High-throughput upsert helpers built on DuckDB's <c>INSERT ... ON CONFLICT DO UPDATE</c>.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Upsert{TEntity}" /> / <see cref="UpsertAsync{TEntity}" /> insert the supplied entities,
///         updating any rows whose primary key already exists. Consecutive entities are merged into a single
///         multi-row statement, which is roughly an order of magnitude faster than the usual
///         read-then-insert-or-update pattern (it removes the existence-check round-trip and batches the
///         writes). Per-row upserts are intentionally not exposed because a single-row <c>ON CONFLICT</c> is
///         slower than that pattern on DuckDB.
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
    /// <summary>Default number of rows merged into a single <c>INSERT ... ON CONFLICT</c> statement.</summary>
    private const int DefaultBatchSize = 100;

    private static readonly ConcurrentDictionary<string, UpsertPlan> PlanCache = new();

    /// <summary>
    ///     Inserts the supplied entities, updating any whose primary key already exists, using batched
    ///     <c>INSERT ... ON CONFLICT (key) DO UPDATE</c> statements.
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
            connection.Open();
        }

        try
        {
            var count = 0;
            foreach (var chunk in Chunk(entities, batchSize))
            {
                using var command = BuildCommand(connection, plan, chunk);
                command.ExecuteNonQuery();
                count += chunk.Count;
            }

            return count;
        }
        finally
        {
            if (openedHere)
            {
                connection.Close();
            }
        }
    }

    /// <summary>
    ///     Asynchronously inserts the supplied entities, updating any whose primary key already exists, using
    ///     batched <c>INSERT ... ON CONFLICT (key) DO UPDATE</c> statements.
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
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var count = 0;
            foreach (var chunk in Chunk(entities, batchSize))
            {
                await using var command = BuildCommand(connection, plan, chunk);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                count += chunk.Count;
            }

            return count;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static DbCommand BuildCommand<TEntity>(DuckDBConnection connection, UpsertPlan plan, List<TEntity> chunk)
        where TEntity : class
    {
        var command = connection.CreateCommand();
        var sql = new StringBuilder(plan.InsertPrefix);

        var parameterIndex = 0;
        for (var row = 0; row < chunk.Count; row++)
        {
            sql.Append(row == 0 ? " " : ", ").Append('(');

            for (var col = 0; col < plan.Accessors.Count; col++)
            {
                if (col > 0)
                {
                    sql.Append(", ");
                }

                var name = "p" + parameterIndex;
                sql.Append('$').Append(name);
                command.Parameters.Add(new DuckDBParameter(name, plan.Accessors[col](chunk[row]!) ?? DBNull.Value));
                parameterIndex++;
            }

            sql.Append(')');
        }

        sql.Append(plan.ConflictSuffix);
        command.CommandText = sql.ToString();
        return command;
    }

    private static UpsertPlan GetPlan(DbContext context, Type clrType)
    {
        var entityType = context.Model.FindEntityType(clrType)
            ?? throw new InvalidOperationException($"'{clrType.Name}' is not part of the model.");

        var table = entityType.GetTableName()
            ?? throw new InvalidOperationException($"'{clrType.Name}' is not mapped to a table; upsert is not supported.");
        var schema = entityType.GetSchema();
        var cacheKey = $"{clrType.FullName}|{schema}|{table}";

        return PlanCache.GetOrAdd(cacheKey, _ => BuildPlan(context, entityType, table, schema));
    }

    private static UpsertPlan BuildPlan(DbContext context, IEntityType entityType, string table, string? schema)
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

        var insertPrefix = new StringBuilder("INSERT INTO ")
            .Append(helper.DelimitIdentifier(table, schema))
            .Append(" (")
            .AppendJoin(", ", insertColumns.Select(helper.DelimitIdentifier))
            .Append(") VALUES")
            .ToString();

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

        return new UpsertPlan(insertPrefix, conflictSuffix, accessors);
    }

    private static IEnumerable<List<TEntity>> Chunk<TEntity>(IEnumerable<TEntity> entities, int batchSize)
    {
        var batch = new List<TEntity>(batchSize);
        foreach (var entity in entities)
        {
            batch.Add(entity);
            if (batch.Count == batchSize)
            {
                yield return batch;
                batch = new List<TEntity>(batchSize);
            }
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private sealed record UpsertPlan(
        string InsertPrefix,
        string ConflictSuffix,
        IReadOnlyList<Func<object, object?>> Accessors);
}
