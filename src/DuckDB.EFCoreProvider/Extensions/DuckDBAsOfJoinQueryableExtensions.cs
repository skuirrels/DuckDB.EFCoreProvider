using DuckDB.EFCoreProvider.Query.Internal;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Joins that translate to DuckDB's <c>ASOF JOIN</c>, which matches each outer row with the nearest inner row by
///     an ordering key, for example the latest price at or before each trade.
/// </summary>
/// <remarks>
///     <para>
///         The condition is a conjunction of any number of equalities and exactly one inequality, each comparing an
///         outer value with an inner value:
///         <c>(trade, price) =&gt; trade.Symbol == price.Symbol &amp;&amp; trade.At &gt;= price.At</c>. For each
///         outer row, DuckDB picks the inner row that satisfies the equalities and is nearest by the inequality; with
///         <c>&gt;=</c> that is the latest inner row at or before the outer value.
///     </para>
///     <para>
///         <see cref="AsOfJoin{TOuter,TInner,TResult}" /> drops outer rows without a match, like an inner join;
///         <see cref="LeftAsOfJoin{TOuter,TInner,TResult}" /> keeps them with a <see langword="null" /> inner row.
///         Each compared value must be a scalar, and a condition can hold at most six equalities.
///     </para>
/// </remarks>
public static class DuckDBAsOfJoinQueryableExtensions
{
    internal static readonly MethodInfo AsOfJoinMethod
        = new Func<IQueryable<object>, IQueryable<object>, Expression<Func<object, object, bool>>, Expression<Func<object, object, object>>, IQueryable<object>>(
                AsOfJoin)
            .Method.GetGenericMethodDefinition();

    internal static readonly MethodInfo LeftAsOfJoinMethod
        = new Func<IQueryable<object>, IQueryable<object>, Expression<Func<object, object, bool>>, Expression<Func<object, object?, object>>, IQueryable<object>>(
                LeftAsOfJoin)
            .Method.GetGenericMethodDefinition();

    /// <summary>
    ///     Matches each outer row with the nearest inner row that satisfies <paramref name="condition" />, translating
    ///     to <c>ASOF JOIN</c>. Outer rows without a match are dropped.
    /// </summary>
    /// <typeparam name="TOuter">The outer element type.</typeparam>
    /// <typeparam name="TInner">The inner element type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="outer">The rows to match.</param>
    /// <param name="inner">The rows to match against.</param>
    /// <param name="condition">Equalities and exactly one inequality between outer and inner values.</param>
    /// <param name="resultSelector">Creates a result from an outer row and its matched inner row.</param>
    /// <returns>A result for each outer row that has a match.</returns>
    public static IQueryable<TResult> AsOfJoin<TOuter, TInner, TResult>(
        this IQueryable<TOuter> outer,
        IQueryable<TInner> inner,
        Expression<Func<TOuter, TInner, bool>> condition,
        Expression<Func<TOuter, TInner, TResult>> resultSelector)
        => Join<TOuter, TInner, TResult>(AsOfJoinMethod, outer, inner, condition, resultSelector);

    /// <summary>
    ///     Matches each outer row with the nearest inner row that satisfies <paramref name="condition" />, translating
    ///     to <c>ASOF LEFT JOIN</c>. Outer rows without a match are kept with a <see langword="null" /> inner row.
    /// </summary>
    /// <typeparam name="TOuter">The outer element type.</typeparam>
    /// <typeparam name="TInner">The inner element type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="outer">The rows to match.</param>
    /// <param name="inner">The rows to match against.</param>
    /// <param name="condition">Equalities and exactly one inequality between outer and inner values.</param>
    /// <param name="resultSelector">
    ///     Creates a result from an outer row and its matched inner row, which is <see langword="null" /> when there
    ///     is no match.
    /// </param>
    /// <returns>A result for each outer row.</returns>
    public static IQueryable<TResult> LeftAsOfJoin<TOuter, TInner, TResult>(
        this IQueryable<TOuter> outer,
        IQueryable<TInner> inner,
        Expression<Func<TOuter, TInner, bool>> condition,
        Expression<Func<TOuter, TInner?, TResult>> resultSelector)
        => Join<TOuter, TInner, TResult>(LeftAsOfJoinMethod, outer, inner, condition, resultSelector);

    private static IQueryable<TResult> Join<TOuter, TInner, TResult>(
        MethodInfo method,
        IQueryable<TOuter> outer,
        IQueryable<TInner> inner,
        Expression<Func<TOuter, TInner, bool>> condition,
        LambdaExpression resultSelector)
    {
        ArgumentNullException.ThrowIfNull(outer);
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(resultSelector);

        // Validating here reports an unsupported condition where the operator is called, not when the query runs.
        DuckDBAsOfJoinPlanner.Plan(condition);

        return outer.Provider.CreateQuery<TResult>(
            Expression.Call(
                method.MakeGenericMethod(typeof(TOuter), typeof(TInner), typeof(TResult)),
                outer.Expression,
                inner.Expression,
                Expression.Quote(condition),
                Expression.Quote(resultSelector)));
    }
}