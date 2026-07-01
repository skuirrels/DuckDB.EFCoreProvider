using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.ExpressionTranslators.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBGuidMethodTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo NewGuid = typeof(Guid).GetMethod(nameof(Guid.NewGuid), Type.EmptyTypes)!;
    private static readonly MethodInfo CreateVersion7 = typeof(Guid).GetMethod(nameof(Guid.CreateVersion7), Type.EmptyTypes)!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    public DuckDBGuidMethodTranslator(ISqlExpressionFactory sqlExpressionFactory, IRelationalTypeMappingSource typeMappingSource)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
        _typeMappingSource = typeMappingSource;
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method == NewGuid)
        {
            return _sqlExpressionFactory.Function(
                name: "uuidv4",
                arguments: [],
                nullable: false,
                argumentsPropagateNullability: [],
                returnType: typeof(Guid),
                typeMapping: _typeMappingSource.FindMapping(typeof(Guid)));
        }

        if (method == CreateVersion7)
        {
            return _sqlExpressionFactory.Function(
                name: "uuidv7",
                arguments: [],
                nullable: false,
                argumentsPropagateNullability: [],
                returnType: typeof(Guid),
                typeMapping: _typeMappingSource.FindMapping(typeof(Guid)));
        }

        return null;
    }
}
