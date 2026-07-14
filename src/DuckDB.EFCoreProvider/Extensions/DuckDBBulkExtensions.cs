using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Collections.Concurrent;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;

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
///         <item><description>no change tracking, concurrency checks, interceptors, or events;</description></item>
///         <item><description>no store-generated values — every mapped column must be given a value;</description></item>
///         <item><description>the target table must already exist;</description></item>
///         <item><description>EF column mappings and value converters are applied; shadow properties,
///             computed/generated columns, and unmapped columns are not supported.</description></item>
///     </list>
///     <para>
///         The per-call setup (resolving the physical column order and building the column accessors) is
///         cached per entity type + table, so repeated bulk-insert calls avoid that fixed overhead.
///     </para>
/// </remarks>
public static class DuckDBBulkExtensions
{
    private static readonly ConcurrentDictionary<(IEntityType EntityType, string Schema, string Table), List<Action<IDuckDBAppenderRow, object>>> AccessorCache = new();

    /// <summary>
    ///     Bulk-inserts the supplied entities into their mapped table using the DuckDB appender.
    /// </summary>
    /// <returns>The number of rows appended.</returns>
    public static int BulkInsert<TEntity>(this DbContext context, IEnumerable<TEntity> entities)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);

        var (entityType, table, schema) = ResolveTarget(context, typeof(TEntity));
        var connection = (DuckDBConnection)context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            context.Database.OpenConnection();
        }

        try
        {
            var accessors = GetOrderedAccessors(connection, entityType, table, schema);
            return Append(connection, table, schema, accessors, entities);
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

        var (entityType, table, schema) = ResolveTarget(context, typeof(TEntity));
        var connection = (DuckDBConnection)context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;

        if (openedHere)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var accessors = GetOrderedAccessors(connection, entityType, table, schema);
            return Append(connection, table, schema, accessors, entities);
        }
        finally
        {
            if (openedHere)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    private static int Append<TEntity>(
        DuckDBConnection connection,
        string table,
        string schema,
        List<Action<IDuckDBAppenderRow, object>> appenders,
        IEnumerable<TEntity> entities)
        where TEntity : class
    {
        using var appender = connection.CreateAppender(schema, table);
        var count = 0;

        foreach (var entity in entities)
        {
            var row = appender.CreateRow();
            foreach (var append in appenders)
            {
                append(row, entity);
            }

            row.EndRow();
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

    private static List<Action<IDuckDBAppenderRow, object>> GetOrderedAccessors(
        DuckDBConnection connection,
        IEntityType entityType,
        string table,
        string schema)
    {
        var cacheKey = (entityType, schema, table);
        if (AccessorCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var columnMap = BuildColumnMap(entityType, table);
        var ordered = new List<Action<IDuckDBAppenderRow, object>>();

        foreach (var columnName in GetColumnOrder(connection, table, schema))
        {
            if (!columnMap.TryGetValue(columnName, out var accessor))
            {
                throw new NotSupportedException(
                    $"Bulk insert into '{table}' is not supported: table column '{columnName}' is not mapped to a writable property "
                    + "(computed/generated or unmapped columns are not supported). Use SaveChanges instead.");
            }

            ordered.Add(accessor);
        }

        if (ordered.Count == 0)
        {
            throw new InvalidOperationException($"No columns were found for table '{table}'.");
        }

        AccessorCache.TryAdd(cacheKey, ordered);
        return ordered;
    }

    private static Dictionary<string, Action<IDuckDBAppenderRow, object>> BuildColumnMap(IEntityType entityType, string table)
    {
        var clrType = entityType.ClrType;
        var storeObject = StoreObjectIdentifier.Table(table, entityType.GetSchema());
        var columns = new Dictionary<string, Action<IDuckDBAppenderRow, object>>(StringComparer.OrdinalIgnoreCase);

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
                    $"Bulk insert does not support shadow property '{property.Name}' on '{clrType.Name}'. Use SaveChanges instead.");
            }

            columns[columnName] = CreateAppender(property);
        }

        return columns;
    }

    private static List<string> GetColumnOrder(DuckDBConnection connection, string table, string schema)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name FROM duckdb_columns() "
            + "WHERE database_name = current_database() AND table_name = $t AND schema_name = $s ORDER BY column_index";
        command.Parameters.Add(new DuckDBParameter("t", table));
        command.Parameters.Add(new DuckDBParameter("s", schema));

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
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
                    $"DuckDB bulk insert does not support values of type '{value.GetType()}'. Use SaveChanges for this entity.");
        }
    }

    private static Action<IDuckDBAppenderRow, object> CreateAppender(IProperty property)
    {
        var getter = property.GetGetter();
        var converter = property.GetTypeMapping().Converter;
        if (converter is not null)
        {
            return (row, entity) => AppendValue(row, converter.ConvertToProvider(getter.GetClrValue(entity)));
        }

        if (property.PropertyInfo is not { } propertyInfo)
        {
            return (row, entity) => AppendValue(row, getter.GetClrValue(entity));
        }

        return CreatePropertyAppender(property.DeclaringType.ClrType, propertyInfo)
               ?? ((row, entity) => AppendValue(row, getter.GetClrValue(entity)));
    }

    private static Action<IDuckDBAppenderRow, object>? CreatePropertyAppender(Type entityType, PropertyInfo propertyInfo)
    {
        var method = typeof(DuckDBBulkExtensions)
            .GetMethod(nameof(CreatePropertyAppenderCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(entityType, propertyInfo.PropertyType);

        return (Action<IDuckDBAppenderRow, object>?)method.Invoke(null, [propertyInfo]);
    }

    private static Action<IDuckDBAppenderRow, object>? CreatePropertyAppenderCore<TEntity, TValue>(PropertyInfo propertyInfo)
        where TEntity : class
    {
        var entity = Expression.Parameter(typeof(TEntity), "entity");
        var getter = Expression.Lambda<Func<TEntity, TValue>>(Expression.Property(entity, propertyInfo), entity).Compile();

        if (typeof(TValue) == typeof(bool))
        {
            var typedGetter = (Func<TEntity, bool>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(byte))
        {
            var typedGetter = (Func<TEntity, byte>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(sbyte))
        {
            var typedGetter = (Func<TEntity, sbyte>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(short))
        {
            var typedGetter = (Func<TEntity, short>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(ushort))
        {
            var typedGetter = (Func<TEntity, ushort>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(int))
        {
            var typedGetter = (Func<TEntity, int>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(uint))
        {
            var typedGetter = (Func<TEntity, uint>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(long))
        {
            var typedGetter = (Func<TEntity, long>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(ulong))
        {
            var typedGetter = (Func<TEntity, ulong>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(float))
        {
            var typedGetter = (Func<TEntity, float>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(double))
        {
            var typedGetter = (Func<TEntity, double>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(decimal))
        {
            var typedGetter = (Func<TEntity, decimal>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(Guid))
        {
            var typedGetter = (Func<TEntity, Guid>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(DateTime))
        {
            var typedGetter = (Func<TEntity, DateTime>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(DateTimeOffset))
        {
            var typedGetter = (Func<TEntity, DateTimeOffset>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(TimeSpan))
        {
            var typedGetter = (Func<TEntity, TimeSpan>)(object)getter;
            return (row, value) => row.AppendValue(typedGetter((TEntity)value));
        }

        if (typeof(TValue) == typeof(string))
        {
            var typedGetter = (Func<TEntity, string?>)(object)getter;
            return (row, value) =>
            {
                var propertyValue = typedGetter((TEntity)value);
                if (propertyValue is null)
                {
                    row.AppendNullValue();
                }
                else
                {
                    row.AppendValue(propertyValue);
                }
            };
        }

        if (typeof(TValue) == typeof(byte[]))
        {
            var typedGetter = (Func<TEntity, byte[]?>)(object)getter;
            return (row, value) =>
            {
                var propertyValue = typedGetter((TEntity)value);
                if (propertyValue is null)
                {
                    row.AppendNullValue();
                }
                else
                {
                    row.AppendValue(propertyValue);
                }
            };
        }

        return null;
    }
}