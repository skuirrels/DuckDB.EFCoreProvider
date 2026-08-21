using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Internal;

internal sealed class DuckDBShapedQueryCompilingExpressionVisitor(
    ShapedQueryCompilingExpressionVisitorDependencies dependencies,
    RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies,
    QueryCompilationContext queryCompilationContext)
    : RelationalShapedQueryCompilingExpressionVisitor(dependencies, relationalDependencies, queryCompilationContext)
{
    private static readonly MethodInfo GetValueMethod
        = typeof(DbDataReader).GetMethod(nameof(DbDataReader.GetValue), [typeof(int)])!;

    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
        var selectExpression = shapedQueryExpression.QueryExpression as SelectExpression;
        var result = base.VisitShapedQuery(shapedQueryExpression);
        if (selectExpression is null)
        {
            return result;
        }

        var slots = FindSharedStructSlots(selectExpression);
        var rewritten = slots.Count == 0
            ? result
            : new DuckDBStructReaderExpressionVisitor(slots).Visit(result)!;
        return rewritten;
    }

    protected override Expression InjectStructuralTypeMaterializers(Expression expression)
        => base.InjectStructuralTypeMaterializers(expression);

    private static Dictionary<int, StructSlot> FindSharedStructSlots(SelectExpression selectExpression)
    {
        var roots = new List<(int Index, DuckDBWholeStructExpression Expression)>();
        for (var i = 0; i < selectExpression.Projection.Count; i++)
        {
            if (selectExpression.Projection[i].Expression is DuckDBWholeStructExpression
                {
                    FieldPath.Count: 0,
                    SuppressSource: false
                } root)
            {
                roots.Add((i, root));
            }
        }

        var sharedSlots = new Dictionary<int, StructSlot>();
        for (var i = 0; i < selectExpression.Projection.Count; i++)
        {
            if (selectExpression.Projection[i].Expression is not DuckDBWholeStructExpression
                {
                    FieldPath.Count: > 0,
                    SuppressSource: true
                } leaf)
            {
                continue;
            }

            var root = roots.FirstOrDefault(candidate => AreSameStructSource(candidate.Expression.Source, leaf.Source));
            if (root.Expression is not null
                && leaf.ExtractionTypeMapping is DuckDBStructKeyTypeMapping extractionMapping)
            {
                sharedSlots[i] = new StructSlot(root.Index, extractionMapping);
            }
        }

        return sharedSlots;
    }

    private static bool AreSameStructSource(SqlExpression left, SqlExpression right)
        => left is ColumnExpression leftColumn
            && right is ColumnExpression rightColumn
            && string.Equals(leftColumn.TableAlias, rightColumn.TableAlias, StringComparison.Ordinal)
            && string.Equals(leftColumn.Name, rightColumn.Name, StringComparison.Ordinal)
            || left.Equals(right);

    private sealed record StructSlot(int RootProjectionIndex, DuckDBStructKeyTypeMapping ExtractionMapping);

    private sealed class DuckDBStructReaderExpressionVisitor(
        IReadOnlyDictionary<int, StructSlot> slots) : ExpressionVisitor
    {
        protected override Expression VisitConditional(ConditionalExpression node)
        {
            if (node.Test is MethodCallExpression test
                && TryGetReaderProjection(test, out var reader, out var projectionIndex)
                && test.Method.Name == nameof(DbDataReader.IsDBNull)
                && slots.TryGetValue(projectionIndex, out var slot)
                && slot.ExtractionMapping.LeafTypeMapping is { } leafTypeMapping)
            {
                var rootValue = Expression.Call(
                    reader,
                    GetValueMethod,
                    Expression.Constant(slot.RootProjectionIndex));
                var extracted = DuckDBStructKeyTypeMapping.CreateReadExpression(
                    rootValue,
                    leafTypeMapping,
                    slot.ExtractionMapping.FieldPath);

                return extracted.Type == node.Type
                    ? extracted
                    : Expression.Convert(extracted, node.Type);
            }

            return base.VisitConditional(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == nameof(DbDataReader.IsDBNull))
            {
                return base.VisitMethodCall(node);
            }

            if (!TryGetReaderProjection(node, out var reader, out var projectionIndex)
                || !slots.TryGetValue(projectionIndex, out var slot)
                || slot.ExtractionMapping.LeafTypeMapping is not { } leafTypeMapping
                || !ReaderMethodMatches(node.Method, leafTypeMapping.GetDataReaderMethod()))
            {
                return base.VisitMethodCall(node);
            }

            var rootValue = Expression.Call(
                reader,
                GetValueMethod,
                Expression.Constant(slot.RootProjectionIndex));
            var extracted = DuckDBStructKeyTypeMapping.CreateProviderReadExpression(
                rootValue,
                leafTypeMapping,
                slot.ExtractionMapping.FieldPath);

            return extracted.Type == node.Type
                ? extracted
                : Expression.Convert(extracted, node.Type);
        }

        private static bool TryGetReaderProjection(
            MethodCallExpression node,
            out Expression reader,
            out int projectionIndex)
        {
            reader = null!;
            projectionIndex = 0;
            if (node.Object is not { Type: { } objectType }
                || !typeof(DbDataReader).IsAssignableFrom(objectType)
                || node.Arguments.Count != 1
                || node.Arguments[0] is not ConstantExpression { Value: int index })
            {
                return false;
            }

            reader = node.Object;
            projectionIndex = index;
            return true;
        }

        private static bool ReaderMethodMatches(MethodInfo actual, MethodInfo expected)
        {
            if (actual == expected)
            {
                return true;
            }

            if (actual.IsGenericMethod
                && expected.IsGenericMethod
                && actual.GetGenericMethodDefinition() == expected.GetGenericMethodDefinition()
                && actual.GetGenericArguments().SequenceEqual(expected.GetGenericArguments()))
            {
                return true;
            }

            return actual.GetBaseDefinition() == expected.GetBaseDefinition();
        }
    }
}
