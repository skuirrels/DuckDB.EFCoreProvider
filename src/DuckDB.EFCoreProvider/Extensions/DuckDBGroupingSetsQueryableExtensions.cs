using DuckDB.EFCoreProvider.Query.Internal;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Multi-level grouping operators that translate to DuckDB's <c>GROUP BY ROLLUP</c>, <c>CUBE</c> and
///     <c>GROUPING SETS</c>.
/// </summary>
/// <remarks>
///     <para>
///         Each operator returns the same groups as <see cref="Queryable.GroupBy{TSource,TKey}(IQueryable{TSource},Expression{Func{TSource,TKey}})" />,
///         plus subtotal groups in which some key members are NULL. Compose the result with <c>Select</c> and aggregate
///         functions such as <c>Sum</c> or <c>Count</c>; use <c>EF.Functions.Grouping</c> to tell a subtotal NULL from
///         a NULL key value.
///     </para>
///     <para>
///         Key members that are NULL in some grouping set must have a nullable type, for example
///         <c>Year = (int?)sale.Year</c>. Each key member must be a scalar value. Projecting the elements of a
///         group, for example <c>g.ToList()</c>, is not supported.
///     </para>
/// </remarks>
public static class DuckDBGroupingSetsQueryableExtensions
{
    internal static readonly MethodInfo GroupByRollupMethod
        = new Func<IQueryable<object>, Expression<Func<object, object>>, IQueryable<IGrouping<object, object>>>(GroupByRollup)
            .Method.GetGenericMethodDefinition();

    internal static readonly MethodInfo GroupByCubeMethod
        = new Func<IQueryable<object>, Expression<Func<object, object>>, IQueryable<IGrouping<object, object>>>(GroupByCube)
            .Method.GetGenericMethodDefinition();

    internal static readonly MethodInfo GroupByGroupingSetsMethod
        = new Func<IQueryable<object>, Expression<Func<object, object>>, Expression<Func<object, object?>>[], IQueryable<IGrouping<object, object>>>(
                GroupByGroupingSets)
            .Method.GetGenericMethodDefinition();

    /// <summary>
    ///     Groups by every prefix of the key, translating to <c>GROUP BY ROLLUP (k1, ..., kn)</c>. A key of
    ///     <c>new { Region, Product }</c> produces groups for (Region, Product), (Region) and the grand total.
    /// </summary>
    /// <typeparam name="TSource">The source element type.</typeparam>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <param name="source">The query to group.</param>
    /// <param name="keySelector">A key selector, typically an anonymous type of scalar members.</param>
    /// <returns>A query of groups, including subtotal groups.</returns>
    public static IQueryable<IGrouping<TKey, TSource>> GroupByRollup<TSource, TKey>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);
        DuckDBGroupingSetsPlanner.Plan(DuckDBGroupingSetsKind.Rollup, keySelector, []);

        return source.Provider.CreateQuery<IGrouping<TKey, TSource>>(
            Expression.Call(
                GroupByRollupMethod.MakeGenericMethod(typeof(TSource), typeof(TKey)),
                source.Expression,
                Expression.Quote(keySelector)));
    }

    /// <summary>
    ///     Groups by every combination of the key members, translating to <c>GROUP BY CUBE (k1, ..., kn)</c>.
    /// </summary>
    /// <typeparam name="TSource">The source element type.</typeparam>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <param name="source">The query to group.</param>
    /// <param name="keySelector">A key selector, typically an anonymous type of scalar members.</param>
    /// <returns>A query of groups, including subtotal groups.</returns>
    public static IQueryable<IGrouping<TKey, TSource>> GroupByCube<TSource, TKey>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);
        DuckDBGroupingSetsPlanner.Plan(DuckDBGroupingSetsKind.Cube, keySelector, []);

        return source.Provider.CreateQuery<IGrouping<TKey, TSource>>(
            Expression.Call(
                GroupByCubeMethod.MakeGenericMethod(typeof(TSource), typeof(TKey)),
                source.Expression,
                Expression.Quote(keySelector)));
    }

    /// <summary>
    ///     Groups by each listed set of key members, translating to <c>GROUP BY GROUPING SETS (...)</c>.
    /// </summary>
    /// <example>
    ///     <code>
    ///         sales.GroupByGroupingSets(
    ///             s => new { s.Region, s.Product },
    ///             k => new { k.Region, k.Product },
    ///             k => k.Region,
    ///             k => new { })
    ///     </code>
    /// </example>
    /// <typeparam name="TSource">The source element type.</typeparam>
    /// <typeparam name="TKey">The grouping key type.</typeparam>
    /// <param name="source">The query to group.</param>
    /// <param name="keySelector">A key selector, typically an anonymous type of scalar members.</param>
    /// <param name="groupingSets">
    ///     One selector per grouping set. Each selects key members (<c>k =&gt; new { k.Region, k.Product }</c>),
    ///     a single member (<c>k =&gt; k.Region</c>), or no members for the grand total (<c>k =&gt; new { }</c>).
    /// </param>
    /// <returns>A query of groups, one per row of each grouping set.</returns>
    public static IQueryable<IGrouping<TKey, TSource>> GroupByGroupingSets<TSource, TKey>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TKey>> keySelector,
        params Expression<Func<TKey, object?>>[] groupingSets)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(groupingSets);
        if (groupingSets.Length == 0)
        {
            throw new ArgumentException("At least one grouping set is required.", nameof(groupingSets));
        }

        foreach (var groupingSet in groupingSets)
        {
            ArgumentNullException.ThrowIfNull(groupingSet, nameof(groupingSets));
        }

        DuckDBGroupingSetsPlanner.Plan(DuckDBGroupingSetsKind.GroupingSets, keySelector, groupingSets);

        return source.Provider.CreateQuery<IGrouping<TKey, TSource>>(
            Expression.Call(
                GroupByGroupingSetsMethod.MakeGenericMethod(typeof(TSource), typeof(TKey)),
                source.Expression,
                Expression.Quote(keySelector),
                Expression.NewArrayInit(
                    typeof(Expression<Func<TKey, object>>),
                    groupingSets.Select(Expression.Quote))));
    }
}
