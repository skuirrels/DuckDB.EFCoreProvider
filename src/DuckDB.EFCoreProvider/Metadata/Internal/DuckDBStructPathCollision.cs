namespace DuckDB.EFCoreProvider.Metadata.Internal;

/// <summary>
///     Detects physical STRUCT field paths where one leaf is also the prefix of another
///     path — i.e. a field used simultaneously as a scalar leaf and a nested parent node.
///     DuckDB resolves struct field names case-insensitively, so the comparison matches that.
/// </summary>
internal static class DuckDBStructPathCollision
{
    /// <summary>
    ///     Returns the first collision where one mapped path is a proper (case-insensitive)
    ///     prefix of another within the same STRUCT root, or <see langword="null" /> when the
    ///     shape is internally consistent.
    /// </summary>
    public static (string StructColumnName, IReadOnlyList<string> LeafPath, IReadOnlyList<string> NestedPath)? Find(
        IEnumerable<DuckDBStructFieldInfo> fields)
    {
        foreach (var group in fields.GroupBy(field => field.StructColumnName, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                for (var j = 0; j < ordered.Length; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    if (IsStrictPrefix(ordered[i].FieldPath, ordered[j].FieldPath))
                    {
                        return (group.Key, ordered[i].FieldPath, ordered[j].FieldPath);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Renders a physical STRUCT path as <c>root.nested.leaf</c>.</summary>
    public static string FormatPath(string structColumnName, IReadOnlyList<string> fieldPath)
        => string.Join(".", new[] { structColumnName }.Concat(fieldPath));

    private static bool IsStrictPrefix(IReadOnlyList<string> prefix, IReadOnlyList<string> path)
    {
        if (prefix.Count >= path.Count)
        {
            return false;
        }

        for (var i = 0; i < prefix.Count; i++)
        {
            if (!string.Equals(prefix[i], path[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}