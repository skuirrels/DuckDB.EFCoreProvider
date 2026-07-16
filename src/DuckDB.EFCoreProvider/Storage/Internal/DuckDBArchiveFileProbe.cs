using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;

namespace DuckDB.EFCoreProvider.Storage.Internal;

internal sealed class DuckDBArchiveFileProbe : IDuckDBArchiveFileProbe
{
    public bool HasArchiveFiles(DuckDBConnection connection, string archivePath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = ArchiveFileExistenceSql(archivePath);
        return command.ExecuteScalar() is not null;
    }

    public IReadOnlyList<string> GetArchiveFiles(DuckDBConnection connection, string archivePath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = ArchiveFileListSql(archivePath);
        using var reader = command.ExecuteReader();
        var files = new List<string>();
        while (reader.Read())
        {
            files.Add(reader.GetString(0));
        }

        return files;
    }

    public DuckDBArchiveFileSummary GetArchiveFileSummary(
        DuckDBConnection connection,
        string archivePath,
        TierManifestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        using var summaryCommand = connection.CreateCommand();
        summaryCommand.CommandText = ArchiveFileSummarySql(archivePath);
        using var summaryReader = summaryCommand.ExecuteReader();
        summaryReader.Read();
        var fileCount = summaryReader.GetInt64(0);
        var totalBytes = summaryReader.GetInt64(1);

        if (options.Detail == TierManifestDetail.Summary || fileCount == 0)
        {
            return new DuckDBArchiveFileSummary(fileCount, totalBytes, [], IsTruncated: fileCount > 0);
        }

        var files = options.Detail == TierManifestDetail.AllFiles
            ? GetArchiveFiles(connection, archivePath)
            : GetArchiveFiles(connection, archivePath, options.MaxFilesPerNode);
        return new DuckDBArchiveFileSummary(
            fileCount,
            totalBytes,
            files,
            IsTruncated: files.Count < fileCount);
    }

    public IReadOnlyList<string> GetArchiveColumns(DuckDBConnection connection, string archivePath)
    {
        using var command = connection.CreateCommand();
        command.CommandText = ArchiveColumnListSql(archivePath);
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    internal static string ArchiveFileExistenceSql(string archivePath)
    {
        var glob = DuckDBTierControl.ReadGlob(archivePath).Replace("'", "''");
        return $"SELECT 1 FROM glob('{glob}') LIMIT 1;";
    }

    internal static string ArchiveFileListSql(string archivePath)
    {
        var glob = DuckDBTierControl.ReadGlob(archivePath).Replace("'", "''");
        return $"SELECT file FROM glob('{glob}') ORDER BY file;";
    }

    internal static string ArchiveFileListSql(string archivePath, int limit)
    {
        var glob = DuckDBTierControl.ReadGlob(archivePath).Replace("'", "''");
        return $"SELECT file FROM glob('{glob}') ORDER BY file LIMIT {limit};";
    }

    internal static string ArchiveFileSummarySql(string archivePath)
    {
        var glob = DuckDBTierControl.ReadGlob(archivePath).Replace("'", "''");
        return "SELECT COUNT(*), COALESCE(SUM(CAST(file_size_bytes AS BIGINT)), 0) "
               + $"FROM parquet_file_metadata('{glob}');";
    }

    internal static string ArchiveColumnListSql(string archivePath)
    {
        var glob = DuckDBTierControl.ReadGlob(archivePath).Replace("'", "''");
        return $"DESCRIBE SELECT * FROM read_parquet('{glob}', hive_partitioning = true, union_by_name = true);";
    }

    private static IReadOnlyList<string> GetArchiveFiles(
        DuckDBConnection connection,
        string archivePath,
        int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = ArchiveFileListSql(archivePath, limit);
        using var reader = command.ExecuteReader();
        var files = new List<string>(limit);
        while (reader.Read())
        {
            files.Add(reader.GetString(0));
        }

        return files;
    }
}