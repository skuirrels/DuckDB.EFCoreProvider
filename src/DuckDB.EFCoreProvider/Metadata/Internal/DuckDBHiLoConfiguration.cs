namespace DuckDB.EFCoreProvider.Metadata.Internal;

internal static class DuckDBHiLoConfiguration
{
    internal static void Validate(string? tableName, int? blockSize)
    {
        if (tableName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        }

        if (blockSize is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(blockSize.Value, 1, nameof(blockSize));
        }
    }
}