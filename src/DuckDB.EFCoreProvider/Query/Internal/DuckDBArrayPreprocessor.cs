using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBArrayPreprocessor : ExpressionVisitor
{
    /// <inheritdoc />
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var method = node.Method;
        
        if (method.DeclaringType == typeof(Enumerable))
        {
            if (method.Name == nameof(Enumerable.Prepend))
            {
                var source = Visit(node.Arguments[0]);
                var element = Visit(node.Arguments[1]);
                var queryableSource = EnsureQueryable(source, method.GetGenericArguments()[0]);

                return new DuckDBArrayPrependExpression(
                    queryableSource,
                    element,
                    queryableSource.Type);
            }

            if (method.Name == nameof(Enumerable.Append))
            {
                var source = Visit(node.Arguments[0]);
                var element = Visit(node.Arguments[1]);
                var queryableSource = EnsureQueryable(source, method.GetGenericArguments()[0]);

                return new DuckDBArrayAppendExpression(
                    queryableSource,
                    element,
                    queryableSource.Type);
            }
        }

        return base.VisitMethodCall(node);
    }

    /// <inheritdoc />
    protected override Expression VisitExtension(Expression node)
    {
        return node switch
        {
            DuckDBArrayPrependExpression or DuckDBArrayAppendExpression => node,
            _ => base.VisitExtension(node)
        };
    }

    private Expression EnsureQueryable(Expression source, Type elementType)
    {
        if (typeof(IQueryable<>).MakeGenericType(elementType).IsAssignableFrom(source.Type))
        {
            return source;
        }

        return Expression.Call(
            QueryableMethods.AsQueryable.MakeGenericMethod(elementType),
            source);
    }
}
