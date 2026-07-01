using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBStructuralJsonTypeMapping : JsonTypeMapping
{
    private static readonly MethodInfo GetStringMethod
        = typeof(DbDataReader).GetRuntimeMethod(nameof(DbDataReader.GetString), [typeof(int)])!;

    private static readonly PropertyInfo UTF8Property
        = typeof(Encoding).GetProperty(nameof(Encoding.UTF8))!;

    private static readonly MethodInfo EncodingGetBytesMethod
        = typeof(Encoding).GetMethod(nameof(Encoding.GetBytes), [typeof(string)])!;

    private static readonly ConstructorInfo MemoryStreamConstructor
        = typeof(MemoryStream).GetConstructor([typeof(byte[])])!;

    public DuckDBStructuralJsonTypeMapping()
        : base("JSON", typeof(JsonTypePlaceholder), System.Data.DbType.String)
    {
    }

    protected DuckDBStructuralJsonTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new DuckDBStructuralJsonTypeMapping(parameters);
    }

    /// <inheritdoc />
    public override MethodInfo GetDataReaderMethod()
    {
        return GetStringMethod;
    }

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
    {
        ((DuckDBParameter)parameter).RemoveDollarSign();
        base.ConfigureParameter(parameter);
    }

    /// <inheritdoc />
    public override Expression CustomizeDataReaderExpression(Expression expression)
        => Expression.New(
            MemoryStreamConstructor,
            Expression.Call(
                Expression.Property(null, UTF8Property),
                EncodingGetBytesMethod,
                expression));

    protected virtual string EscapeSqlLiteral(string literal)
        => literal.Replace("'", "''");

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
        => $"'{EscapeSqlLiteral((string)value)}'";
}
