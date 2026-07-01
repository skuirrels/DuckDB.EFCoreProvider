using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.ExpressionTranslators.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBRandomMethodTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo Random = typeof(DbFunctionsExtensions).GetRuntimeMethod(nameof(DbFunctionsExtensions.Random), [typeof(DbFunctions)])!;
    
    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBRandomMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method == Random)
        {
            return _sqlExpressionFactory.Function(
                name: "random",
                arguments: [],
                nullable: false,
                argumentsPropagateNullability: [],
                returnType: typeof(double));
        }

        return null;
    }
}
