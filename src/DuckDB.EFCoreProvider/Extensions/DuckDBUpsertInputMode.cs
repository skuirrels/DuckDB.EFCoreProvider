namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Describes whether an Upsert input can contain repeated conflict-target values.
/// </summary>
public enum DuckDBUpsertInputMode
{
    /// <summary>
    ///     The input can contain duplicate conflict-target values. Updating shapes deterministically retain the
    ///     last occurrence in input order; key-only shapes retain the first inserted occurrence.
    /// </summary>
    MayContainDuplicates = 0,

    /// <summary>
    ///     The caller guarantees that every conflict-target value is distinct across the complete input. This
    ///     bypasses provider-side staging deduplication. The provider does not validate the guarantee.
    /// </summary>
    DistinctConflictTargets = 1,
}