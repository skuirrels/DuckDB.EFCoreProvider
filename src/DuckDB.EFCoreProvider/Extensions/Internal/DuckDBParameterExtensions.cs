using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Storage;

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

    public static DuckDBParameter ConfigureNameAndMetadata(
        this DuckDBParameter parameter,
        RelationalTypeMapping typeMapping)
    {
        parameter.RemoveDollarSign();
        DuckDBParameterMetadataRegistry.Register(parameter, typeMapping);
        return parameter;
    }
}