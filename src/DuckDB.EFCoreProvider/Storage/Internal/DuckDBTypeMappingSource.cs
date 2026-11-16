using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBTypeMappingSource : RelationalTypeMappingSource
{
    internal const string VarCharTypeName = "VARCHAR";
    internal const string BlobTypeName = "BLOB";

    private static readonly DuckDBBooleanTypeMapping BooleanTypeMapping = new();
    private static readonly DuckDBByteTypeMapping ByteTypeMapping = new();
    private static readonly DuckDBDateOnlyTypeMapping DateTypeMapping = new();

    private static readonly DuckDBCharTypeMapping CharTypeMapping = new();
    private static readonly DuckDBDoubleTypeMapping DoubleTypeMapping = new();
    private static readonly DuckDBFloatTypeMapping FloatTypeMapping = new();
    private static readonly DuckDBGuidTypeMapping GuidTypeMapping = new();
    private static readonly DuckDBInt16TypeMapping Int16TypeMapping = new();
    private static readonly DuckDBInt32TypeMapping Int32TypeMapping = new();
    private static readonly DuckDBInt64TypeMapping Int64TypeMapping = new();
    private static readonly DuckDBSByteTypeMapping SByteTypeMapping = new();
    private static readonly DuckDBStringTypeMapping StringTypeMapping = DuckDBStringTypeMapping.Default;
    private static readonly DuckDBUInt16TypeMapping UInt16TypeMapping = new();
    private static readonly DuckDBUInt32TypeMapping UInt32TypeMapping = new();
    private static readonly DuckDBUInt64TypeMapping UInt64TypeMapping = new();

    // JSON
    private static readonly DuckDBJsonTypeMapping JsonString = new(typeof(string));
    private static readonly DuckDBJsonTypeMapping JsonDocument = new(typeof(JsonDocument));
    private static readonly DuckDBJsonTypeMapping JsonElement = new(typeof(JsonElement));
    private static readonly DuckDBStructuralJsonTypeMapping JsonOwned = new();

    private static readonly Dictionary<Type, RelationalTypeMapping> ClrTypeMappings = new()
    {
        { typeof(string), StringTypeMapping },
        { typeof(byte[]), DuckDBBlobTypeMapping.Default },
        { typeof(bool), BooleanTypeMapping },
        { typeof(byte), ByteTypeMapping },
        { typeof(char), CharTypeMapping },
        { typeof(int), Int32TypeMapping },
        { typeof(long), Int64TypeMapping },
        { typeof(sbyte), SByteTypeMapping },
        { typeof(short), Int16TypeMapping },
        { typeof(uint), UInt32TypeMapping },
        { typeof(ulong), UInt64TypeMapping },
        { typeof(ushort), UInt16TypeMapping },
        { typeof(DateTime), DuckDBTimestampTypeMapping.Timestamp },
        { typeof(DateTimeOffset), DuckDBTimestampTypeMapping.TimestampTz },
        { typeof(DateOnly), DateTypeMapping },
        { typeof(TimeSpan), DuckDBTimeTypeMapping.TimeSpan },
        { typeof(TimeOnly), DuckDBTimeTypeMapping.Time },
        { typeof(decimal), DuckDBDecimalTypeMapping.Default },
        { typeof(double), DoubleTypeMapping },
        { typeof(float), FloatTypeMapping },
        { typeof(Guid), GuidTypeMapping },
        { typeof(JsonDocument), JsonDocument },
        { typeof(JsonElement), JsonElement },
        { typeof(JsonTypePlaceholder), JsonOwned }
    };

    private static readonly Dictionary<string, RelationalTypeMapping> StoreTypeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Boolean
        { "BOOLEAN", BooleanTypeMapping },
        { "BOOL", BooleanTypeMapping },
        { "LOGICAL", BooleanTypeMapping },

        // Signed integers. DuckDB's INTn aliases use bytes, so INT8 is a signed BIGINT.
        { "TINYINT", SByteTypeMapping },
        { "INT1", SByteTypeMapping },
        { "SMALLINT", Int16TypeMapping },
        { "INT2", Int16TypeMapping },
        { "INT16", Int16TypeMapping },
        { "SHORT", Int16TypeMapping },
        { "INTEGER", Int32TypeMapping },
        { "INT4", Int32TypeMapping },
        { "INT32", Int32TypeMapping },
        { "INT", Int32TypeMapping },
        { "SIGNED", Int32TypeMapping },
        { "BIGINT", Int64TypeMapping },
        { "INT8", Int64TypeMapping },
        { "INT64", Int64TypeMapping },
        { "LONG", Int64TypeMapping },

        // Unsigned integers. DuckDB's UINTn aliases use bits.
        { "UTINYINT", ByteTypeMapping },
        { "UINT8", ByteTypeMapping },
        { "USMALLINT", UInt16TypeMapping },
        { "UINT16", UInt16TypeMapping },
        { "UINTEGER", UInt32TypeMapping },
        { "UINT32", UInt32TypeMapping },
        { "UBIGINT", UInt64TypeMapping },
        { "UINT64", UInt64TypeMapping },

        // Floating point and fixed precision
        { "FLOAT", FloatTypeMapping },
        { "FLOAT4", FloatTypeMapping },
        { "REAL", FloatTypeMapping },
        { "DOUBLE", DoubleTypeMapping },
        { "FLOAT8", DoubleTypeMapping },
        { "DECIMAL", DuckDBDecimalTypeMapping.Default },
        { "NUMERIC", DuckDBDecimalTypeMapping.Default },

        // Binary and text
        { "BLOB", DuckDBBlobTypeMapping.Default },
        { "BYTEA", DuckDBBlobTypeMapping.Default },
        { "BINARY", DuckDBBlobTypeMapping.Default },
        { "VARBINARY", DuckDBBlobTypeMapping.Default },
        { "VARCHAR", StringTypeMapping },
        { "CHAR", StringTypeMapping },
        { "BPCHAR", StringTypeMapping },
        { "TEXT", StringTypeMapping },
        { "STRING", StringTypeMapping },

        { "UUID", GuidTypeMapping },
        { "DATE", DateTypeMapping },

        // Timestamp Types
        { "TIMESTAMP_NS", DuckDBTimestampTypeMapping.TimestampNs },
        { "TIMESTAMP", DuckDBTimestampTypeMapping.Timestamp },
        { "DATETIME", DuckDBTimestampTypeMapping.Timestamp },
        { "TIMESTAMP WITHOUT TIME ZONE", DuckDBTimestampTypeMapping.Timestamp },
        { "TIMESTAMP_MS", DuckDBTimestampTypeMapping.TimestampMs },
        { "TIMESTAMP_S", DuckDBTimestampTypeMapping.TimestampS },
        { "TIMESTAMPTZ", DuckDBTimestampTypeMapping.TimestampTz },
        { "TIMESTAMP WITH TIME ZONE", DuckDBTimestampTypeMapping.TimestampTz },

        // Time types
        { "TIME", DuckDBTimeTypeMapping.Time },
        { "TIME WITHOUT TIME ZONE", DuckDBTimeTypeMapping.Time },
        { "TIMETZ", DuckDBTimeTypeMapping.TimeTz },
        { "TIME WITH TIME ZONE", DuckDBTimeTypeMapping.TimeTz },
        { "TIME_NS", DuckDBTimeTypeMapping.TimeNs },

        { "JSON", JsonString }
    };

    public DuckDBTypeMappingSource(TypeMappingSourceDependencies dependencies, RelationalTypeMappingSourceDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var mapping = base.FindMapping(mappingInfo)
                      ?? (IsMapMappingRequest(mappingInfo)
                          ? FindMapMapping(mappingInfo)
                          : IsStoreTypeOnlyListRequest(mappingInfo)
                              ? FindCollectionMapping(mappingInfo.StoreTypeName!.Trim(), null, null, null)
                              : FindRawMapping(mappingInfo)?.Clone(mappingInfo)
                                ?? FindRowValueMapping(mappingInfo)?.Clone(mappingInfo));

        return mapping != null && mappingInfo.StoreTypeName != null
            ? mapping.WithStoreTypeAndSize(mappingInfo.StoreTypeName, null)
            : mapping;
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping? FindCollectionMapping(
        RelationalTypeMappingInfo info,
        Type? modelType,
        Type? providerType,
        CoreTypeMapping? elementMapping)
    {
        var result = FindCollectionMapping(info.StoreTypeName, modelType, providerType, elementMapping);

        if (result is null && modelType is not null)
        {
            var elemType = modelType.TryGetElementType(typeof(IEnumerable<>)) ?? modelType.GetElementType();

            if (elemType == typeof(object))
            {
                return base.FindCollectionMapping(info, modelType, providerType, elementMapping);
            }
        }

        return result;
    }

    [EntityFrameworkInternal]
    public virtual RelationalTypeMapping? FindCollectionMapping(
        string? storeType,
        Type? modelClrType,
        Type? providerClrType,
        CoreTypeMapping? elementMapping)
    {
        if (elementMapping is not null and not RelationalTypeMapping)
        {
            return null;
        }

        Type concreteCollectionType;
        Type? elementType = null;

        if (modelClrType is not null)
        {
            elementType = modelClrType.TryGetElementType(typeof(IEnumerable<>)) ?? modelClrType.GetElementType();

            if (elementType is null || elementType == modelClrType || modelClrType.GetGenericTypeImplementations(typeof(IDictionary<,>)).Any())
            {
                return null;
            }
        }

        switch (storeType)
        {
            case null:
                {
                    if (modelClrType is null)
                    {
                        return null;
                    }

                    Debug.Assert(elementType is not null, "elementClrType is null");

                    if (elementType == typeof(object))
                    {
                        return null;
                    }

                    var relationalElementMapping = elementMapping as RelationalTypeMapping ?? FindMapping(elementType);
                    if (!IsListElementMapping(relationalElementMapping))
                    {
                        return null;
                    }

                    concreteCollectionType = FindTypeToInstantiate(modelClrType, elementType);

                    return (DuckDBArrayTypeMapping)Activator.CreateInstance(
                        typeof(DuckDBArrayTypeMapping<,,>).MakeGenericType(modelClrType, concreteCollectionType, elementType),
                        relationalElementMapping)!;
                }

            case var _ when storeType.EndsWith("[]", StringComparison.Ordinal):
                {
                    var elementStoreType = storeType.Substring(0, storeType.Length - 2);

                    var relationalElementMapping = elementMapping as RelationalTypeMapping
                        ?? (elementType is null
                            ? FindMapping(elementStoreType)
                            : FindMapping(elementType, elementStoreType));
                    if (!IsListElementMapping(relationalElementMapping))
                    {
                        return null;
                    }

                    if (modelClrType is null)
                    {
                        elementType = relationalElementMapping.ClrType;
                        modelClrType = concreteCollectionType = typeof(List<>).MakeGenericType(elementType);
                    }
                    else
                    {
                        concreteCollectionType = FindTypeToInstantiate(modelClrType, elementType!);
                        Debug.Assert(elementType is not null, "elementType is null");
                    }

                    return (DuckDBArrayTypeMapping)Activator.CreateInstance(
                        typeof(DuckDBArrayTypeMapping<,,>).MakeGenericType(modelClrType, concreteCollectionType, elementType),
                        storeType, relationalElementMapping)!;
                }

#pragma warning disable EF1001 // SelectExpression constructors are pubternal

            case "JSON" or "json" when modelClrType is not null:
                return base.FindCollectionMapping(
                    new RelationalTypeMappingInfo(
                        modelClrType, (RelationalTypeMapping?)elementMapping, storeTypeName: storeType, storeTypeNameBase: storeType),
                    modelClrType,
                    providerClrType,
                    elementMapping);
#pragma warning restore EF1001 // SelectExpression constructors are pubternal

            default:
                return null;
        }

        // A LIST element is either a scalar mapping or another native LIST, which gives nested lists such as
        // INTEGER[][]. JSON collections and MAP values cannot be bound as list elements by DuckDB.NET.
        static bool IsListElementMapping([NotNullWhen(true)] RelationalTypeMapping? mapping)
            => mapping is { ElementTypeMapping: null } and not DuckDBMapTypeMapping
                || mapping is DuckDBArrayTypeMapping { ElementTypeMapping: var nested } && IsListElementMapping(nested);

        static Type FindTypeToInstantiate(Type collectionType, Type elementType)
        {
            if (collectionType.IsArray)
            {
                return collectionType;
            }

            var listOfT = typeof(List<>).MakeGenericType(elementType);

            if (collectionType.IsAssignableFrom(listOfT))
            {
                if (!collectionType.IsAbstract)
                {
                    var constructor = collectionType.GetDeclaredConstructor(null);
                    if (constructor?.IsPublic == true)
                    {
                        return collectionType;
                    }
                }

                return listOfT;
            }

            return collectionType;
        }
    }

    // A MAP store type never falls through to the affinity rules, which would otherwise match MAP(VARCHAR, INTEGER)
    // as an integer.
    private static bool IsMapMappingRequest(in RelationalTypeMappingInfo mappingInfo)
        => mappingInfo.StoreTypeName is { } storeTypeName
            ? TryParseMapStoreType(storeTypeName, out _, out _)
                || string.Equals(mappingInfo.StoreTypeNameBase, "MAP", StringComparison.OrdinalIgnoreCase)
            : mappingInfo.ClrType is { } clrType && TryGetMapEntryTypes(clrType, out _, out _);

    // Without a CLR type, a LIST store type such as INTEGER[] must resolve as a list rather than reach the affinity
    // rules, which would match its element name and return the scalar mapping.
    private static bool IsStoreTypeOnlyListRequest(in RelationalTypeMappingInfo mappingInfo)
        => mappingInfo.ClrType is null
            && mappingInfo.StoreTypeName?.TrimEnd().EndsWith("[]", StringComparison.Ordinal) == true;

    private RelationalTypeMapping? FindMapMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType;
        Type? keyClrType = null;
        Type? valueClrType = null;
        if (clrType is not null && !TryGetMapEntryTypes(clrType, out keyClrType, out valueClrType))
        {
            return null;
        }

        string? keyStoreType = null;
        string? valueStoreType = null;
        if (mappingInfo.StoreTypeName is { } storeTypeName
            && !TryParseMapStoreType(storeTypeName, out keyStoreType, out valueStoreType))
        {
            return null;
        }

        // DuckDB MAP keys are never NULL, so a nullable key type has no faithful mapping.
        if (keyClrType?.IsNullableValueType() == true)
        {
            return null;
        }

        var keyMapping = FindMapEntryMapping(keyClrType, keyStoreType);
        var valueMapping = FindMapEntryMapping(valueClrType, valueStoreType);
        if (keyMapping is null || valueMapping is null)
        {
            return null;
        }

        keyClrType ??= keyMapping.ClrType;

        // A store-type-only lookup, as used by scaffolding, cannot know the values are non-null.
        valueClrType ??= valueMapping.ClrType.MakeNullable();
        clrType ??= typeof(Dictionary<,>).MakeGenericType(keyClrType, valueClrType);

        return (RelationalTypeMapping)Activator.CreateInstance(
            typeof(DuckDBMapTypeMapping<,,>).MakeGenericType(clrType, keyClrType, valueClrType),
            mappingInfo.StoreTypeName ?? DuckDBMapTypeMapping.CreateStoreType(keyMapping, valueMapping),
            keyMapping,
            valueMapping)!;
    }

    private RelationalTypeMapping? FindMapEntryMapping(Type? clrType, string? storeType)
    {
        var mapping = (clrType, storeType) switch
        {
            (null, null) => null,
            (null, not null) => FindMapping(storeType),
            (not null, null) => FindMapping(clrType),
            _ => FindMapping(clrType, storeType)
        };

        return mapping is { ElementTypeMapping: null } and not DuckDBMapTypeMapping
            && DuckDBMapTypeMapping.IsSupportedElementType(mapping.Converter?.ProviderClrType ?? mapping.ClrType)
                ? mapping
                : null;
    }

    private static bool TryGetMapEntryTypes(
        Type clrType,
        [NotNullWhen(true)] out Type? keyType,
        [NotNullWhen(true)] out Type? valueType)
    {
        if (clrType.IsGenericType
            && clrType.GetGenericTypeDefinition() is var definition
            && (definition == typeof(Dictionary<,>)
                || definition == typeof(IDictionary<,>)
                || definition == typeof(IReadOnlyDictionary<,>)))
        {
            var arguments = clrType.GetGenericArguments();
            keyType = arguments[0];
            valueType = arguments[1];
            return true;
        }

        keyType = null;
        valueType = null;
        return false;
    }

    internal static bool TryParseMapStoreType(
        string storeType,
        [NotNullWhen(true)] out string? keyStoreType,
        [NotNullWhen(true)] out string? valueStoreType)
    {
        keyStoreType = null;
        valueStoreType = null;

        var trimmed = storeType.Trim();
        if (!trimmed.StartsWith("MAP", StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith(')'))
        {
            return false;
        }

        var open = trimmed.IndexOf('(');
        if (open < 0 || !string.IsNullOrWhiteSpace(trimmed[3..open]))
        {
            return false;
        }

        // Split at the single top-level comma; DECIMAL(18, 2) and nested types keep their own commas.
        var inner = trimmed[(open + 1)..^1];
        var depth = 0;
        var separator = -1;
        for (var i = 0; i < inner.Length; i++)
        {
            switch (inner[i])
            {
                case '(':
                    depth++;
                    break;
                case ')' when --depth < 0:
                    return false;
                case ',' when depth == 0:
                    if (separator >= 0)
                    {
                        return false;
                    }

                    separator = i;
                    break;
            }
        }

        if (depth != 0 || separator < 0)
        {
            return false;
        }

        keyStoreType = inner[..separator].Trim();
        valueStoreType = inner[(separator + 1)..].Trim();
        return keyStoreType.Length > 0 && valueStoreType.Length > 0;
    }

    protected virtual RelationalTypeMapping? FindRowValueMapping(in RelationalTypeMappingInfo mappingInfo)
        => mappingInfo.ClrType is { } clrType
           && clrType.IsAssignableTo(typeof(ITuple))
            ? new DuckDBRowValueTypeMapping(clrType)
            : null;

    private RelationalTypeMapping? FindRawMapping(RelationalTypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType;
        if (clrType == typeof(byte[]) && mappingInfo.ElementTypeMapping != null)
        {
            return null;
        }

        var storeTypeName = mappingInfo.StoreTypeName;
        var storeTypeNameBase = mappingInfo.StoreTypeNameBase;

        if (clrType != null && ClrTypeMappings.TryGetValue(clrType, out var mapping))
        {
            if (storeTypeName != null)
            {
                if (mapping.StoreType.Equals(storeTypeName, StringComparison.OrdinalIgnoreCase)
                    || mapping.StoreTypeNameBase.Equals(storeTypeName, StringComparison.OrdinalIgnoreCase))
                {
                    return mapping;
                }

                if (TryGetBuiltInStoreTypeMapping(storeTypeName, storeTypeNameBase, out var storeMapping)
                    && storeMapping.ClrType.UnwrapNullableType() != clrType)
                {
                    return null;
                }

                var affinityMapping = _typeRules.Select(r => r(storeTypeName)).FirstOrDefault(r => r != null);
                if (affinityMapping != null && affinityMapping.ClrType.UnwrapNullableType() != clrType)
                {
                    return null;
                }
            }

            return mapping;
        }

        if (storeTypeName != null
            && TryGetBuiltInStoreTypeMapping(storeTypeName, storeTypeNameBase, out mapping)
            && (clrType == null || mapping.ClrType.UnwrapNullableType() == clrType))
        {
            return mapping;
        }

        if (storeTypeName != null)
        {
            var affinityTypeMapping = _typeRules.Select(r => r(storeTypeName)).FirstOrDefault(r => r != null);

            if (affinityTypeMapping != null)
            {
                return clrType == null || affinityTypeMapping.ClrType.UnwrapNullableType() == clrType
                    ? affinityTypeMapping
                    : null;
            }

            if (clrType == null || clrType == typeof(byte[]))
            {
                return DuckDBBlobTypeMapping.Default;
            }
        }

        return null;

    }

    internal static bool TryGetBuiltInStoreTypeMapping(
        string storeTypeName,
        string? storeTypeNameBase,
        out RelationalTypeMapping mapping)
        => StoreTypeMappings.TryGetValue(storeTypeName, out mapping!)
            || storeTypeNameBase is not null
            && StoreTypeMappings.TryGetValue(storeTypeNameBase, out mapping!);

    private readonly Func<string, RelationalTypeMapping?>[] _typeRules =
    [
        name => Contains(name, "INT")
            ? Int32TypeMapping
            : null,
        name => Contains(name, "CHAR")
                || Contains(name, "BPCHAR")
                || Contains(name, "TEXT")
                || Contains(name, "STRING")
            ? StringTypeMapping
            : null,
        name => Contains(name, "BLOB")
            ? DuckDBBlobTypeMapping.Default
            : null,
        name => Contains(name, "REAL")
                || Contains(name, "FLOAT")
            ? FloatTypeMapping
            : null
    ];

    private static bool Contains(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}