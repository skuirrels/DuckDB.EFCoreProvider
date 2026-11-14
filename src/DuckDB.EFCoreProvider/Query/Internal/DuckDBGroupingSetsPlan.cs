namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public enum DuckDBGroupingSetsKind
{
    /// <summary>Renders <c>GROUP BY ROLLUP (...)</c>.</summary>
    Rollup,

    /// <summary>Renders <c>GROUP BY CUBE (...)</c>.</summary>
    Cube,

    /// <summary>Renders <c>GROUP BY GROUPING SETS (...)</c> with explicit sets.</summary>
    GroupingSets
}

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     An immutable, validated description of a multi-level grouping. Key members are identified by their
///     zero-based position in the grouping key, which is also the order in which EF Core emits GROUP BY terms.
/// </remarks>
public sealed class DuckDBGroupingSetsPlan : IEquatable<DuckDBGroupingSetsPlan>
{
    /// <summary>Creates a plan.</summary>
    public DuckDBGroupingSetsPlan(DuckDBGroupingSetsKind kind, int keyMemberCount, IReadOnlyList<IReadOnlyList<int>> sets)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keyMemberCount, 1);
        ArgumentNullException.ThrowIfNull(sets);

        if (kind == DuckDBGroupingSetsKind.GroupingSets)
        {
            if (sets.Count == 0)
            {
                throw new ArgumentException("GROUPING SETS requires at least one grouping set.", nameof(sets));
            }

            foreach (var set in sets)
            {
                foreach (var index in set)
                {
                    ArgumentOutOfRangeException.ThrowIfNegative(index);
                    ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, keyMemberCount);
                }
            }
        }
        else if (sets.Count != 0)
        {
            throw new ArgumentException("ROLLUP and CUBE derive their grouping sets from the key.", nameof(sets));
        }

        Kind = kind;
        KeyMemberCount = keyMemberCount;

        // Copy so that a caller cannot change a validated plan.
        Sets = sets.Select(set => (IReadOnlyList<int>)set.ToArray()).ToArray();
    }

    /// <summary>The grouping operator.</summary>
    public DuckDBGroupingSetsKind Kind { get; }

    /// <summary>The number of scalar members in the grouping key.</summary>
    public int KeyMemberCount { get; }

    /// <summary>For <see cref="DuckDBGroupingSetsKind.GroupingSets" />, the key-member positions of each set.</summary>
    public IReadOnlyList<IReadOnlyList<int>> Sets { get; }

    /// <summary>Returns whether the key member at <paramref name="index" /> can be NULL in a result row.</summary>
    public bool IsRolledUp(int index)
        => Kind != DuckDBGroupingSetsKind.GroupingSets || Sets.Any(set => !set.Contains(index));

    /// <inheritdoc />
    public bool Equals(DuckDBGroupingSetsPlan? other)
        => other is not null
            && Kind == other.Kind
            && KeyMemberCount == other.KeyMemberCount
            && Sets.Count == other.Sets.Count
            && Sets.Zip(other.Sets).All(pair => pair.First.SequenceEqual(pair.Second));

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => obj is DuckDBGroupingSetsPlan other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(KeyMemberCount);
        foreach (var set in Sets)
        {
            hash.Add(set.Count);
            foreach (var index in set)
            {
                hash.Add(index);
            }
        }

        return hash.ToHashCode();
    }
}
