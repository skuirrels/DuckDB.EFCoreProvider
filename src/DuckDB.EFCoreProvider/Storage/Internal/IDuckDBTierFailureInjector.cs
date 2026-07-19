namespace DuckDB.EFCoreProvider.Storage.Internal;

internal enum DuckDBTierFailurePoint
{
    BeforeCandidateRegistration,
    AfterCandidateRegistration,
    AfterCopy,
    AfterPublication,
    AfterNodeDelete,
    BeforeCopy,
    BeforeVerify,
    AfterVerify,
    BeforeCatalogueValidation,
    AfterCatalogueValidation,
    BeforePublication,
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