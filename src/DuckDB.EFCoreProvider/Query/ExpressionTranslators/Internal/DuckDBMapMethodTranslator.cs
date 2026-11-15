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
/// <remarks>
///     Translates the indexer and <c>ContainsKey</c> of a MAP-mapped dictionary. Unlike the CLR indexer, the SQL
///     lookup returns NULL for a missing key rather than throwing.
/// </remarks>
public class DuckDBMapMethodTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslator
{
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is not { TypeMapping: DuckDBMapTypeMapping mapping }
            || arguments is not [var key]
            || !IsDictionaryMethod(method))
        {
            return null;
        }

        var typedKey = sqlExpressionFactory.ApplyTypeMapping(key, mapping.KeyTypeMapping);
        return method.Name switch
        {
            "get_Item" => sqlExpressionFactory.Function(
                "map_extract_value",
                [instance, typedKey],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                method.ReturnType,
                mapping.ValueTypeMapping),
            "ContainsKey" => sqlExpressionFactory.Function(
                "map_contains",
                [instance, typedKey],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                typeof(bool)),
            _ => null
        };
    }

    private static bool IsDictionaryMethod(MethodInfo method)
        => method.DeclaringType is { IsGenericType: true } declaringType
            && declaringType.GetGenericTypeDefinition() is var definition
            && (definition == typeof(Dictionary<,>)
                || definition == typeof(IDictionary<,>)
                || definition == typeof(IReadOnlyDictionary<,>));
}