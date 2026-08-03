using System.Collections.Immutable;

namespace DuckDB.EFCoreProvider.Metadata.Internal;

internal sealed record DuckDBStructForeignKeyPath
{
    public DuckDBStructForeignKeyPath(string shadowPropertyName, IEnumerable<string> memberNames)
    {
        ShadowPropertyName = shadowPropertyName;
        MemberNames = memberNames.ToImmutableArray();
    }

    public string ShadowPropertyName { get; }

    public IReadOnlyList<string> MemberNames { get; }

    public static DuckDBStructForeignKeyPath Create(IReadOnlyList<string> memberNames)
    {
        ArgumentNullException.ThrowIfNull(memberNames);

        return new DuckDBStructForeignKeyPath(
            GetShadowPropertyName(memberNames),
            memberNames);
    }

    public static bool IsShadowProperty(string propertyName)
        => propertyName.StartsWith("__DuckDBStructForeignKey_", StringComparison.Ordinal);

    private static string GetShadowPropertyName(IReadOnlyList<string> memberNames)
        => "__DuckDBStructForeignKey_"
            + string.Join(
                "_",
                memberNames.Select(memberName => memberName.Replace("_", "__", StringComparison.Ordinal)));
}