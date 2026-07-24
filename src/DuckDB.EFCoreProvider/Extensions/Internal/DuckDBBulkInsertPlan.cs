using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

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

    private static readonly MethodInfo AppendConvertedValueMethod =
        typeof(DuckDBBulkInsertPlanner<TEntity>)
            .GetMethod(nameof(AppendConvertedValue), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo AppendPropertyValueMethod =
        typeof(DuckDBBulkInsertPlanner<TEntity>)
            .GetMethod(nameof(AppendPropertyValue), BindingFlags.NonPublic | BindingFlags.Static)!;

    internal static DuckDBBulkInsertPlan<TEntity> GetOrCreate(
        DuckDBConnection connection,
        IEntityType entityType,
        string table,
        string schema)
    {
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

        var plan = new DuckDBBulkInsertPlan<TEntity>(table, schema, CreateRowWriter(ordered));
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

    private static Action<IDuckDBAppenderRow, TEntity> CreateRowWriter(IReadOnlyList<IProperty> properties)
    {
        var row = Expression.Parameter(typeof(IDuckDBAppenderRow), "row");
        var entity = Expression.Parameter(typeof(TEntity), "entity");
        var expressions = new List<Expression>(properties.Count + 1);

        foreach (var property in properties)
        {
            expressions.Add(CreateAppendExpression(row, entity, property));
        }

        // Action delegates require a void block even though direct AppendValue calls return the row.
        expressions.Add(Expression.Empty());
        return Expression.Lambda<Action<IDuckDBAppenderRow, TEntity>>(
            Expression.Block(expressions),
            row,
            entity).Compile();
    }

    private static Expression CreateAppendExpression(
        ParameterExpression row,
        ParameterExpression entity,
        IProperty property)
    {
        var getter = property.GetGetter();
        var converter = property.GetTypeMapping().Converter;
        if (converter is not null)
        {
            return Expression.Call(
                AppendConvertedValueMethod,
                row,
                entity,
                Expression.Constant(getter, typeof(IClrPropertyGetter)),
                Expression.Constant(converter, typeof(ValueConverter)));
        }

        if (property.PropertyInfo is not { } propertyInfo
            || FindAppendValueMethod(propertyInfo.PropertyType) is not { } appendValueMethod)
        {
            return Expression.Call(
                AppendPropertyValueMethod,
                row,
                entity,
                Expression.Constant(getter, typeof(IClrPropertyGetter)));
        }

        Expression value = Expression.Property(entity, propertyInfo);
        var parameterType = appendValueMethod.GetParameters()[0].ParameterType;
        if (value.Type != parameterType)
        {
            value = Expression.Convert(value, parameterType);
        }

        return Expression.Call(row, appendValueMethod, value);
    }

    private static MethodInfo? FindAppendValueMethod(Type valueType)
    {
        var parameterType = valueType.IsValueType && Nullable.GetUnderlyingType(valueType) is null
            ? typeof(Nullable<>).MakeGenericType(valueType)
            : valueType;

        return typeof(IDuckDBAppenderRow)
            .GetMethods()
            .SingleOrDefault(
                method => method.Name == nameof(IDuckDBAppenderRow.AppendValue)
                    && !method.IsGenericMethod
                    && method.GetParameters() is [{ ParameterType: var candidateType }]
                    && candidateType == parameterType);
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

    private static void AppendConvertedValue(
        IDuckDBAppenderRow row,
        TEntity entity,
        IClrPropertyGetter getter,
        ValueConverter converter)
        => AppendValue(row, converter.ConvertToProvider(getter.GetClrValue(entity)));

    private static void AppendPropertyValue(
        IDuckDBAppenderRow row,
        TEntity entity,
        IClrPropertyGetter getter)
        => AppendValue(row, getter.GetClrValue(entity));

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
}