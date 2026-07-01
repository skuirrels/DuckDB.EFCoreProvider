using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBRowValueTypeMapping : RelationalTypeMapping
{
    public DuckDBRowValueTypeMapping(Type clrType)
        : base(new RelationalTypeMappingParameters(new CoreTypeMappingParameters(clrType), storeType: "record"))
    {
    }

    protected DuckDBRowValueTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new DuckDBRowValueTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
        => throw new InvalidOperationException("GenerateNonNullSqlLiteral not supported on DuckDBRowValueTypeMapping");

    /// <inheritdoc />
    protected override void ConfigureParameter(DbParameter parameter)
        => throw new InvalidOperationException("ConfigureParameter not supported on DuckDBRowValueTypeMapping");
}
