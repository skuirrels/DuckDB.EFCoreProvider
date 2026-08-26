using DuckDB.NET.Data;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal static class DuckDBConnectionHelper
{
    public static DuckDBConnection Require(DbConnection connection)
        => connection as DuckDBConnection
            ?? throw new NotSupportedException(
                $"This DuckDB-native operation requires a DuckDBConnection, but found '{connection.GetType().Name}'.");
}
