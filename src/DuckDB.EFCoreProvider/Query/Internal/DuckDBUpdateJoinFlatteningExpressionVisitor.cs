using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     Rewrites the inner joins of an <c>ExecuteUpdate</c> source into cross joins whose join predicates move to
///     <c>WHERE</c>.
/// </summary>
/// <remarks>
///     EF Core renders the first joined table of an <c>UPDATE ... FROM</c> as a plain table and lifts its join
///     predicate into <c>WHERE</c>, but renders later inner joins with their <c>ON</c> clause. When that clause
///     references the update target, DuckDB fails to bind it, because the target is not visible inside the
///     <c>FROM</c> clause. Moving inner-join predicates into <c>WHERE</c> keeps the same rows. Sources with any other
///     join kind are left unchanged, since their predicates cannot move without changing results.
/// </remarks>
internal sealed class DuckDBUpdateJoinFlatteningExpressionVisitor(ISqlExpressionFactory sqlExpressionFactory)
    : ExpressionVisitor
{
    protected override Expression VisitExtension(Expression node)
        => node switch
        {
            UpdateExpression update => Flatten(update),

            // Only the top-level UPDATE source is rewritten; nested queries keep their joins.
            ShapedQueryExpression or SelectExpression => node,
            _ => base.VisitExtension(node)
        };

    private UpdateExpression Flatten(UpdateExpression update)
    {
        var select = update.SelectExpression;

        // With one joined table EF Core already lifts the join predicate, so the SQL is valid as it is.
        if (select.Tables.Count < 3
            || select.Tables.Any(table => table is JoinExpressionBase and not InnerJoinExpression and not CrossJoinExpression))
        {
            return update;
        }

        var tables = new TableExpressionBase[select.Tables.Count];
        SqlExpression? joinPredicates = null;
        for (var i = 0; i < tables.Length; i++)
        {
            if (select.Tables[i] is InnerJoinExpression innerJoin)
            {
                tables[i] = new CrossJoinExpression(innerJoin.Table);
                joinPredicates = joinPredicates is null
                    ? innerJoin.JoinPredicate
                    : sqlExpressionFactory.AndAlso(joinPredicates, innerJoin.JoinPredicate);
            }
            else
            {
                tables[i] = select.Tables[i];
            }
        }

        if (joinPredicates is null)
        {
            return update;
        }

        var predicate = select.Predicate is null
            ? joinPredicates
            : sqlExpressionFactory.AndAlso(joinPredicates, select.Predicate);

        var flattened = select.Update(
            tables,
            predicate,
            select.GroupBy,
            select.Having,
            select.Projection,
            select.Orderings,
            select.Offset,
            select.Limit);

        return update.Update(flattened, update.ColumnValueSetters);
    }
}