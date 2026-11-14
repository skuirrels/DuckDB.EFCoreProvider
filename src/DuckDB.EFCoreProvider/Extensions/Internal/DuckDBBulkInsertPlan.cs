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
        Action<IDuckDBAppenderRow, TEntity> writeRow,
        IReadOnlyList<DuckDBBulkInsertKeyReservation> keyReservations)
    {
        Table = table;
        Schema = schema;
        Columns = columns;
        WriteRow = writeRow;
        KeyReservations = keyReservations;
    }

    internal string Table { get; }

    internal string Schema { get; }

    internal IReadOnlyList<string> Columns { get; }

    internal Action<IDuckDBAppenderRow, TEntity> WriteRow { get; }

    /// <summary>Database-generated key columns whose values are reserved before rows are appended.</summary>
    internal IReadOnlyList<DuckDBBulkInsertKeyReservation> KeyReservations { get; }
}

internal static class DuckDBBulkInsertPlanner<TEntity>
    where TEntity : class
{
    private static readonly ConcurrentDictionary<
        (IEntityType EntityType, string? Database, string Schema, string Table, bool ReserveKeys),
        DuckDBBulkInsertPlan<TEntity>> PlanCache = new();

    internal static DuckDBBulkInsertPlan<TEntity> GetOrCreate(
        DbConnection connection,
        IEntityType entityType,
        string table,
        string schema,
        bool reserveKeys,
        string? database = null)
    {
        if (entityType.GetStructMetadata() is not null)
        {
            throw new NotSupportedException(
                $"Bulk insert into '{table}' is not supported for entities with DuckDB STRUCT mappings. "
                + "Use SaveChanges instead.");
        }

        var cacheKey = (entityType, database, schema, table, reserveKeys);
        if (PlanCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var columnMap = BuildColumnMap(entityType, table);
        var ordered = new List<IProperty>();
        var keyReservations = new List<DuckDBBulkInsertKeyReservation>();

        foreach (var (columnName, columnDefault) in GetColumns(connection, table, schema, database))
        {
            if (!columnMap.TryGetValue(columnName, out var property))
            {
                throw new NotSupportedException(
                    $"Bulk insert into '{table}' is not supported: table column '{columnName}' is not mapped to a writable property "
                    + "(computed/generated or unmapped columns are not supported). Use SaveChanges instead.");
            }

            ordered.Add(property);
            if (reserveKeys && columnDefault is not null && DuckDBBulkInsertKeyReservation.IsReservable(property))
            {
                keyReservations.Add(new DuckDBBulkInsertKeyReservation(property, columnDefault));
            }
        }

        if (ordered.Count == 0)
        {
            throw new InvalidOperationException($"No columns were found for table '{table}'.");
        }

        var plan = new DuckDBBulkInsertPlan<TEntity>(
            table,
            schema,
            ordered.Select(property => property.GetColumnName(StoreObjectIdentifier.Table(table, entityType.GetSchema()))!).ToArray(),
            DuckDBCompiledAppenderRowWriter.Create<TEntity>(ordered),
            keyReservations.ToArray());
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

    private static List<(string Name, string? Default)> GetColumns(
        DbConnection connection,
        string table,
        string schema,
        string? database)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT column_name, column_default FROM duckdb_columns() "
            + (database is null ? "WHERE database_name = current_database() " : "WHERE database_name = $d ")
            + "AND table_name = $t AND schema_name = $s ORDER BY column_index";
        if (database is not null)
        {
            command.AddParameter("d", database);
        }
        command.AddParameter("t", table);
        command.AddParameter("s", schema);

        var columns = new List<(string Name, string? Default)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return columns;
    }

}