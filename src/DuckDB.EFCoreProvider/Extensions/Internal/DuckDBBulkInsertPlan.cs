using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Collections.Concurrent;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal sealed class DuckDBBulkInsertPlan<TEntity>
    where TEntity : class
{
    internal DuckDBBulkInsertPlan(
        string table,
        string schema,
        IReadOnlyList<string> columns,
        Action<IDuckDBAppenderRow, TEntity> writeRow)
    {
        Table = table;
        Schema = schema;
        Columns = columns;
        WriteRow = writeRow;
    }

    internal string Table { get; }

    internal string Schema { get; }

    internal IReadOnlyList<string> Columns { get; }

    internal Action<IDuckDBAppenderRow, TEntity> WriteRow { get; }
}

internal static class DuckDBBulkInsertPlanner<TEntity>
    where TEntity : class
{
    private static readonly ConcurrentDictionary<
        (IEntityType EntityType, string? Database, string Schema, string Table),
        DuckDBBulkInsertPlan<TEntity>> PlanCache = new();

    internal static DuckDBBulkInsertPlan<TEntity> GetOrCreate(
        DbConnection connection,
        IEntityType entityType,
        string table,
        string schema,
        string? database = null)
    {
        if (entityType.GetStructMetadata() is not null)
        {
            throw new NotSupportedException(
                $"Bulk insert into '{table}' is not supported for entities with DuckDB STRUCT mappings. "
                + "Use SaveChanges instead.");
        }

        var cacheKey = (entityType, database, schema, table);
        if (PlanCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var columnMap = BuildColumnMap(entityType, table);
        var ordered = new List<IProperty>();

        foreach (var columnName in GetColumnOrder(connection, table, schema, database))
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
            ordered.Select(property => property.GetColumnName(StoreObjectIdentifier.Table(table, entityType.GetSchema()))!).ToArray(),
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

    private static List<string> GetColumnOrder(
        DbConnection connection,
        string table,
        string schema,
        string? database)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name FROM duckdb_columns() "
            + (database is null ? "WHERE database_name = current_database() " : "WHERE database_name = $d ")
            + "AND table_name = $t AND schema_name = $s ORDER BY column_index";
        if (database is not null)
        {
            command.Parameters.Add(new DuckDBParameter("d", database));
        }
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