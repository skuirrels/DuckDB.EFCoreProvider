using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     Marks an index into a list of lists before navigation expansion, which cannot follow operations on an inner
///     list. The SQL translator renders it as a native one-based LIST index; client evaluation indexes the CLR value.
/// </summary>
internal static class DuckDBNestedListElement
{
    internal static readonly MethodInfo ElementAtMethod =
        typeof(DuckDBNestedListElement).GetMethod(nameof(ElementAt), BindingFlags.Static | BindingFlags.NonPublic)!;

    // Generic over the list type so the translated argument keeps the property's LIST mapping, without a cast.
    internal static T ElementAt<TList, T>(TList source, int index)
        where TList : IEnumerable<T>
        => source is IList<T> list ? list[index] : source.ElementAt(index);

    /// <summary>
    ///     Whether indexing <paramref name="source" /> should become a native LIST index: the source is a list of
    ///     lists, or is itself an inner list produced by this marker.
    /// </summary>
    internal static bool TryGetIndexedElementType(Expression source, [NotNullWhen(true)] out Type? elementType)
    {
        elementType = GetListElementType(source.Type);
        return elementType is not null
            && (GetListElementType(elementType) is not null
                || source is MethodCallExpression { Method.IsGenericMethod: true } call
                && call.Method.GetGenericMethodDefinition() == ElementAtMethod);
    }

    private static Type? GetListElementType(Type type)
        => type == typeof(string) || type == typeof(byte[])
            ? null
            : type.IsArray
                ? type.GetArrayRank() == 1 ? type.GetElementType() : null
                : type.TryGetElementType(typeof(IList<>)) ?? type.TryGetElementType(typeof(IReadOnlyList<>));
}