namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>Non-secret technical identity of one root-scoped tiered-storage relationship binding.</summary>
/// <param name="BindingId">The deterministic bounded binding identifier.</param>
/// <param name="RootEntityType">The configured aggregate-root CLR type.</param>
/// <param name="ControlKey">The root-scoped provider control key.</param>
public readonly record struct TieredStorageBindingInfo(
    string BindingId,
    Type RootEntityType,
    string ControlKey);

internal static class TieredStorageBindingEvidence
{
    public static string Describe(TieredStorageBindingInfo binding)
        => $"'{Bound(binding.BindingId)}' "
           + $"(root '{Bound(binding.RootEntityType.Name)}', control '{Bound(binding.ControlKey)}')";

    private static string Bound(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return singleLine.Length <= 64 ? singleLine : singleLine[..61] + "...";
    }
}

/// <summary>
///     Raised before external writes or hot deletion when a physical child row is reachable through multiple
///     independently archived root bindings.
/// </summary>
public sealed class TierAmbiguousBindingException : InvalidOperationException
{
    /// <summary>Creates an ambiguity failure with bounded, non-secret binding evidence.</summary>
    public TierAmbiguousBindingException(
        string table,
        long ambiguousRows,
        IEnumerable<TieredStorageBindingInfo> bindings)
        : this(table, ambiguousRows, bindings.ToArray())
    {
    }

    private TierAmbiguousBindingException(
        string table,
        long ambiguousRows,
        IReadOnlyList<TieredStorageBindingInfo> bindings)
        : base(CreateMessage(table, ambiguousRows, bindings))
    {
        Table = table;
        AmbiguousRows = ambiguousRows;
        Bindings = bindings;
    }

    /// <summary>The physical child table containing ambiguous rows.</summary>
    public string Table { get; }

    /// <summary>The number of rows reachable through more than one configured root binding.</summary>
    public long AmbiguousRows { get; }

    /// <summary>The configured bindings that can reach the table.</summary>
    public IReadOnlyList<TieredStorageBindingInfo> Bindings { get; }

    private static string CreateMessage(
        string table,
        long ambiguousRows,
        IReadOnlyList<TieredStorageBindingInfo> bindings)
    {
        const int evidenceLimit = 8;
        var evidence = string.Join(
            ", ",
            bindings.Take(evidenceLimit).Select(
                TieredStorageBindingEvidence.Describe));
        if (bindings.Count > evidenceLimit)
        {
            evidence += $", +{bindings.Count - evidenceLimit} more";
        }

        return $"Tiered-storage table '{table}' has {ambiguousRows} row(s) reachable through multiple root "
               + $"bindings: {evidence}. The provider cannot choose archive ownership; no Parquet files were "
               + "written and no hot rows were deleted.";
    }
}
