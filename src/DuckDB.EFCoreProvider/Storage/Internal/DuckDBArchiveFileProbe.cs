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

    internal static string ArchiveFileExistenceSql(string archivePath)
    {
        var glob = DuckDBTierControl.ReadGlob(archivePath).Replace("'", "''");
        return $"SELECT 1 FROM glob('{glob}') LIMIT 1;";
    }
}
