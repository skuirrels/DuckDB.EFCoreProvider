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
public class DuckDBConvertMethodTranslator : IMethodCallTranslator
{
    // The single-string Parse overloads; DuckDB's CAST accepts the same invariant numeric text.
    private static readonly HashSet<Type> ParseableNumericTypes =
    [
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(decimal), typeof(double), typeof(float)
    ];

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    public DuckDBConvertMethodTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.DeclaringType == typeof(Convert) && arguments.Count == 1)
        {
            return method.Name switch
            {
                nameof(Convert.ToBoolean) => _sqlExpressionFactory.Convert(arguments[0], typeof(bool)),
                nameof(Convert.ToByte) => _sqlExpressionFactory.Convert(arguments[0], typeof(byte)),
                nameof(Convert.ToDecimal) => _sqlExpressionFactory.Convert(arguments[0], typeof(decimal)),
                nameof(Convert.ToDouble) => _sqlExpressionFactory.Convert(arguments[0], typeof(double)),
                nameof(Convert.ToInt16) => _sqlExpressionFactory.Convert(arguments[0], typeof(short)),
                nameof(Convert.ToInt32) => _sqlExpressionFactory.Convert(arguments[0], typeof(int)),
                nameof(Convert.ToInt64) => _sqlExpressionFactory.Convert(arguments[0], typeof(long)),
                nameof(Convert.ToString) => _sqlExpressionFactory.Convert(arguments[0], typeof(string)),
                _ => null
            };
        }

        if (method.Name == nameof(int.Parse)
            && method.IsStatic
            && ParseableNumericTypes.Contains(method.DeclaringType!)
            && arguments.Count == 1
            && arguments[0].Type == typeof(string))
        {
            return _sqlExpressionFactory.Convert(arguments[0], method.DeclaringType!);
        }

        return null;
    }
}