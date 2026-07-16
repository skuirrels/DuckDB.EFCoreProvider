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

    internal static string ArchiveColumnListSql(string archivePath)
    {
        var glob = DuckDBTierControl.ReadGlob(archivePath).Replace("'", "''");
        return $"DESCRIBE SELECT * FROM read_parquet('{glob}', hive_partitioning = true, union_by_name = true);";
    }
}
