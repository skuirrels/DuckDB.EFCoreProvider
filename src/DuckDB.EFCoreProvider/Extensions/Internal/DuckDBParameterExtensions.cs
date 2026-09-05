using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal static class DuckDBParameterExtensions
{
    public static string RemoveDollarSign(this string parameterName)
        => parameterName.StartsWith('$') ? parameterName[1..] : parameterName;

    public static DuckDBParameter RemoveDollarSign(this DuckDBParameter parameter)
    {
        if (parameter.ParameterName.StartsWith('$'))
        {
            parameter.ParameterName = parameter.ParameterName[1..];
        }

        return parameter;
    }

    public static DbParameter ConfigureNameAndMetadata(
        this DbParameter parameter,
        RelationalTypeMapping typeMapping)
    {
        if (parameter is DuckDBParameter duckDbParameter)
        {
            duckDbParameter.RemoveDollarSign();
        }

        DuckDBParameterMetadataRegistry.Register(parameter, typeMapping);
        return parameter;
    }
}