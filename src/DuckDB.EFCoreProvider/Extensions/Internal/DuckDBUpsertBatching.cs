namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal static class DuckDBUpsertBatching
{
    internal const int MaxStagedCellCount = 100_000;
    internal const int StreamingInitialBatchCapacity = 500;

    // Use the width-aware cell budget as the default. This avoids repeatedly probing a large target index
    // in small fixed-size chunks while still bounding the staged work for wide entity shapes.
    internal const int DefaultRequestedBatchSize = MaxStagedCellCount;

    internal static int EffectiveBatchSize(int columnCount, int requestedBatchSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columnCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(requestedBatchSize, 1);

        return Math.Min(requestedBatchSize, Math.Max(1, MaxStagedCellCount / columnCount));
    }

    internal static int InitialBatchCapacity(int effectiveBatchSize, int? sourceCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(effectiveBatchSize, 1);

        if (sourceCount is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sourceCount.Value);
            return Math.Min(sourceCount.Value, effectiveBatchSize);
        }

        return Math.Min(StreamingInitialBatchCapacity, effectiveBatchSize);
    }
}