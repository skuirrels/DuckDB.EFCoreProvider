using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;
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
        { "INT8", UInt64TypeMapping },
        { "LONG", UInt64TypeMapping },
        { "BYTEA", DuckDBBlobTypeMapping.Default },
        { "BINARY", DuckDBBlobTypeMapping.Default },
        { "VARBINARY", DuckDBBlobTypeMapping.Default },
        { "BOOL", BooleanTypeMapping },
        { "LOGICAL", BooleanTypeMapping },
        { "FLOAT8", DoubleTypeMapping },
        { "FLOAT4", FloatTypeMapping },
        { "REAL", FloatTypeMapping },
        { "INT4", Int32TypeMapping },
        { "INT", Int32TypeMapping },
        { "SIGNED", Int32TypeMapping },
        { "INT2", Int16TypeMapping },
        { "SHORT", Int16TypeMapping },

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

        { "INT1", SByteTypeMapping },
        { "CHAR", StringTypeMapping },
        { "BPCHAR", StringTypeMapping },
        { "TEXT", StringTypeMapping },
        { "STRING", StringTypeMapping },
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
                      ?? FindRawMapping(mappingInfo)?.Clone(mappingInfo)
                      ?? FindRowValueMapping(mappingInfo)?.Clone(mappingInfo);

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
                if (relationalElementMapping is not { ElementTypeMapping: null })
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
                if (relationalElementMapping is not { ElementTypeMapping: null })
                {
                    return null;
                }

                if (relationalElementMapping is not null and not DuckDBArrayTypeMapping)
                {
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

                return null;
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

        if (clrType != null && ClrTypeMappings.TryGetValue(clrType, out var mapping))
        {
            if (storeTypeName != null)
            {
                if (mapping.StoreType.Equals(storeTypeName, StringComparison.OrdinalIgnoreCase)
                    || mapping.StoreTypeNameBase.Equals(storeTypeName, StringComparison.OrdinalIgnoreCase))
                {
                    return mapping;
                }

                if (StoreTypeMappings.TryGetValue(storeTypeName, out var storeMapping)
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
            && StoreTypeMappings.TryGetValue(storeTypeName, out mapping)
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
