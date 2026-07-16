using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;

namespace DuckDB.EFCoreProvider.Storage.Internal;

internal interface IDuckDBArchiveFileProbe
{
    bool HasArchiveFiles(DuckDBConnection connection, string archivePath);

    IReadOnlyList<string> GetArchiveFiles(DuckDBConnection connection, string archivePath);

    DuckDBArchiveFileSummary GetArchiveFileSummary(
        DuckDBConnection connection,
        string archivePath,
        TierManifestOptions options);

    IReadOnlyList<string> GetArchiveColumns(DuckDBConnection connection, string archivePath);
}

internal readonly record struct DuckDBArchiveFileSummary(
    long FileCount,
    long TotalBytes,
    IReadOnlyList<string> Files,
    bool IsTruncated);