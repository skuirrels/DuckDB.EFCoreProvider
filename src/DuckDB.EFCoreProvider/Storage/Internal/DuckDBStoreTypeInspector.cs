using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Storage;

namespace DuckDB.EFCoreProvider.Storage.Internal;

internal sealed class DuckDBStoreTypeInspector(IRelationalTypeMappingSource typeMappingSource)
{
    private static readonly HashSet<string> RawReaderTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MAP", "UNION", "VARIANT", "HUGEINT", "UHUGEINT", "ENUM", "BIT", "INTERVAL"
    };

    public DuckDBStoreTypeMappingInfo Inspect(string storeType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeType);
        storeType = storeType.Trim();

        if (storeType.EndsWith("[]", StringComparison.Ordinal))
        {
            var element = Inspect(storeType[..^2]);
            if (element.Support != DuckDBStoreTypeSupport.ScalarProperty)
            {
                return new DuckDBStoreTypeMappingInfo(
                    storeType,
                    DuckDBStoreTypeSupport.Unsupported,
                    null,
                    null,
                    element);
            }

            var collectionClrType = typeof(List<>).MakeGenericType(element.ClrType!);
            var arrayMapping = typeMappingSource.FindMapping(collectionClrType, storeType);
            return arrayMapping is null
                ? new DuckDBStoreTypeMappingInfo(storeType, DuckDBStoreTypeSupport.Unsupported, null, null, element)
                : new DuckDBStoreTypeMappingInfo(
                    arrayMapping.StoreType,
                    DuckDBStoreTypeSupport.ScalarProperty,
                    collectionClrType,
                    arrayMapping.GetType().Name,
                    element);
        }

        if (TryGetFixedArrayElementType(storeType, out var fixedArrayElementType))
        {
            return new DuckDBStoreTypeMappingInfo(
                storeType,
                DuckDBStoreTypeSupport.RawReaderOnly,
                null,
                null,
                Inspect(fixedArrayElementType));
        }

        var baseType = GetBaseType(storeType);
        if (baseType.Equals("STRUCT", StringComparison.OrdinalIgnoreCase))
        {
            return new DuckDBStoreTypeMappingInfo(
                storeType,
                DuckDBStoreTypeSupport.ComplexProperty,
                null,
                null);
        }

        if (RawReaderTypes.Contains(baseType))
        {
            return new DuckDBStoreTypeMappingInfo(
                storeType,
                DuckDBStoreTypeSupport.RawReaderOnly,
                null,
                null);
        }

        var mapping = typeMappingSource.FindMapping(storeType);
        var isProviderPluginMapping = mapping?.GetType().Assembly != typeof(DuckDBTypeMappingSource).Assembly;
        var isBuiltInMapping = DuckDBTypeMappingSource.TryGetBuiltInStoreTypeMapping(baseType, null, out _);
        if (mapping is null || !isBuiltInMapping && !isProviderPluginMapping)
        {
            return new DuckDBStoreTypeMappingInfo(
                storeType,
                DuckDBStoreTypeSupport.Unsupported,
                null,
                null);
        }

        return new DuckDBStoreTypeMappingInfo(
            mapping.StoreType,
            DuckDBStoreTypeSupport.ScalarProperty,
            mapping.ClrType,
            mapping.GetType().Name);
    }

    private static string GetBaseType(string storeType)
    {
        var openParenthesis = storeType.IndexOf('(');
        return (openParenthesis < 0 ? storeType : storeType[..openParenthesis]).Trim();
    }

    private static bool TryGetFixedArrayElementType(string storeType, out string elementStoreType)
    {
        var openBracket = storeType.LastIndexOf('[');
        if (openBracket <= 0
            || !storeType.EndsWith(']')
            || !int.TryParse(storeType.AsSpan(openBracket + 1, storeType.Length - openBracket - 2), out var length)
            || length <= 0)
        {
            elementStoreType = string.Empty;
            return false;
        }

        elementStoreType = storeType[..openBracket].TrimEnd();
        return elementStoreType.Length > 0;
    }
}