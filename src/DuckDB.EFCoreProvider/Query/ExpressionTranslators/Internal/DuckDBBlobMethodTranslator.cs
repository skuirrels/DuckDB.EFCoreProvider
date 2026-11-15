using DuckDB.EFCoreProvider.Storage.Internal;
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
public class DuckDBBlobMethodTranslator : IMethodCallTranslator
{
    private readonly ISqlExpressionFactory? _sqlExpressionFactory;

    public DuckDBBlobMethodTranslator()
    {
    }

    public DuckDBBlobMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        // byte[].Any() is true for a non-empty BLOB. Like byte[].Length, this applies only to BLOB-mapped values,
        // not byte arrays mapped to a native list.
        if (_sqlExpressionFactory is not null
            && method.DeclaringType == typeof(Enumerable)
            && method.Name == nameof(Enumerable.Any)
            && arguments is [{ Type: var type } blob]
            && type == typeof(byte[])
            && blob.TypeMapping is DuckDBBlobTypeMapping or null)
        {
            return _sqlExpressionFactory.GreaterThan(
                _sqlExpressionFactory.Function(
                    "octet_length",
                    [blob],
                    nullable: true,
                    argumentsPropagateNullability: [true],
                    typeof(int)),
                _sqlExpressionFactory.Constant(0));
        }

        return null;
    }
}