namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>
///     The transformation applied to an aggregate-root property before it becomes a Hive partition key.
/// </summary>
public enum TierPartitionTransform
{
    /// <summary>Use the mapped property value directly.</summary>
    Value,

    /// <summary>Bucket a date/time property by calendar year.</summary>
    Year,

    /// <summary>Bucket a date/time property by calendar month.</summary>
    Month,

    /// <summary>Bucket a date/time property by calendar day.</summary>
    Day,
}
