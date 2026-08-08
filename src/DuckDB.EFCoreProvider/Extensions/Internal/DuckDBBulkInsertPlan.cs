using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections.Concurrent;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal sealed class DuckDBBulkInsertPlan<TEntity>
    where TEntity : class
{
    internal DuckDBBulkInsertPlan(
        string table,
        string schema,
        Action<IDuckDBAppenderRow, TEntity> writeRow)
    {
        Table = table;
        Schema = schema;
        WriteRow = writeRow;
    }

    internal string Table { get; }

    internal string Schema { get; }

    internal Action<IDuckDBAppenderRow, TEntity> WriteRow { get; }
}

internal static class DuckDBBulkInsertPlanner<TEntity>
    where TEntity : class
{
    private static readonly ConcurrentDictionary<
        (IEntityType EntityType, string Schema, string Table),
        DuckDBBulkInsertPlan<TEntity>> PlanCache = new();

    internal static DuckDBBulkInsertPlan<TEntity> GetOrCreate(
        DuckDBConnection connection,
        IEntityType entityType,
        string table,
        string schema)
    {
        if (entityType.GetStructMetadata() is not null)
        {
            throw new NotSupportedException(
                $"Bulk insert into '{table}' is not supported for entities with DuckDB STRUCT mappings. "
                + "Use SaveChanges instead.");
        }

        var cacheKey = (entityType, schema, table);
        if (PlanCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var columnMap = BuildColumnMap(entityType, table);
        var ordered = new List<IProperty>();

        foreach (var columnName in GetColumnOrder(connection, table, schema))
        {
            if (!columnMap.TryGetValue(columnName, out var property))
            {
                throw new NotSupportedException(
                    $"Bulk insert into '{table}' is not supported: table column '{columnName}' is not mapped to a writable property "
                    + "(computed/generated or unmapped columns are not supported). Use SaveChanges instead.");
            }

            ordered.Add(property);
        }

        if (ordered.Count == 0)
        {
            throw new InvalidOperationException($"No columns were found for table '{table}'.");
        }

        var plan = new DuckDBBulkInsertPlan<TEntity>(
            table,
            schema,
            DuckDBCompiledAppenderRowWriter.Create<TEntity>(ordered));
        return PlanCache.GetOrAdd(cacheKey, plan);
    }

    private static Dictionary<string, IProperty> BuildColumnMap(IEntityType entityType, string table)
    {
        var clrType = entityType.ClrType;
        var storeObject = StoreObjectIdentifier.Table(table, entityType.GetSchema());
        var columns = new Dictionary<string, IProperty>(StringComparer.OrdinalIgnoreCase);

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

            columns[columnName] = property;
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

}