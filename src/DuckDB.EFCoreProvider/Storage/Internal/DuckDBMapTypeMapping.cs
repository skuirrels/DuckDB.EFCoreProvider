using DuckDB.EFCoreProvider.Extensions.Internal;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     Maps <see cref="Dictionary{TKey,TValue}" />, <see cref="IDictionary{TKey,TValue}" /> and
///     <see cref="IReadOnlyDictionary{TKey,TValue}" /> properties to a native DuckDB <c>MAP(K, V)</c> column.
/// </remarks>
public abstract class DuckDBMapTypeMapping : RelationalTypeMapping
{
    protected DuckDBMapTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <summary>The mapping of the map's keys.</summary>
    public abstract RelationalTypeMapping KeyTypeMapping { get; }

    /// <summary>The mapping of the map's values.</summary>
    public abstract RelationalTypeMapping ValueTypeMapping { get; }

    internal static string CreateStoreType(RelationalTypeMapping keyMapping, RelationalTypeMapping valueMapping)
        => $"MAP({keyMapping.StoreType}, {valueMapping.StoreType})";
}

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     DuckDB.NET can read a MAP as a dictionary but cannot bind a dictionary parameter, so parameters are sent as
///     DuckDB's quoted MAP text form, which DuckDB casts to the column's MAP type on assignment and comparison.
///     Literals use the typed <c>MAP {k: v}</c> constructor.
/// </remarks>
public class DuckDBMapTypeMapping<TDictionary, TKey, TValue> : DuckDBMapTypeMapping
    where TDictionary : class, IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    private static readonly MethodInfo ConvertFromProviderMethod =
        typeof(DuckDBMapTypeMapping<TDictionary, TKey, TValue>)
            .GetMethod(nameof(ConvertFromProvider), BindingFlags.Static | BindingFlags.NonPublic)!;

    private readonly RelationalTypeMapping _keyTypeMapping;
    private readonly RelationalTypeMapping _valueTypeMapping;

    public DuckDBMapTypeMapping(RelationalTypeMapping keyTypeMapping, RelationalTypeMapping valueTypeMapping)
        : this(CreateStoreType(keyTypeMapping, valueTypeMapping), keyTypeMapping, valueTypeMapping)
    {
    }

    public DuckDBMapTypeMapping(
        string storeType,
        RelationalTypeMapping keyTypeMapping,
        RelationalTypeMapping valueTypeMapping)
        : this(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(
                    typeof(TDictionary),
                    comparer: new DuckDBMapValueComparer<TDictionary, TKey, TValue>()),
                storeType,
                dbType: System.Data.DbType.String),
            keyTypeMapping,
            valueTypeMapping)
    {
    }

    protected DuckDBMapTypeMapping(
        RelationalTypeMappingParameters parameters,
        RelationalTypeMapping keyTypeMapping,
        RelationalTypeMapping valueTypeMapping)
        : base(parameters)
    {
        Debug.Assert(keyTypeMapping.ClrType == typeof(TKey), "MAP key mapping does not match TKey");
        Debug.Assert(
            valueTypeMapping.ClrType == typeof(TValue).UnwrapNullableType(),
            "MAP value mapping does not match TValue");

        _keyTypeMapping = keyTypeMapping;
        _valueTypeMapping = valueTypeMapping;
    }

    /// <inheritdoc />
    public override RelationalTypeMapping KeyTypeMapping
        => _keyTypeMapping;

    /// <inheritdoc />
    public override RelationalTypeMapping ValueTypeMapping
        => _valueTypeMapping;

    /// <summary>The key type DuckDB.NET materializes, after the key mapping's value converter.</summary>
    private Type ProviderKeyType
        => KeyTypeMapping.Converter?.ProviderClrType ?? typeof(TKey);

    /// <summary>The value type DuckDB.NET materializes, nullable when the model value type is nullable.</summary>
    private Type ProviderValueType
        => ValueTypeMapping.Converter?.ProviderClrType is { } providerType
            ? typeof(TValue).IsNullableValueType() ? providerType.MakeNullable() : providerType
            : typeof(TValue);

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DuckDBMapTypeMapping<TDictionary, TKey, TValue>(parameters, KeyTypeMapping, ValueTypeMapping);

    /// <inheritdoc />
    public override DbParameter CreateParameter(
        DbCommand command,
        string name,
        object? value,
        bool? nullable = null,
        ParameterDirection direction = ParameterDirection.Input)
    {
        var parameter = command.CreateParameter();
        parameter.Direction = direction;
        parameter.ParameterName = name;
        parameter.Value = value is null ? DBNull.Value : FormatParameterValue((TDictionary)value);
        if (nullable.HasValue)
        {
            parameter.IsNullable = nullable.Value;
        }

        parameter.DbType = System.Data.DbType.String;
        ConfigureParameter(parameter);
        return parameter;
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        parameter.ConfigureNameAndMetadata(this);
        base.ConfigureParameter(parameter);
    }

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        var builder = new StringBuilder("MAP {");
        var isFirst = true;
        foreach (var (key, element) in (TDictionary)value)
        {
            if (!isFirst)
            {
                builder.Append(", ");
            }

            isFirst = false;
            builder
                .Append(KeyTypeMapping.GenerateSqlLiteral(key))
                .Append(": ")
                .Append(ValueTypeMapping.GenerateSqlLiteral(element));
        }

        // The cast keeps an empty map, or one whose values are all NULL, typed as the column's MAP.
        return builder.Append("}::").Append(StoreType).ToString();
    }

    /// <inheritdoc />
    public override MethodInfo GetDataReaderMethod()
        => GetDataReaderMethod(typeof(Dictionary<,>).MakeGenericType(ProviderKeyType, ProviderValueType));

    /// <inheritdoc />
    public override Expression CustomizeDataReaderExpression(Expression expression)
        => KeyTypeMapping.Converter is null && ValueTypeMapping.Converter is null
            ? expression
            : Expression.Call(
                ConvertFromProviderMethod.MakeGenericMethod(ProviderKeyType, ProviderValueType),
                expression,
                Expression.Constant(KeyTypeMapping, typeof(RelationalTypeMapping)),
                Expression.Constant(ValueTypeMapping, typeof(RelationalTypeMapping)));

    private string FormatParameterValue(TDictionary dictionary)
    {
        var keyConverter = KeyTypeMapping.Converter;
        var valueConverter = ValueTypeMapping.Converter;
        var builder = new StringBuilder("{");
        var isFirst = true;
        foreach (var (key, value) in dictionary)
        {
            if (!isFirst)
            {
                builder.Append(", ");
            }

            isFirst = false;
            DuckDBMapText.AppendElement(builder, keyConverter is null ? key : keyConverter.ConvertToProvider(key));
            builder.Append('=');
            DuckDBMapText.AppendElement(
                builder,
                value is null || valueConverter is null ? value : valueConverter.ConvertToProvider(value));
        }

        return builder.Append('}').ToString();
    }

    private static Dictionary<TKey, TValue> ConvertFromProvider<TProviderKey, TProviderValue>(
        Dictionary<TProviderKey, TProviderValue> provider,
        RelationalTypeMapping keyMapping,
        RelationalTypeMapping valueMapping)
        where TProviderKey : notnull
    {
        var keyConverter = keyMapping.Converter;
        var valueConverter = valueMapping.Converter;
        var model = new Dictionary<TKey, TValue>(provider.Count);
        foreach (var (key, value) in provider)
        {
            var modelKey = keyConverter is null ? (object)key : keyConverter.ConvertFromProvider(key);
            var modelValue = value is null || valueConverter is null ? value : valueConverter.ConvertFromProvider(value);
            model.Add((TKey)modelKey!, (TValue)modelValue!);
        }

        return model;
    }
}

/// <summary>
///     Compares MAP values by their entries, so change tracking detects in-place dictionary edits.
/// </summary>
internal sealed class DuckDBMapValueComparer<TDictionary, TKey, TValue>() : ValueComparer<TDictionary>(
    (left, right) => AreEqual(left, right),
    dictionary => ComputeHash(dictionary),
    dictionary => CreateSnapshot(dictionary))
    where TDictionary : class, IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    private static bool AreEqual(TDictionary? left, TDictionary? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        var rightEntries = AsReadOnly(right);
        var count = 0;
        foreach (var (key, value) in left)
        {
            count++;
            if (!rightEntries.TryGetValue(key, out var other)
                || !EqualityComparer<TValue>.Default.Equals(value, other))
            {
                return false;
            }
        }

        return count == rightEntries.Count;
    }

    private static int ComputeHash(TDictionary dictionary)
    {
        // Order-independent, because dictionaries with the same entries can enumerate differently. Keys are left
        // out because equality looks them up with the dictionary's own key comparer, which may not be the default.
        var hash = 0;
        var count = 0;
        foreach (var (_, value) in dictionary)
        {
            count++;
            hash += value is null ? 0 : EqualityComparer<TValue>.Default.GetHashCode(value);
        }

        return HashCode.Combine(count, hash);
    }

    // A Dictionary snapshot keeps the key comparer, so a case-insensitive map stays case-insensitive.
    private static TDictionary CreateSnapshot(TDictionary dictionary)
        => (TDictionary)(object)(dictionary is Dictionary<TKey, TValue> concrete
            ? new Dictionary<TKey, TValue>(concrete, concrete.Comparer)
            : new Dictionary<TKey, TValue>(dictionary));

    private static IReadOnlyDictionary<TKey, TValue> AsReadOnly(TDictionary dictionary)
        => dictionary as IReadOnlyDictionary<TKey, TValue> ?? new Dictionary<TKey, TValue>(dictionary);
}