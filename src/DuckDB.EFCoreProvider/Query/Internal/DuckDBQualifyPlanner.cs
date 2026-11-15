using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public enum DuckDBRankingFunction
{
    /// <summary><c>row_number()</c>.</summary>
    RowNumber,

    /// <summary><c>rank()</c>.</summary>
    Rank,

    /// <summary><c>dense_rank()</c>.</summary>
    DenseRank
}

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>The ranking function and the direction of each window ordering key, in ordering order.</remarks>
public sealed class DuckDBQualifyPlan
{
    internal DuckDBQualifyPlan(DuckDBRankingFunction function, IReadOnlyList<bool> descending)
    {
        Function = function;
        Descending = descending.ToArray();
    }

    /// <summary>The ranking function.</summary>
    public DuckDBRankingFunction Function { get; }

    /// <summary>Whether each window ordering key sorts descending.</summary>
    public IReadOnlyList<bool> Descending { get; }
}

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <param name="KeySelector">The ordering key selector.</param>
/// <param name="Descending">Whether the key sorts descending.</param>
public sealed record DuckDBWindowOrdering(LambdaExpression KeySelector, bool Descending);

/// <summary>
///     Resolves the window ordering of a QUALIFY operator from the ordering calls of its source.
/// </summary>
internal static class DuckDBQualifyPlanner
{
    internal const string OrderingRequired
        = "QualifyRowNumber, QualifyRank and QualifyDenseRank use the source's ordering as the window ORDER BY, so the "
        + "source must end with OrderBy or OrderByDescending, optionally followed by ThenBy or ThenByDescending. "
        + "Orderings with a custom comparer are not supported.";

    internal static IReadOnlyList<DuckDBWindowOrdering> GetWindowOrderings(Expression source)
    {
        var orderings = new List<DuckDBWindowOrdering>();
        while (source is MethodCallExpression { Method: { DeclaringType: var declaringType } method, Arguments: [var inner, var keySelector] }
            && declaringType == typeof(Queryable))
        {
            switch (method.Name)
            {
                case nameof(Queryable.ThenBy):
                case nameof(Queryable.ThenByDescending):
                    orderings.Add(new DuckDBWindowOrdering(Unquote(keySelector), method.Name == nameof(Queryable.ThenByDescending)));
                    source = inner;
                    continue;

                case nameof(Queryable.OrderBy):
                case nameof(Queryable.OrderByDescending):
                    orderings.Add(new DuckDBWindowOrdering(Unquote(keySelector), method.Name == nameof(Queryable.OrderByDescending)));
                    orderings.Reverse();
                    return orderings;
            }

            break;
        }

        throw new InvalidOperationException(OrderingRequired);
    }

    internal const string RankRequired
        = "The QUALIFY predicate must use the rank it receives, for example n => n == 1, because DuckDB requires a "
        + "window function in the QUALIFY clause.";

    /// <summary>Rejects a predicate that ignores the rank, which DuckDB would reject only when the query runs.</summary>
    internal static void ValidatePredicate(LambdaExpression predicate)
    {
        if (!ParameterFinder.Uses(predicate.Body, predicate.Parameters[0]))
        {
            throw new InvalidOperationException(RankRequired);
        }
    }

    internal static DuckDBRankingFunction GetFunction(MethodInfo definition)
        => definition == DuckDBQualifyQueryableExtensions.QualifyRowNumberMethod
            ? DuckDBRankingFunction.RowNumber
            : definition == DuckDBQualifyQueryableExtensions.QualifyRankMethod
                ? DuckDBRankingFunction.Rank
                : definition == DuckDBQualifyQueryableExtensions.QualifyDenseRankMethod
                    ? DuckDBRankingFunction.DenseRank
                    : throw new InvalidOperationException(CoreStrings.TranslationFailed(definition.Name));

    internal static LambdaExpression Unquote(Expression expression)
        => expression is UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression quoted }
            ? quoted
            : (LambdaExpression)expression;

    private sealed class ParameterFinder(ParameterExpression parameter) : ExpressionVisitor
    {
        private bool _found;

        public static bool Uses(Expression expression, ParameterExpression parameter)
        {
            var finder = new ParameterFinder(parameter);
            finder.Visit(expression);
            return finder._found;
        }

        public override Expression? Visit(Expression? node)
            => _found ? node : base.Visit(node);

        protected override Expression VisitParameter(ParameterExpression node)
        {
            _found |= node == parameter;
            return node;
        }
    }
}

/// <summary>
///     Marker methods that carry a QUALIFY filter through navigation expansion as an ordinary <c>Where</c> predicate.
/// </summary>
internal static class DuckDBQualifyMarker
{
    internal static readonly MethodInfo QualifyMethod
        = typeof(DuckDBQualifyMarker).GetMethod(nameof(Qualify))!;

    internal static readonly MethodInfo RankMethod
        = typeof(DuckDBQualifyMarker).GetMethod(nameof(Rank))!;

    public static bool Qualify(bool predicate)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DuckDBQualifyQueryableExtensions)));

    public static long Rank<TPartition>(TPartition partitionBy, object?[] orderKeys, DuckDBQualifyPlan plan)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DuckDBQualifyQueryableExtensions)));

    internal static bool IsQualify(Expression expression)
        => expression is MethodCallExpression { Method: var method } && method == QualifyMethod;

    internal static bool IsRank(MethodInfo method)
        => method.IsGenericMethod && method.GetGenericMethodDefinition() == RankMethod;
}