using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <param name="Terms">The GROUP BY terms EF Core produced for the grouping key, in key-member order.</param>
/// <param name="Plan">The validated grouping plan.</param>
public sealed record DuckDBGroupingSetsBinding(IReadOnlyList<SqlExpression> Terms, DuckDBGroupingSetsPlan Plan);

/// <summary>
///     Replaces the plain GROUP BY terms of each multi-level grouping with a single
///     <see cref="DuckDBGroupingSetsExpression" />.
/// </summary>
/// <remarks>
///     Translation keeps EF Core's ordinary GROUP BY terms so that projection, HAVING and pushdown all see the
///     shapes EF Core understands. The terms are recorded by reference when the grouping is translated, and this
///     visitor runs before alias postprocessing, so a SELECT whose GROUP BY list is exactly a recorded term list is
///     the grouped SELECT. A recorded grouping that is not found fails the query rather than silently falling back to
///     a plain GROUP BY.
/// </remarks>
internal sealed class DuckDBGroupingSetsPostprocessor(IReadOnlyList<DuckDBGroupingSetsBinding> bindings) : ExpressionVisitor
{
    internal const string ElementAccessNotSupported
        = "The elements of a ROLLUP, CUBE or GROUPING SETS group cannot be queried, because subtotal rows do not "
        + "correspond to source rows by key. Project the key and aggregates such as Sum or Count instead.";

    private readonly bool[] _applied = new bool[bindings.Count];

    public Expression Process(Expression expression)
    {
        var result = Visit(expression)!;

        if (_applied.Any(applied => !applied))
        {
            throw new InvalidOperationException(
                "A ROLLUP, CUBE or GROUPING SETS grouping could not be translated, because EF Core restructured the "
                + "grouped query. Simplify the query around the grouping operator.");
        }

        return result;
    }

    public override Expression? Visit(Expression? node)
    {
        switch (node)
        {
            case ShapedQueryExpression shapedQuery:
                return shapedQuery
                    .UpdateQueryExpression(Visit(shapedQuery.QueryExpression)!)
                    .UpdateShaperExpression(Visit(shapedQuery.ShaperExpression)!);

            case DuckDBGroupingSetsElementAccessExpression:
                throw new InvalidOperationException(ElementAccessNotSupported);

            case SelectExpression select:
                var visited = (SelectExpression)base.Visit(select)!;
                return RewriteGroupBy(visited);

            default:
                return base.Visit(node);
        }
    }

    private SelectExpression RewriteGroupBy(SelectExpression select)
    {
        if (select.GroupBy.Count == 0)
        {
            return select;
        }

        for (var i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (binding.Terms.Count != select.GroupBy.Count
                || !binding.Terms.Zip(select.GroupBy).All(pair => ReferenceEquals(pair.First, pair.Second)))
            {
                continue;
            }

            _applied[i] = true;
            var plan = binding.Plan;
            var groupingSets = new DuckDBGroupingSetsExpression(plan.Kind, select.GroupBy, plan.Sets);

            return select.Update(
                select.Tables,
                select.Predicate,
                [groupingSets],
                select.Having,
                select.Projection,
                select.Orderings,
                select.Offset,
                select.Limit);
        }

        return select;
    }
}
