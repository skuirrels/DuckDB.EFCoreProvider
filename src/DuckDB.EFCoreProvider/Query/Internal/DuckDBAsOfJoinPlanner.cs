using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <param name="KeyCount">The number of compared values: the equalities, then the inequality.</param>
/// <param name="Inequality">The inequality, oriented as <c>outer OP inner</c>.</param>
public sealed record DuckDBAsOfJoinPlan(int KeyCount, ExpressionType Inequality);

/// <summary>
///     Validates an ASOF join condition and turns it into equality join keys, whose last member is the inequality.
/// </summary>
/// <remarks>
///     EF Core translates a join on a <see cref="ValueTuple" /> key into one equality per key member, in member order.
///     The ASOF postprocessor later turns the last equality back into the planned inequality.
/// </remarks>
internal static class DuckDBAsOfJoinPlanner
{
    // ValueTuple's widest non-nested form, holding six equalities and the inequality.
    private const int MaxKeyCount = 7;

    internal const string ConditionShape
        = "An ASOF join condition must combine, with &&, any number of equalities and exactly one inequality "
        + "(<, <=, > or >=), each comparing a value of the outer row with a value of the inner row, for example "
        + "(trade, price) => trade.Symbol == price.Symbol && trade.At >= price.At.";

    internal static DuckDBAsOfJoinPlan Plan(LambdaExpression condition)
        => Analyse(condition).Plan;

    internal static (DuckDBAsOfJoinPlan Plan, LambdaExpression OuterKeySelector, LambdaExpression InnerKeySelector) CreateKeySelectors(
        LambdaExpression condition)
    {
        var (plan, outerKeys, innerKeys) = Analyse(condition);
        return (plan,
            Expression.Lambda(CreateKey(outerKeys), condition.Parameters[0]),
            Expression.Lambda(CreateKey(innerKeys), condition.Parameters[1]));
    }

    // A single compared value stays a scalar key: EF Core pads a one-member tuple key with an extra TRUE = TRUE term.
    private static Expression CreateKey(IReadOnlyList<Expression> keys)
        => keys.Count == 1
            ? keys[0]
            : Expression.New(
                GetValueTupleType(keys.Select(key => key.Type).ToArray()).GetConstructors().Single(),
                keys);

    private static Type GetValueTupleType(Type[] types)
        => (types.Length switch
        {
            2 => typeof(ValueTuple<,>),
            3 => typeof(ValueTuple<,,>),
            4 => typeof(ValueTuple<,,,>),
            5 => typeof(ValueTuple<,,,,>),
            6 => typeof(ValueTuple<,,,,,>),
            _ => typeof(ValueTuple<,,,,,,>)
        }).MakeGenericType(types);

    private static (DuckDBAsOfJoinPlan Plan, IReadOnlyList<Expression> OuterKeys, IReadOnlyList<Expression> InnerKeys) Analyse(
        LambdaExpression condition)
    {
        var outer = condition.Parameters[0];
        var inner = condition.Parameters[1];
        var outerKeys = new List<Expression>();
        var innerKeys = new List<Expression>();
        (Expression Outer, Expression Inner, ExpressionType Operator)? inequality = null;

        foreach (var comparison in GetConjuncts(condition.Body))
        {
            if (comparison is not BinaryExpression
                {
                    NodeType: ExpressionType.Equal or ExpressionType.LessThan or ExpressionType.LessThanOrEqual
                    or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
                } binary
                || binary.Left.Type != binary.Right.Type
                || !TryOrient(binary, outer, inner, out var outerValue, out var innerValue, out var comparisonOperator))
            {
                throw new InvalidOperationException(ConditionShape);
            }

            if (comparisonOperator == ExpressionType.Equal)
            {
                outerKeys.Add(outerValue);
                innerKeys.Add(innerValue);
            }
            else if (inequality is null)
            {
                inequality = (outerValue, innerValue, comparisonOperator);
            }
            else
            {
                throw new InvalidOperationException(
                    "An ASOF join condition can contain only one inequality; DuckDB matches on equalities and "
                    + "picks the nearest row by that inequality. " + ConditionShape);
            }
        }

        if (inequality is not { } ordering)
        {
            throw new InvalidOperationException(
                "An ASOF join condition needs one inequality (<, <=, > or >=) that picks the nearest inner row. "
                + ConditionShape);
        }

        if (outerKeys.Count + 1 > MaxKeyCount)
        {
            throw new InvalidOperationException(
                $"An ASOF join condition can contain at most {MaxKeyCount - 1} equalities.");
        }

        outerKeys.Add(ordering.Outer);
        innerKeys.Add(ordering.Inner);
        return (new DuckDBAsOfJoinPlan(outerKeys.Count, ordering.Operator), outerKeys, innerKeys);
    }

    private static IEnumerable<Expression> GetConjuncts(Expression expression)
        => expression is BinaryExpression { NodeType: ExpressionType.AndAlso } andAlso
            ? GetConjuncts(andAlso.Left).Concat(GetConjuncts(andAlso.Right))
            : [expression];

    private static bool TryOrient(
        BinaryExpression comparison,
        ParameterExpression outer,
        ParameterExpression inner,
        [NotNullWhen(true)] out Expression? outerValue,
        [NotNullWhen(true)] out Expression? innerValue,
        out ExpressionType comparisonOperator)
    {
        var left = ParameterCollector.Collect(comparison.Left);
        var right = ParameterCollector.Collect(comparison.Right);
        if (IsOnly(left, outer) && IsOnly(right, inner))
        {
            outerValue = comparison.Left;
            innerValue = comparison.Right;
            comparisonOperator = comparison.NodeType;
            return true;
        }

        if (IsOnly(left, inner) && IsOnly(right, outer))
        {
            outerValue = comparison.Right;
            innerValue = comparison.Left;
            comparisonOperator = comparison.NodeType switch
            {
                ExpressionType.LessThan => ExpressionType.GreaterThan,
                ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
                ExpressionType.GreaterThan => ExpressionType.LessThan,
                ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
                var other => other
            };
            return true;
        }

        outerValue = null;
        innerValue = null;
        comparisonOperator = default;
        return false;

        static bool IsOnly(HashSet<ParameterExpression> parameters, ParameterExpression parameter)
            => parameters.Count == 1 && parameters.Contains(parameter);
    }

    private sealed class ParameterCollector : ExpressionVisitor
    {
        private readonly HashSet<ParameterExpression> _parameters = [];

        public static HashSet<ParameterExpression> Collect(Expression expression)
        {
            var collector = new ParameterCollector();
            collector.Visit(expression);
            return collector._parameters;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            _parameters.Add(node);
            return node;
        }

        // A nested lambda's own parameters are not the join rows.
        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            var collector = new ParameterCollector();
            collector.Visit(node.Body);
            _parameters.UnionWith(collector._parameters.Except(node.Parameters));
            return node;
        }
    }
}

/// <summary>
///     Carries an ASOF join plan on the outer key selector through navigation expansion.
/// </summary>
internal static class DuckDBAsOfJoinMarker
{
    internal static readonly MethodInfo MarkMethod
        = typeof(DuckDBAsOfJoinMarker).GetMethod(nameof(Mark))!;

    public static TKey Mark<TKey>(TKey key, DuckDBAsOfJoinPlan plan)
        => throw new InvalidOperationException(CoreStrings.FunctionOnClient(nameof(DuckDBAsOfJoinQueryableExtensions)));

    /// <summary>
    ///     Finds the plan on a marked outer key selector and returns both key selectors without the marker and without
    ///     the conversion to <see cref="object" /> that navigation expansion adds. EF Core then compares tuple keys member
    ///     by member, as it does anonymous-type keys, which also works when either side is a subquery.
    /// </summary>
    public static bool TryUnwrap(
        LambdaExpression outerKeySelector,
        LambdaExpression innerKeySelector,
        [NotNullWhen(true)] out LambdaExpression? unmarkedOuterKeySelector,
        [NotNullWhen(true)] out LambdaExpression? unmarkedInnerKeySelector,
        [NotNullWhen(true)] out DuckDBAsOfJoinPlan? plan)
    {
        if (RemoveObjectConversion(outerKeySelector.Body) is MethodCallExpression { Method.IsGenericMethod: true } call
            && call.Method.GetGenericMethodDefinition() == MarkMethod
            && call.Arguments[1] is ConstantExpression { Value: DuckDBAsOfJoinPlan constantPlan })
        {
            unmarkedOuterKeySelector = Expression.Lambda(call.Arguments[0], outerKeySelector.Parameters);
            unmarkedInnerKeySelector = Expression.Lambda(RemoveObjectConversion(innerKeySelector.Body), innerKeySelector.Parameters);
            plan = constantPlan;
            return true;
        }

        unmarkedOuterKeySelector = null;
        unmarkedInnerKeySelector = null;
        plan = null;
        return false;

        static Expression RemoveObjectConversion(Expression body)
            => body is UnaryExpression { NodeType: ExpressionType.Convert } conversion && conversion.Type == typeof(object)
                ? conversion.Operand
                : body;
    }
}