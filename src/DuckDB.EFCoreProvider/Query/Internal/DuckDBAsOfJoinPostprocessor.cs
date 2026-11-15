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
/// <param name="JoinPredicate">The equality join predicate EF Core produced for the planned keys.</param>
/// <param name="Plan">The validated ASOF plan.</param>
/// <param name="IsLeft">Whether the join keeps outer rows without a match.</param>
public sealed record DuckDBAsOfJoinBinding(SqlExpression JoinPredicate, DuckDBAsOfJoinPlan Plan, bool IsLeft);

/// <summary>
///     Replaces each recorded inner or left join with a <see cref="DuckDBAsOfJoinExpression" /> whose last key
///     equality becomes the planned inequality.
/// </summary>
/// <remarks>
///     Translation keeps EF Core's ordinary join, so pushdown, projection and nullability all see shapes EF Core
///     understands. A recorded join that is not found fails the query rather than silently running as an equi-join.
/// </remarks>
internal sealed class DuckDBAsOfJoinPostprocessor(
    IReadOnlyList<DuckDBAsOfJoinBinding> bindings,
    ISqlExpressionFactory sqlExpressionFactory) : ExpressionVisitor
{
    internal const string RestructuredJoin
        = "An ASOF join could not be translated, because EF Core restructured the joined query. Simplify the query "
        + "around the ASOF join operator.";

    private readonly bool[] _applied = new bool[bindings.Count];

    public Expression Process(Expression expression)
    {
        var result = Visit(expression)!;
        if (_applied.Any(applied => !applied))
        {
            throw new InvalidOperationException(RestructuredJoin);
        }

        return result;
    }

    internal static IReadOnlyList<SqlExpression> GetConjuncts(SqlExpression predicate)
        => predicate is SqlBinaryExpression { OperatorType: ExpressionType.AndAlso } andAlso
            ? [.. GetConjuncts(andAlso.Left), .. GetConjuncts(andAlso.Right)]
            : [predicate];

    /// <summary>
    ///     Returns the outer and inner value of each key member compared by an equality join predicate, or
    ///     <see langword="null" /> when a conjunct is not a plain equality.
    /// </summary>
    internal static IReadOnlyList<(SqlExpression Outer, SqlExpression Inner)>? GetKeyComparisons(SqlExpression joinPredicate)
    {
        var comparisons = new List<(SqlExpression, SqlExpression)>();
        foreach (var conjunct in GetConjuncts(joinPredicate))
        {
            if (conjunct is not SqlBinaryExpression { OperatorType: ExpressionType.Equal } equality
                || equality.Left is RowValueExpression or DuckDBRowValueExpression)
            {
                return null;
            }

            comparisons.Add((equality.Left, equality.Right));
        }

        return comparisons;
    }

    public override Expression? Visit(Expression? node)
    {
        switch (node)
        {
            case ShapedQueryExpression shapedQuery:
                return shapedQuery
                    .UpdateQueryExpression(Visit(shapedQuery.QueryExpression)!)
                    .UpdateShaperExpression(Visit(shapedQuery.ShaperExpression)!);

            case SelectExpression select:
                var visited = (SelectExpression)base.Visit(select)!;
                return RewriteJoins(visited);

            default:
                return base.Visit(node);
        }
    }

    private SelectExpression RewriteJoins(SelectExpression select)
    {
        TableExpressionBase[]? tables = null;
        for (var i = 0; i < select.Tables.Count; i++)
        {
            if (select.Tables[i] is not (InnerJoinExpression or LeftJoinExpression)
                || select.Tables[i] is not PredicateJoinExpressionBase join
                || FindBinding(join) is not { } bindingIndex)
            {
                continue;
            }

            _applied[bindingIndex] = true;
            tables ??= select.Tables.ToArray();
            tables[i] = new DuckDBAsOfJoinExpression(
                join.Table,
                CreateAsOfPredicate(join.JoinPredicate, bindings[bindingIndex].Plan),
                bindings[bindingIndex].IsLeft);
        }

        return tables is null
            ? select
            : select.Update(
                tables,
                select.Predicate,
                select.GroupBy,
                select.Having,
                select.Projection,
                select.Orderings,
                select.Offset,
                select.Limit);
    }

    private int? FindBinding(PredicateJoinExpressionBase join)
    {
        for (var i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (binding.IsLeft == join is LeftJoinExpression
                && (ReferenceEquals(binding.JoinPredicate, join.JoinPredicate) || binding.JoinPredicate.Equals(join.JoinPredicate)))
            {
                return i;
            }
        }

        return null;
    }

    // DuckDB requires the ASOF condition to be plain comparisons, so each key member becomes its own equality and the
    // last member becomes the planned inequality.
    private SqlExpression CreateAsOfPredicate(SqlExpression joinPredicate, DuckDBAsOfJoinPlan plan)
    {
        if (GetKeyComparisons(joinPredicate) is not { } comparisons || comparisons.Count != plan.KeyCount)
        {
            throw new InvalidOperationException(RestructuredJoin);
        }

        var (outerOrdering, innerOrdering) = comparisons[^1];
        SqlExpression predicate = sqlExpressionFactory.MakeBinary(plan.Inequality, outerOrdering, innerOrdering, typeMapping: null)!;
        for (var i = comparisons.Count - 2; i >= 0; i--)
        {
            predicate = sqlExpressionFactory.AndAlso(sqlExpressionFactory.Equal(comparisons[i].Outer, comparisons[i].Inner), predicate);
        }

        return predicate;
    }
}