using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     Rewrites the <see cref="DuckDBGroupingSetsQueryableExtensions" /> operators into an ordinary
///     <see cref="Queryable.GroupBy{TSource,TKey}(IQueryable{TSource},Expression{Func{TSource,TKey}})" /> whose key
///     is wrapped in a <see cref="DuckDBGroupingSetsKeyMarker" />. Navigation expansion then treats the key like any
///     other grouping key, and grouping translation reads the plan back from the marker.
/// </remarks>
public class DuckDBGroupingSetsPreprocessor : ExpressionVisitor
{
    private static readonly MethodInfo QueryableGroupByMethod = typeof(Queryable)
        .GetMethods()
        .Single(method => method.Name == nameof(Queryable.GroupBy)
            && method.GetParameters().Length == 2
            && method.GetGenericArguments().Length == 2);

    /// <inheritdoc />
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.DeclaringType != typeof(DuckDBGroupingSetsQueryableExtensions) || !node.Method.IsGenericMethod)
        {
            return base.VisitMethodCall(node);
        }

        var definition = node.Method.GetGenericMethodDefinition();
        DuckDBGroupingSetsKind kind;
        if (definition == DuckDBGroupingSetsQueryableExtensions.GroupByRollupMethod)
        {
            kind = DuckDBGroupingSetsKind.Rollup;
        }
        else if (definition == DuckDBGroupingSetsQueryableExtensions.GroupByCubeMethod)
        {
            kind = DuckDBGroupingSetsKind.Cube;
        }
        else if (definition == DuckDBGroupingSetsQueryableExtensions.GroupByGroupingSetsMethod)
        {
            kind = DuckDBGroupingSetsKind.GroupingSets;
        }
        else
        {
            return base.VisitMethodCall(node);
        }

        var source = Visit(node.Arguments[0]);
        var keySelector = Unquote(node.Arguments[1]);
        var groupingSets = kind == DuckDBGroupingSetsKind.GroupingSets
            ? UnwrapGroupingSets(node.Arguments[2])
            : [];

        var plan = DuckDBGroupingSetsPlanner.Plan(kind, keySelector, groupingSets);
        var genericArguments = node.Method.GetGenericArguments();

        var markedKeySelector = Expression.Lambda(
            Expression.Call(
                DuckDBGroupingSetsKeyMarker.MarkMethod.MakeGenericMethod(genericArguments[1]),
                keySelector.Body,
                Expression.Constant(plan)),
            keySelector.Parameters);

        return Expression.Call(
            QueryableGroupByMethod.MakeGenericMethod(genericArguments),
            source,
            Expression.Quote(markedKeySelector));
    }

    private static LambdaExpression[] UnwrapGroupingSets(Expression groupingSets)
        => groupingSets is NewArrayExpression { NodeType: ExpressionType.NewArrayInit } array
            ? array.Expressions.Select(Unquote).ToArray()
            : throw new InvalidOperationException(CoreStrings.TranslationFailed(groupingSets.ToString()));

    private static LambdaExpression Unquote(Expression expression)
        => expression is UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression quoted }
            ? quoted
            : (LambdaExpression)expression;
}

/// <summary>Carries a <see cref="DuckDBGroupingSetsPlan" /> on a grouping key from preprocessing to translation.</summary>
internal static class DuckDBGroupingSetsKeyMarker
{
    internal static readonly MethodInfo MarkMethod
        = typeof(DuckDBGroupingSetsKeyMarker).GetMethod(nameof(Mark))!;

    /// <summary>Marks a grouping key as multi-level. Only valid inside a translated query.</summary>
    public static TKey Mark<TKey>(TKey key, DuckDBGroupingSetsPlan plan)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DuckDBGroupingSetsQueryableExtensions)));

    /// <summary>Removes the marker from a grouping key selector.</summary>
    public static bool TryUnwrap(
        LambdaExpression keySelector,
        [NotNullWhen(true)] out LambdaExpression? unmarkedKeySelector,
        [NotNullWhen(true)] out DuckDBGroupingSetsPlan? plan)
    {
        if (keySelector.Body is MethodCallExpression { Method.IsGenericMethod: true } call
            && call.Method.GetGenericMethodDefinition() == MarkMethod
            && call.Arguments[1] is ConstantExpression { Value: DuckDBGroupingSetsPlan constantPlan })
        {
            unmarkedKeySelector = Expression.Lambda(call.Arguments[0], keySelector.Parameters);
            plan = constantPlan;
            return true;
        }

        unmarkedKeySelector = null;
        plan = null;
        return false;
    }
}
