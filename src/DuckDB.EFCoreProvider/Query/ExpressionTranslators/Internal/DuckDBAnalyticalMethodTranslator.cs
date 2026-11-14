using DuckDB.EFCoreProvider.Extensions.DbFunctionsExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.ExpressionTranslators.Internal;

/// <summary>Translates DuckDB-specific scalar analytical helpers, including <c>GROUPING</c>.</summary>
internal sealed class DuckDBAnalyticalMethodTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslator
{
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method.DeclaringType != typeof(DuckDBDbFunctionsExtensions))
        {
            return null;
        }

        if (method.Name == nameof(DuckDBDbFunctionsExtensions.Grouping))
        {
            var keyMembers = arguments.Skip(1).ToArray();

            // DuckDB returns BIGINT; the cast keeps the documented int result exact for reading.
            return sqlExpressionFactory.Convert(
                sqlExpressionFactory.Function(
                    "grouping",
                    keyMembers,
                    nullable: false,
                    argumentsPropagateNullability: keyMembers.Select(_ => false),
                    typeof(long)),
                typeof(int));
        }

        if (method.Name != nameof(DuckDBDbFunctionsExtensions.SplitPart))
        {
            return null;
        }

        return sqlExpressionFactory.Function(
            "split_part",
            [arguments[1], arguments[2], arguments[3]],
            nullable: true,
            argumentsPropagateNullability: [true, true, true],
            typeof(string));
    }
}