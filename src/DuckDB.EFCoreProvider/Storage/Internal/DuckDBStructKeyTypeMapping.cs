using Microsoft.EntityFrameworkCore.Storage;
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

    private readonly Type _leafClrType;
    private readonly IReadOnlyList<string> _fieldPath;

    public DuckDBStructKeyTypeMapping(
        string storeType,
        Type leafClrType,
        IReadOnlyList<string> fieldPath)
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(Dictionary<string, object>)),
            storeType: storeType ?? "STRUCT"))
    {
        _leafClrType = leafClrType;
        _fieldPath = fieldPath.ToArray();
    }

    private DuckDBStructKeyTypeMapping(
        RelationalTypeMappingParameters parameters,
        Type leafClrType,
        IReadOnlyList<string> fieldPath)
        : base(parameters)
    {
        _leafClrType = leafClrType;
        _fieldPath = fieldPath;
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DuckDBStructKeyTypeMapping(parameters, _leafClrType, _fieldPath);

    /// <inheritdoc />
    public override Expression CustomizeDataReaderExpression(Expression expression)
        => Expression.Call(
            ReadStructKeyMethod.MakeGenericMethod(_leafClrType),
            expression,
            Expression.Constant(_fieldPath.ToArray()));

    /// <inheritdoc />
    public override string GenerateSqlLiteral(object? value)
        => throw new NotSupportedException("A whole-struct extraction mapping cannot be used for SQL literals.");

    private static T ReadStructKey<T>(Dictionary<string, object>? dict, string[] path)
    {
        object? current = dict;
        for (var i = 0; i < path.Length; i++)
        {
            if (current is not Dictionary<string, object> level
                || !level.TryGetValue(path[i], out var value))
            {
                return default!;
            }

            current = value;
        }

        if (current is null)
        {
            return default!;
        }

        return (T)ConvertToClr(current, typeof(T))!;
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
