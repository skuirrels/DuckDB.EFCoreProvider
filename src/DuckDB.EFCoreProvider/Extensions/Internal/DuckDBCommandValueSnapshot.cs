using System.Collections;
using System.Text.Json;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal static class DuckDBCommandValueSnapshot
{
    public static object? Create(object? value)
        => value switch
        {
            null => null,
            JsonDocument document => JsonDocument.Parse(document.RootElement.GetRawText()),
            JsonElement element => element.Clone(),
            Array array => CloneArray(array),
            IDictionary dictionary => CloneDictionary(dictionary),
            IList list => CloneList(list),
            MemoryStream stream => new MemoryStream(stream.ToArray(), writable: false),
            ICloneable cloneable => cloneable.Clone(),
            _ => value
        };

    private static Array CloneArray(Array source)
    {
        var clone = (Array)source.Clone();
        var indices = new int[source.Rank];
        CopyDimension(0);
        return clone;

        void CopyDimension(int dimension)
        {
            var lowerBound = source.GetLowerBound(dimension);
            var upperBound = source.GetUpperBound(dimension);
            for (var index = lowerBound; index <= upperBound; index++)
            {
                indices[dimension] = index;
                if (dimension + 1 == source.Rank)
                {
                    clone.SetValue(Create(source.GetValue(indices)), indices);
                }
                else
                {
                    CopyDimension(dimension + 1);
                }
            }
        }
    }

    private static object CloneDictionary(IDictionary source)
    {
        var clone = CreateMutableDictionary(source.GetType());

        foreach (DictionaryEntry entry in source)
        {
            clone.Add(Create(entry.Key)!, Create(entry.Value));
        }

        return clone;
    }

    private static object CloneList(IList source)
    {
        var clone = CreateMutableList(source.GetType());

        foreach (var item in source)
        {
            clone.Add(Create(item));
        }

        return clone;
    }

    private static IDictionary CreateMutableDictionary(Type sourceType)
    {
        if (TryCreate(sourceType) is IDictionary { IsReadOnly: false, IsFixedSize: false } exactClone)
        {
            return exactClone;
        }

        var dictionaryInterface = sourceType.GetInterfaces()
            .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictionaryInterface is not null)
        {
            var fallbackType = typeof(Dictionary<,>).MakeGenericType(dictionaryInterface.GetGenericArguments());
            return (IDictionary)Activator.CreateInstance(fallbackType)!;
        }

        return new Hashtable();
    }

    private static IList CreateMutableList(Type sourceType)
    {
        if (TryCreate(sourceType) is IList { IsReadOnly: false, IsFixedSize: false } exactClone)
        {
            return exactClone;
        }

        var listInterface = sourceType.GetInterfaces()
            .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>));
        if (listInterface is not null)
        {
            var fallbackType = typeof(List<>).MakeGenericType(listInterface.GetGenericArguments());
            return (IList)Activator.CreateInstance(fallbackType)!;
        }

        return new ArrayList();
    }

    private static object? TryCreate(Type type)
    {
        try
        {
            return Activator.CreateInstance(type);
        }
        catch (MissingMethodException)
        {
            return null;
        }
        catch (MemberAccessException)
        {
            return null;
        }
    }
}