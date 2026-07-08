using DuckDB.NET.Data;

namespace DuckDB.EFCoreProvider.Storage.Internal;

internal interface IDuckDBArchiveFileProbe
{
    bool HasArchiveFiles(DuckDBConnection connection, string archivePath);
}
