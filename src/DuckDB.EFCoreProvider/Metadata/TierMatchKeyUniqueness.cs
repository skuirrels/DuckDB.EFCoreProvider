namespace DuckDB.EFCoreProvider.Metadata;

/// <summary>Describes how uniqueness of a tiered-storage match key is guaranteed.</summary>
public enum TierMatchKeyUniqueness
{
    /// <summary>The selected properties must be covered by an EF primary key, alternate key, or unique index.</summary>
    Model,

    /// <summary>
    ///     The application guarantees uniqueness outside the EF model. Archive operations still reject null match-key
    ///     values among rows selected for archival.
    /// </summary>
    ExternallyEnforced,
}