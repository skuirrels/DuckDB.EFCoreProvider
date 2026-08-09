using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal static class DuckDBCompiledAppenderRowWriter
{
    private static readonly MethodInfo AppendConvertedValueMethod =
        typeof(DuckDBCompiledAppenderRowWriter)
            .GetMethod(nameof(AppendConvertedValue), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo AppendPropertyValueMethod =
        typeof(DuckDBCompiledAppenderRowWriter)
            .GetMethod(nameof(AppendPropertyValue), BindingFlags.NonPublic | BindingFlags.Static)!;

    internal static Action<IDuckDBAppenderRow, TEntity> Create<TEntity>(IReadOnlyList<IProperty> properties)
        where TEntity : class
    {
        var row = Expression.Parameter(typeof(IDuckDBAppenderRow), "row");
        var entity = Expression.Parameter(typeof(TEntity), "entity");
        return Expression.Lambda<Action<IDuckDBAppenderRow, TEntity>>(
            CreateWriteBlock(row, entity, entity, properties),
            row,
            entity).Compile();
    }

    internal static Action<IDuckDBAppenderRow, object> Create(
        Type entityClrType,
        IReadOnlyList<IProperty> properties)
    {
        var row = Expression.Parameter(typeof(IDuckDBAppenderRow), "row");
        var entity = Expression.Parameter(typeof(object), "entity");
        var typedEntity = Expression.Convert(entity, entityClrType);
        return Expression.Lambda<Action<IDuckDBAppenderRow, object>>(
            CreateWriteBlock(row, entity, typedEntity, properties),
            row,
            entity).Compile();
    }

    private static Expression CreateWriteBlock(
        ParameterExpression row,
        ParameterExpression entity,
        Expression typedEntity,
        IReadOnlyList<IProperty> properties)
    {
        var expressions = new List<Expression>(properties.Count + 1);
        foreach (var property in properties)
        {
            expressions.Add(CreateAppendExpression(row, entity, typedEntity, property));
        }

        // Action delegates require a void block even though direct AppendValue calls return the row.
        expressions.Add(Expression.Empty());
        return Expression.Block(expressions);
    }

    private static Expression CreateAppendExpression(
        ParameterExpression row,
        ParameterExpression entity,
        Expression typedEntity,
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

        Expression value = Expression.Property(typedEntity, propertyInfo);
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

    private static void AppendConvertedValue(
        IDuckDBAppenderRow row,
        object entity,
        IClrPropertyGetter getter,
        ValueConverter converter)
        => AppendValue(row, converter.ConvertToProvider(getter.GetClrValue(entity)));

    private static void AppendPropertyValue(
        IDuckDBAppenderRow row,
        object entity,
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
                    $"DuckDB appender operations do not support values of type '{value.GetType()}'. Use SaveChanges for this entity.");
        }
    }
}