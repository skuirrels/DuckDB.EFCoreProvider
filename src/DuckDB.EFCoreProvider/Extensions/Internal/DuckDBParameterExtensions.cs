using System.Data.Common;
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

    /// <summary>
    ///     Applies DuckDB's name and native-metadata bookkeeping when <paramref name="parameter"/> is a real
    ///     <see cref="DuckDBParameter"/>, and is a no-op otherwise. Type mappings call this from
    ///     <c>ConfigureParameter</c>, which EF Core also invokes against a substituted ADO.NET provider's own
    ///     <see cref="DbParameter"/> implementation (e.g. under a fake/test connection) -- forcing the cast there
    ///     would throw <see cref="InvalidCastException"/> for every parameter of every mapped type. The base
    ///     type mapping's <c>Value</c>/<c>DbType</c> assignment still runs either way; only the DuckDB-native
    ///     metadata registration, which nothing but the real driver ever reads, is skipped for a non-DuckDB
    ///     parameter.
    /// </summary>
    public static DbParameter ConfigureNameAndMetadata(
        this DbParameter parameter,
        RelationalTypeMapping typeMapping)
    {
        if (parameter is DuckDBParameter duckDbParameter)
        {
            duckDbParameter.ConfigureNameAndMetadata(typeMapping);
        }

        return parameter;
    }
}