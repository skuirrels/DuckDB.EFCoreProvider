using System.Data;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal static class DbCommandExtensions
{
    public static DbParameter CreateParameter(
        this DbCommand command,
        string name,
        object? value,
        DbType? dbType = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        if (dbType.HasValue)
        {
            parameter.DbType = dbType.Value;
        }

        return parameter;
    }

    public static DbParameter CreateParameter(this DbCommand command, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.Value = value ?? DBNull.Value;
        return parameter;
    }

    public static DbParameter AddParameter(
        this DbCommand command,
        string name,
        object? value,
        DbType? dbType = null)
    {
        var parameter = command.CreateParameter(name, value, dbType);
        command.Parameters.Add(parameter);
        return parameter;
    }

    public static DbParameter AddParameter(this DbCommand command, object? value)
    {
        var parameter = command.CreateParameter(value);
        command.Parameters.Add(parameter);
        return parameter;
    }
}