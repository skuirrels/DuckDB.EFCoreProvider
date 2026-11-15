using DuckDB.EFCoreProvider.Query.Internal;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     Ranking filters that translate to DuckDB's <c>QUALIFY</c> clause, for example the latest row per key or the
///     top rows per group.
/// </summary>
/// <remarks>
///     <para>
///         Each operator ranks the rows of an ordered query within the partitions chosen by <c>partitionBy</c>, using
///         the preceding <c>OrderBy</c>, <c>OrderByDescending</c>, <c>ThenBy</c> and <c>ThenByDescending</c> calls
///         as the window's <c>ORDER BY</c>, and keeps the rows whose rank satisfies <c>predicate</c>. The query
///         keeps that ordering.
///     </para>
///     <para>
///         <c>orders.OrderByDescending(o => o.PlacedAt).QualifyRowNumber(o => o.CustomerId, n => n == 1)</c>
///         returns the latest order of each customer, translating to
///         <c>QUALIFY row_number() OVER (PARTITION BY "CustomerId" ORDER BY "PlacedAt" DESC) = 1</c>.
///     </para>
///     <para>
///         Partition and ordering keys must be scalar values; a partition key can be an anonymous type of scalar
///         members. Operators composed after the filter, such as <c>Where</c> or <c>Count</c>, apply to the
///         filtered rows.
///     </para>
/// </remarks>
public static class DuckDBQualifyQueryableExtensions
{
    internal static readonly MethodInfo QualifyRowNumberMethod
        = new Func<IOrderedQueryable<object>, Expression<Func<object, object>>, Expression<Func<long, bool>>, IQueryable<object>>(
                QualifyRowNumber)
            .Method.GetGenericMethodDefinition();

    internal static readonly MethodInfo QualifyRankMethod
        = new Func<IOrderedQueryable<object>, Expression<Func<object, object>>, Expression<Func<long, bool>>, IQueryable<object>>(
                QualifyRank)
            .Method.GetGenericMethodDefinition();

    internal static readonly MethodInfo QualifyDenseRankMethod
        = new Func<IOrderedQueryable<object>, Expression<Func<object, object>>, Expression<Func<long, bool>>, IQueryable<object>>(
                QualifyDenseRank)
            .Method.GetGenericMethodDefinition();

    /// <summary>
    ///     Keeps the rows whose <c>row_number()</c> within their partition satisfies <paramref name="predicate" />.
    ///     Rows that tie on the ordering are numbered in an unspecified order.
    /// </summary>
    /// <typeparam name="TSource">The element type.</typeparam>
    /// <typeparam name="TPartition">The partition key type.</typeparam>
    /// <param name="source">An ordered query; its ordering is the window's <c>ORDER BY</c>.</param>
    /// <param name="partitionBy">The partition key, typically a scalar member or an anonymous type of them.</param>
    /// <param name="predicate">A condition on the one-based row number, for example <c>n =&gt; n &lt;= 3</c>.</param>
    /// <returns>The rows that satisfy the condition, in the source ordering.</returns>
    public static IQueryable<TSource> QualifyRowNumber<TSource, TPartition>(
        this IOrderedQueryable<TSource> source,
        Expression<Func<TSource, TPartition>> partitionBy,
        Expression<Func<long, bool>> predicate)
        => Qualify(QualifyRowNumberMethod, source, partitionBy, predicate);

    /// <summary>
    ///     Keeps the rows whose <c>rank()</c> within their partition satisfies <paramref name="predicate" />. Rows
    ///     that tie on the ordering share a rank, and the next rank skips the tied positions.
    /// </summary>
    /// <typeparam name="TSource">The element type.</typeparam>
    /// <typeparam name="TPartition">The partition key type.</typeparam>
    /// <param name="source">An ordered query; its ordering is the window's <c>ORDER BY</c>.</param>
    /// <param name="partitionBy">The partition key, typically a scalar member or an anonymous type of them.</param>
    /// <param name="predicate">A condition on the one-based rank, for example <c>n =&gt; n == 1</c>.</param>
    /// <returns>The rows that satisfy the condition, in the source ordering.</returns>
    public static IQueryable<TSource> QualifyRank<TSource, TPartition>(
        this IOrderedQueryable<TSource> source,
        Expression<Func<TSource, TPartition>> partitionBy,
        Expression<Func<long, bool>> predicate)
        => Qualify(QualifyRankMethod, source, partitionBy, predicate);

    /// <summary>
    ///     Keeps the rows whose <c>dense_rank()</c> within their partition satisfies <paramref name="predicate" />.
    ///     Rows that tie on the ordering share a rank, and the next rank follows without gaps.
    /// </summary>
    /// <typeparam name="TSource">The element type.</typeparam>
    /// <typeparam name="TPartition">The partition key type.</typeparam>
    /// <param name="source">An ordered query; its ordering is the window's <c>ORDER BY</c>.</param>
    /// <param name="partitionBy">The partition key, typically a scalar member or an anonymous type of them.</param>
    /// <param name="predicate">A condition on the one-based dense rank, for example <c>n =&gt; n &lt;= 2</c>.</param>
    /// <returns>The rows that satisfy the condition, in the source ordering.</returns>
    public static IQueryable<TSource> QualifyDenseRank<TSource, TPartition>(
        this IOrderedQueryable<TSource> source,
        Expression<Func<TSource, TPartition>> partitionBy,
        Expression<Func<long, bool>> predicate)
        => Qualify(QualifyDenseRankMethod, source, partitionBy, predicate);

    private static IQueryable<TSource> Qualify<TSource, TPartition>(
        MethodInfo method,
        IOrderedQueryable<TSource> source,
        Expression<Func<TSource, TPartition>> partitionBy,
        Expression<Func<long, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(partitionBy);
        ArgumentNullException.ThrowIfNull(predicate);

        // Validating here reports an unsupported ordering or predicate where the operator is called, not when the
        // query runs.
        DuckDBQualifyPlanner.GetWindowOrderings(source.Expression);
        DuckDBQualifyPlanner.ValidatePredicate(predicate);

        return source.Provider.CreateQuery<TSource>(
            Expression.Call(
                method.MakeGenericMethod(typeof(TSource), typeof(TPartition)),
                source.Expression,
                Expression.Quote(partitionBy),
                Expression.Quote(predicate)));
    }
}