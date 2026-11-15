using DuckDB.EFCoreProvider.Query.Internal;
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
///     Translates an index into a native nested LIST, and Contains over an inner list, to a typed, one-based <see cref="DuckDBSqlExpressionFactory.ArrayIndex" />
///     node, so the inner list keeps its LIST mapping for further indexing and <c>Contains</c>.
/// </remarks>
public class DuckDBNestedListMethodTranslator(DuckDBSqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslator
{
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (!method.IsGenericMethod)
        {
            return null;
        }

        var definition = method.GetGenericMethodDefinition();

        // Navigation expansion leaves Contains over an inner list as an Enumerable call. Like the queryable
        // translation, array_contains() is used only when NULL cannot be both an element and the searched item.
        if (method.DeclaringType == typeof(Enumerable)
            && method.Name == nameof(Enumerable.Contains)
            && arguments is [{ TypeMapping: DuckDBArrayTypeMapping containsMapping } source, var item]
            && (!source.Type.GetSequenceType().IsNullableType()
                || !item.Type.IsNullableType()
                || item is SqlConstantExpression { Value: not null }))
        {
            return sqlExpressionFactory.Function(
                "array_contains",
                [source, sqlExpressionFactory.ApplyTypeMapping(item, containsMapping.ElementTypeMapping)],
                nullable: true,
                argumentsPropagateNullability: [true, true],
                typeof(bool));
        }

        if (definition != DuckDBNestedListElement.ElementAtMethod
            || arguments is not [{ TypeMapping: DuckDBArrayTypeMapping mapping } list, var index])
        {
            return null;
        }

        var oneBasedIndex = index is SqlConstantExpression { Value: int constant }
            ? sqlExpressionFactory.Constant(constant + 1)
            : sqlExpressionFactory.Add(index, sqlExpressionFactory.Constant(1));
        return sqlExpressionFactory.ArrayIndex(list, oneBasedIndex, nullable: true, mapping.ElementTypeMapping);
    }
}