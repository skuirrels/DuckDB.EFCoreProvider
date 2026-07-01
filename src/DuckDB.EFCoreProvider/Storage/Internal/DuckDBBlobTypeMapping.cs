using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBBlobTypeMapping : ByteArrayTypeMapping
{
    public static new DuckDBBlobTypeMapping Default { get; } = new(DuckDBTypeMappingSource.BlobTypeName);

    public DuckDBBlobTypeMapping(string storeType, DbType? dbType = System.Data.DbType.Binary)
        : this(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(
                    typeof(byte[])),
                storeType,
                dbType: dbType))
    {
    }

    protected DuckDBBlobTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new DuckDBBlobTypeMapping(parameters);
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        ((DuckDBParameter)parameter).RemoveDollarSign();
        base.ConfigureParameter(parameter);
    }

    /// <inheritdoc />
    public override MethodInfo GetDataReaderMethod()
    {
        return typeof(DbDataReader).GetRuntimeMethod(nameof(DbDataReader.GetStream), [typeof(int)])!;
    }

    /// <inheritdoc />
    public override Expression CustomizeDataReaderExpression(Expression expression)
    {
        var streamType = typeof(Stream);
        var readStreamMethod = typeof(DuckDBBlobTypeMapping).GetMethod(nameof(ReadStream), BindingFlags.Static | BindingFlags.NonPublic)!;
        return Expression.Call(readStreamMethod, expression); 
    }

    private static byte[] ReadStream(Stream stream)
    {
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }
}
