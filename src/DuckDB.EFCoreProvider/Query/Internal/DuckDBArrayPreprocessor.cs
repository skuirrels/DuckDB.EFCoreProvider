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
    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType == ExpressionType.ArrayIndex)
        {
            var source = Visit(node.Left);
            var index = Visit(node.Right);
            return DuckDBNestedListElement.TryGetIndexedElementType(source, out var elementType)
                ? CreateNestedListElement(source, index, elementType)
                : node.Update(source, node.Conversion, index);
        }

        return base.VisitBinary(node);
    }

    /// <inheritdoc />
    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var method = node.Method;

        // list[i] and ElementAt(list, i) on a list of lists, or on an inner list taken from one.
        if (node is { Object: { } instance, Method.Name: "get_Item", Arguments: [{ Type: var indexType } index] }
            && indexType == typeof(int))
        {
            // Each operand is visited once, so a long indexing chain is rewritten in linear time.
            var source = Visit(instance);
            var visitedIndex = Visit(index);
            return DuckDBNestedListElement.TryGetIndexedElementType(source, out var itemType)
                ? CreateNestedListElement(source, visitedIndex, itemType)
                : node.Update(source, [visitedIndex]);
        }

        if (method.DeclaringType == typeof(Enumerable)
            && method.Name == nameof(Enumerable.ElementAt)
            && node.Arguments is [var elementSource, { Type: var elementIndexType } elementIndex]
            && elementIndexType == typeof(int))
        {
            var source = Visit(elementSource);
            var visitedIndex = Visit(elementIndex);
            return DuckDBNestedListElement.TryGetIndexedElementType(source, out var elementAtType)
                ? CreateNestedListElement(source, visitedIndex, elementAtType)
                : node.Update(null, [source, visitedIndex]);
        }

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

    private static Expression CreateNestedListElement(Expression source, Expression index, Type elementType)
        => Expression.Call(
            DuckDBNestedListElement.ElementAtMethod.MakeGenericMethod(source.Type, elementType),
            source,
            index);

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