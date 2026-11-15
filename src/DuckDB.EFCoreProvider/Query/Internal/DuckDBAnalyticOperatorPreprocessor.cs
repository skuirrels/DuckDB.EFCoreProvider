using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     Navigation expansion only understands standard query operators, so the QUALIFY and ASOF operators are
///     rewritten into <c>Where</c>, <c>Join</c> and <c>LeftJoin</c> calls whose lambdas carry the validated plan in
///     marker methods. Translation recognises the markers and produces the native SQL.
/// </remarks>
public class DuckDBAnalyticOperatorPreprocessor : ExpressionVisitor
{
    /// <inheritdoc />
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (!node.Method.IsGenericMethod)
        {
            return base.VisitMethodCall(node);
        }

        if (node.Method.DeclaringType == typeof(DuckDBQualifyQueryableExtensions))
        {
            return RewriteQualify(node);
        }

        if (node.Method.DeclaringType == typeof(DuckDBAsOfJoinQueryableExtensions))
        {
            return RewriteAsOfJoin(node);
        }

        return base.VisitMethodCall(node);
    }

    private Expression RewriteQualify(MethodCallExpression node)
    {
        var function = DuckDBQualifyPlanner.GetFunction(node.Method.GetGenericMethodDefinition());
        var source = Visit(node.Arguments[0]);
        var partitionBy = DuckDBQualifyPlanner.Unquote(node.Arguments[1]);
        var predicate = DuckDBQualifyPlanner.Unquote(node.Arguments[2]);
        DuckDBQualifyPlanner.ValidatePredicate(predicate);
        var orderings = DuckDBQualifyPlanner.GetWindowOrderings(source);

        var row = partitionBy.Parameters[0];
        var plan = new DuckDBQualifyPlan(function, orderings.Select(ordering => ordering.Descending).ToArray());
        var orderKeys = orderings.Select(
            ordering => (Expression)Expression.Convert(
                ReplacingExpressionVisitor.Replace(ordering.KeySelector.Parameters[0], row, ordering.KeySelector.Body),
                typeof(object)));

        var rank = Expression.Call(
            DuckDBQualifyMarker.RankMethod.MakeGenericMethod(partitionBy.ReturnType),
            partitionBy.Body,
            Expression.NewArrayInit(typeof(object), orderKeys),
            Expression.Constant(plan));
        var qualify = Expression.Call(
            DuckDBQualifyMarker.QualifyMethod,
            ReplacingExpressionVisitor.Replace(predicate.Parameters[0], rank, predicate.Body));

        var elementType = node.Method.GetGenericArguments()[0];
        return Expression.Call(
            QueryableMethods.Where.MakeGenericMethod(elementType),
            source,
            Expression.Quote(Expression.Lambda(qualify, row)));
    }

    private Expression RewriteAsOfJoin(MethodCallExpression node)
    {
        var definition = node.Method.GetGenericMethodDefinition();
        var isLeft = definition == DuckDBAsOfJoinQueryableExtensions.LeftAsOfJoinMethod;
        if (!isLeft && definition != DuckDBAsOfJoinQueryableExtensions.AsOfJoinMethod)
        {
            return base.VisitMethodCall(node);
        }

        var outer = Visit(node.Arguments[0]);
        var inner = Visit(node.Arguments[1]);
        var condition = DuckDBQualifyPlanner.Unquote(node.Arguments[2]);
        var resultSelector = DuckDBQualifyPlanner.Unquote(node.Arguments[3]);

        var (plan, outerKeySelector, innerKeySelector) = DuckDBAsOfJoinPlanner.CreateKeySelectors(condition);
        var keyType = outerKeySelector.ReturnType;
        var markedOuterKeySelector = Expression.Lambda(
            Expression.Call(DuckDBAsOfJoinMarker.MarkMethod.MakeGenericMethod(keyType), outerKeySelector.Body, Expression.Constant(plan)),
            outerKeySelector.Parameters);

        var genericArguments = node.Method.GetGenericArguments();
        return Expression.Call(
            (isLeft ? QueryableMethods.LeftJoin : QueryableMethods.Join)
                .MakeGenericMethod(genericArguments[0], genericArguments[1], keyType, genericArguments[2]),
            outer,
            inner,
            Expression.Quote(markedOuterKeySelector),
            Expression.Quote(innerKeySelector),
            Expression.Quote(resultSelector));
    }
}