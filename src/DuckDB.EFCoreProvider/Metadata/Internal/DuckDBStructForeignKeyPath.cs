using System.Collections.Immutable;
using System.Globalization;

namespace DuckDB.EFCoreProvider.Metadata.Internal;

internal sealed class DuckDBStructForeignKeyPath : IEquatable<DuckDBStructForeignKeyPath>
{
    public DuckDBStructForeignKeyPath(string shadowPropertyName, IEnumerable<string> memberNames)
    {
        ShadowPropertyName = shadowPropertyName;
        MemberNames = memberNames.ToImmutableArray();
    }

    public string ShadowPropertyName { get; }

    public IReadOnlyList<string> MemberNames { get; }

    /// <summary>
    ///     Two bindings are equivalent when they resolve to the same STRUCT member sequence. Member names are
    ///     compared structurally because the synthesized record equality would otherwise fall back to reference
    ///     identity for the <see cref="IReadOnlyList{T}"/> property.
    /// </summary>
    public bool Equals(DuckDBStructForeignKeyPath? other)
        => other is not null
            && string.Equals(ShadowPropertyName, other.ShadowPropertyName, StringComparison.Ordinal)
            && MemberNames.SequenceEqual(other.MemberNames);

    public override bool Equals(object? obj)
        => Equals(obj as DuckDBStructForeignKeyPath);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(ShadowPropertyName, StringComparer.Ordinal);
        foreach (var memberName in MemberNames)
        {
            hashCode.Add(memberName, StringComparer.Ordinal);
        }

        return hashCode.ToHashCode();
    }

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
                memberNames.Select(memberName =>
                    memberName.Length.ToString(CultureInfo.InvariantCulture) + "_" + memberName));
}