using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     A <see cref="RelationalTypeMapping" /> that reads a whole DuckDB STRUCT column as a
///     <c>Dictionary&lt;string, object&gt;</c> and extracts a single field path client-side.
/// </summary>
/// <remarks>
///     DuckDB.NET materializes a whole STRUCT column as a dictionary whose keys are exactly the
///     physically-present struct members. Extracting the requested field through
///     <see cref="Dictionary{TKey,TValue}.ContainsKey" /> therefore avoids the binder error that
///     per-field <c>struct."field"</c> projections produce when a declared C# member has no
///     backing field in the underlying struct, and never reads fields that were not projected.
/// </remarks>
public sealed class DuckDBStructKeyTypeMapping : RelationalTypeMapping
{
    private static readonly MethodInfo ReadStructKeyMethod = typeof(DuckDBStructKeyTypeMapping).GetMethod(
        nameof(ReadStructKey), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo HasStructKeyMethod = typeof(DuckDBStructKeyTypeMapping).GetMethod(
        nameof(HasStructKey), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly Type _leafClrType;
    private readonly IReadOnlyList<string> _fieldPath;
    private readonly ValueConverter? _converter;

    internal Type LeafClrType => _leafClrType;

    internal IReadOnlyList<string> FieldPath => _fieldPath;

    internal ValueConverter? LeafConverter => _converter;

    public DuckDBStructKeyTypeMapping(
        string storeType,
        Type leafClrType,
        IReadOnlyList<string> fieldPath)
        : this(storeType, leafClrType, fieldPath, null)
    {
    }

    internal DuckDBStructKeyTypeMapping(
        RelationalTypeMapping leafTypeMapping,
        IReadOnlyList<string> fieldPath)
        : this(
            leafTypeMapping.StoreType,
            leafTypeMapping.ClrType,
            fieldPath,
            leafTypeMapping.Converter)
    {
    }

    private DuckDBStructKeyTypeMapping(
        string storeType,
        Type leafClrType,
        IReadOnlyList<string> fieldPath,
        ValueConverter? converter)
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(Dictionary<string, object>)),
            storeType: storeType ?? "STRUCT"))
    {
        _leafClrType = leafClrType;
        _fieldPath = fieldPath.ToArray();
        _converter = converter;
    }

    private DuckDBStructKeyTypeMapping(
        RelationalTypeMappingParameters parameters,
        Type leafClrType,
        IReadOnlyList<string> fieldPath,
        ValueConverter? converter)
        : base(parameters)
    {
        _leafClrType = leafClrType;
        _fieldPath = fieldPath;
        _converter = converter;
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DuckDBStructKeyTypeMapping(parameters, _leafClrType, _fieldPath, _converter);

    /// <inheritdoc />
    public override Expression CustomizeDataReaderExpression(Expression expression)
        => CreateReadExpression(expression, _leafClrType, _fieldPath, _converter);

    internal static Expression CreateReadExpression(
        Expression expression,
        Type leafClrType,
        IReadOnlyList<string> fieldPath,
        ValueConverter? converter = null)
    {
        var providerType = converter?.ProviderClrType ?? leafClrType;
        Expression valueExpression = Expression.Call(
            ReadStructKeyMethod.MakeGenericMethod(providerType),
            expression,
            Expression.Constant(fieldPath.ToArray()));

        if (converter is not null)
        {
            var convertedValueExpression = ReplacingExpressionVisitor.Replace(
                converter.ConvertFromProviderExpression.Parameters.Single(),
                valueExpression,
                converter.ConvertFromProviderExpression.Body);

            valueExpression = Expression.Condition(
                Expression.Call(
                    HasStructKeyMethod,
                    expression,
                    Expression.Constant(fieldPath.ToArray())),
                convertedValueExpression,
                Expression.Default(leafClrType));
        }

        return valueExpression.Type == leafClrType
            ? valueExpression
            : Expression.Convert(valueExpression, leafClrType);
    }

    /// <inheritdoc />
    public override string GenerateSqlLiteral(object? value)
        => throw new NotSupportedException("A whole-struct extraction mapping cannot be used for SQL literals.");

    private static T ReadStructKey<T>(object? value, string[] path)
    {
        object? current = value is DBNull ? null : value;
        for (var i = 0; i < path.Length; i++)
        {
            if (current is not Dictionary<string, object> level)
            {
                return default!;
            }

            if (!level.TryGetValue(path[i], out var foundValue)
                && !TryGetValueIgnoreCase(level, path[i], out foundValue))
            {
                return default!;
            }

            current = foundValue;
        }

        if (current is null or DBNull)
        {
            return default!;
        }

        return (T)ConvertToClr(current, typeof(T))!;
    }

    private static bool HasStructKey(object? value, string[] path)
    {
        object? current = value;
        for (var i = 0; i < path.Length; i++)
        {
            if (current is not Dictionary<string, object> level
                || (!level.TryGetValue(path[i], out var foundValue)
                    && !TryGetValueIgnoreCase(level, path[i], out foundValue)))
            {
                return false;
            }

            current = foundValue;
        }

        return current is not null and not DBNull;
    }

    private static bool TryGetValueIgnoreCase(
        Dictionary<string, object> dictionary,
        string key,
        out object? value)
    {
        foreach (var pair in dictionary)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static object? ConvertToClr(object value, Type targetType)
    {
        var nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nonNullable.IsInstanceOfType(value))
        {
            return value;
        }

        if (nonNullable.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(nonNullable);
            var converted = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
            return Enum.ToObject(nonNullable, converted);
        }

        try
        {
            return Convert.ChangeType(value, nonNullable, CultureInfo.InvariantCulture);
        }
        catch
        {
            return value;
        }
    }
}
