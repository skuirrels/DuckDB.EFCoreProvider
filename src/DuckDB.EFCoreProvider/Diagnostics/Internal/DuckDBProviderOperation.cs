namespace DuckDB.EFCoreProvider.Diagnostics.Internal;

internal enum DuckDBProviderOperation
{
    BulkInsert,
    Upsert,
    ParquetExport,
    TieredStorage,
    ExtensionLoad,
    DuckLakeAttachment,
    Count,
}