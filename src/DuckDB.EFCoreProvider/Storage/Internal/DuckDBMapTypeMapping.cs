using DuckDB.EFCoreProvider.Extensions.Internal;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using System.Collections;
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

    private static readonly HashSet<Type> SupportedElementTypes =
    [
        typeof(string),
        typeof(bool),
        typeof(sbyte),
        typeof(byte),
        typeof(short),
        typeof(ushort),
        typeof(int),
        typeof(uint),
        typeof(long),
        typeof(ulong),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(Guid),
        typeof(DateTime),
        typeof(DateOnly)
    ];

    /// <summary>The mapping of the map's keys.</summary>
    public abstract RelationalTypeMapping KeyTypeMapping { get; }

    /// <summary>The mapping of the map's values.</summary>
    public abstract RelationalTypeMapping ValueTypeMapping { get; }

    internal static string CreateStoreType(RelationalTypeMapping keyMapping, RelationalTypeMapping valueMapping)
        => $"MAP({keyMapping.StoreType}, {valueMapping.StoreType})";

    /// <summary>Whether a provider element type can be bound, appended and read back by DuckDB.NET as a MAP entry.</summary>
    internal static bool IsSupportedElementType(Type providerType)
        => SupportedElementTypes.Contains(providerType.UnwrapNullableType());

    /// <summary>
    ///     Converts a model dictionary to the dictionary DuckDB.NET binds or appends, applying the key and value
    ///     converters. Returns <see langword="null" /> for a <see langword="null" /> map.
    /// </summary>
    /// <remarks>Virtual rather than abstract, so classes outside this assembly can still derive from this one.</remarks>
    internal virtual IDictionary? ConvertToProviderDictionary(object? value)
        => throw new NotSupportedException($"'{GetType().Name}' does not convert MAP values for DuckDB.NET.");
}

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     Parameters bind as native MAP values and the appender writes them directly, both from a dictionary of the
///     key and value provider types. Literals use the typed <c>MAP {k: v}</c> constructor.
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
    private readonly Type _providerDictionaryType;

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
                dbType: System.Data.DbType.Object),
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
        _providerDictionaryType = typeof(Dictionary<,>).MakeGenericType(ProviderKeyType, ProviderValueType);
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
        parameter.Value = (object?)ConvertToProviderDictionary(value) ?? DBNull.Value;
        if (nullable.HasValue)
        {
            parameter.IsNullable = nullable.Value;
        }

        parameter.DbType = System.Data.DbType.Object;
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
        => GetDataReaderMethod(_providerDictionaryType);

    /// <inheritdoc />
    public override Expression CustomizeDataReaderExpression(Expression expression)
        => KeyTypeMapping.Converter is null && ValueTypeMapping.Converter is null
            ? expression
            : Expression.Call(
                ConvertFromProviderMethod.MakeGenericMethod(ProviderKeyType, ProviderValueType),
                expression,
                Expression.Constant(KeyTypeMapping, typeof(RelationalTypeMapping)),
                Expression.Constant(ValueTypeMapping, typeof(RelationalTypeMapping)));

    /// <inheritdoc />
    internal override IDictionary? ConvertToProviderDictionary(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var dictionary = (TDictionary)value;
        var keyConverter = KeyTypeMapping.Converter;
        var valueConverter = ValueTypeMapping.Converter;

        // Without converters DuckDB.NET takes the dictionary as it is. A dictionary type that is only generic is
        // copied, because the appender and the parameter binder both accept a non-generic IDictionary.
        if (keyConverter is null && valueConverter is null && dictionary is IDictionary providerDictionary)
        {
            return providerDictionary;
        }

        var provider = (IDictionary)Activator.CreateInstance(_providerDictionaryType)!;
        foreach (var (key, element) in dictionary)
        {
            provider.Add(
                keyConverter is null ? key : keyConverter.ConvertToProvider(key)!,
                element is null || valueConverter is null ? element : valueConverter.ConvertToProvider(element));
        }

        return provider;
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