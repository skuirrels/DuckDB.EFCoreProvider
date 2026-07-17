using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DuckDB.EFCoreProvider.Storage.Internal;

internal static class DuckDBTierPartitionContract
{
    internal const string ColumnPrefix = "__duckdb_tier_contract_v1_";

    public static string GetValidationColumn(IReadOnlyList<DuckDBTierPartitionColumn> partitions)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        if (partitions.Count == 0)
        {
            throw new ArgumentException("A tier partition contract requires at least one partition.", nameof(partitions));
        }

        var canonical = new StringBuilder("v1;")
            .Append(partitions.Count.ToString(CultureInfo.InvariantCulture))
            .Append(';');
        foreach (var partition in partitions)
        {
            AppendField(canonical, partition.SourceColumn);
            AppendField(canonical, partition.Name);
            AppendField(canonical, partition.StoreType.Trim().ToUpperInvariant());
            canonical.Append(((int)partition.Transform).ToString(CultureInfo.InvariantCulture)).Append(';');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return ColumnPrefix + Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static void AppendField(StringBuilder builder, string value)
        => builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append(';');
}