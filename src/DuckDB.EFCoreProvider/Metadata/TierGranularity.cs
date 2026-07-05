namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     The time granularity used to hive-partition the cold Parquet archive of a tiered-storage entity.
/// </summary>
/// <remarks>
///     The granularity controls both the on-disk partition layout (for example
///     <c>archive/events/year=2024/month=03/</c>) and the boundary that an archive cutoff is aligned down to,
///     which guarantees each archived period is written whole in a single offload run.
/// </remarks>
public enum TierGranularity
{
    /// <summary>
    ///     Partition the archive by calendar month: <c>year=YYYY/month=M/</c>. A good default for most
    ///     workloads that retain months-to-years of hot data.
    /// </summary>
    Month = 0,

    /// <summary>
    ///     Partition the archive by calendar day: <c>year=YYYY/month=M/day=D/</c>. Produces more, smaller
    ///     partitions; suited to very high-volume data or short hot-retention windows.
    /// </summary>
    Day = 1,
}
