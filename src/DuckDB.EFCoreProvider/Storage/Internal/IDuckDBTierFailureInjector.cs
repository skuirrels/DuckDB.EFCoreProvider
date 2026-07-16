namespace DuckDB.EFCoreProvider.Storage.Internal;

internal enum DuckDBTierFailurePoint
{
    AfterCopy,
    AfterPublication,
    AfterNodeDelete,
}

internal interface IDuckDBTierFailureInjector
{
    void ThrowIfRequested(DuckDBTierFailurePoint point, string? table);
}

internal sealed class DuckDBTierFailureInjector : IDuckDBTierFailureInjector
{
    public void ThrowIfRequested(DuckDBTierFailurePoint point, string? table)
    {
    }
}
