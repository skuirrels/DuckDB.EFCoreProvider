using System.Collections.Frozen;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Internal;

internal enum DuckDBTerminalQueryOperator
{
    Count,
    LongCount,
    Any,
    Min,
    Max,
    Sum,
    Average
}

internal static class DuckDBTerminalQueryMethodResolver
{
    private static readonly FrozenDictionary<DuckDBTerminalQueryOperator, MethodInfo> GenericUnaryMethods = typeof(Queryable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(method =>
            method.IsGenericMethodDefinition
            && method.GetGenericArguments().Length == 1
            && method.GetParameters() is [{ ParameterType: var parameterType }]
            && parameterType.IsGenericType
            && parameterType.GetGenericTypeDefinition() == typeof(IQueryable<>))
        .Where(method => method.Name is
            nameof(Queryable.Count)
            or nameof(Queryable.LongCount)
            or nameof(Queryable.Any)
            or nameof(Queryable.Min)
            or nameof(Queryable.Max))
        .ToFrozenDictionary(method => GetOperator(method.Name));

    private static readonly FrozenDictionary<(DuckDBTerminalQueryOperator Operator, Type ElementType), MethodInfo> NumericUnaryMethods = typeof(Queryable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(method =>
            !method.IsGenericMethod
            && method.GetParameters() is [{ ParameterType: var parameterType }]
            && parameterType.IsGenericType
            && parameterType.GetGenericTypeDefinition() == typeof(IQueryable<>))
        .Where(method => method.Name is nameof(Queryable.Sum) or nameof(Queryable.Average))
        .ToFrozenDictionary(
            method => (GetOperator(method.Name), method.GetParameters()[0].ParameterType.GetGenericArguments()[0]));

    public static MethodInfo Resolve(DuckDBTerminalQueryOperator queryOperator, Type elementType)
    {
        ArgumentNullException.ThrowIfNull(elementType);

        var methodName = queryOperator switch
        {
            DuckDBTerminalQueryOperator.Count => nameof(Queryable.Count),
            DuckDBTerminalQueryOperator.LongCount => nameof(Queryable.LongCount),
            DuckDBTerminalQueryOperator.Any => nameof(Queryable.Any),
            DuckDBTerminalQueryOperator.Min => nameof(Queryable.Min),
            DuckDBTerminalQueryOperator.Max => nameof(Queryable.Max),
            DuckDBTerminalQueryOperator.Sum => nameof(Queryable.Sum),
            DuckDBTerminalQueryOperator.Average => nameof(Queryable.Average),
            _ => throw new ArgumentOutOfRangeException(nameof(queryOperator), queryOperator, null)
        };
        if (queryOperator is DuckDBTerminalQueryOperator.Sum or DuckDBTerminalQueryOperator.Average)
        {
            if (NumericUnaryMethods.TryGetValue((queryOperator, elementType), out var numericMethod))
            {
                return numericMethod;
            }

            throw new NotSupportedException(
                $"Terminal {methodName} command extraction does not support IQueryable<{GetDisplayName(elementType)}>. "
                + "Project to int, long, float, double, decimal, or the corresponding nullable type first.");
        }

        return GenericUnaryMethods[queryOperator].MakeGenericMethod(elementType);
    }

    private static DuckDBTerminalQueryOperator GetOperator(string methodName)
        => methodName switch
        {
            nameof(Queryable.Count) => DuckDBTerminalQueryOperator.Count,
            nameof(Queryable.LongCount) => DuckDBTerminalQueryOperator.LongCount,
            nameof(Queryable.Any) => DuckDBTerminalQueryOperator.Any,
            nameof(Queryable.Min) => DuckDBTerminalQueryOperator.Min,
            nameof(Queryable.Max) => DuckDBTerminalQueryOperator.Max,
            nameof(Queryable.Sum) => DuckDBTerminalQueryOperator.Sum,
            nameof(Queryable.Average) => DuckDBTerminalQueryOperator.Average,
            _ => throw new ArgumentOutOfRangeException(nameof(methodName), methodName, null)
        };

    private static string GetDisplayName(Type type)
        => Nullable.GetUnderlyingType(type) is { } underlyingType
            ? underlyingType.Name + "?"
            : type.Name;
}