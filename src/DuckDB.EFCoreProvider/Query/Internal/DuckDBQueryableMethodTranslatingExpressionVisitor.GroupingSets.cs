using DuckDB.EFCoreProvider.Query.Expressions.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

public partial class DuckDBQueryableMethodTranslatingExpressionVisitor
{
    /// <inheritdoc />
    protected override ShapedQueryExpression? TranslateGroupBy(
        ShapedQueryExpression source,
        LambdaExpression keySelector,
        LambdaExpression? elementSelector,
        LambdaExpression? resultSelector)
    {
        if (!DuckDBGroupingSetsKeyMarker.TryUnwrap(keySelector, out var unmarkedKeySelector, out var plan))
        {
            return base.TranslateGroupBy(source, keySelector, elementSelector, resultSelector);
        }

        // The grouping-sets operators only produce GroupBy(source, key); any other shape is not one of ours.
        if (elementSelector is not null || resultSelector is not null)
        {
            return null;
        }

        // Let EF Core build an ordinary grouping; the postprocessor later swaps its GROUP BY terms for the plan.
        var grouped = base.TranslateGroupBy(source, unmarkedKeySelector, elementSelector: null, resultSelector: null);
        if (grouped is not { QueryExpression: SelectExpression select, ShaperExpression: RelationalGroupByShaperExpression shaper })
        {
            return null;
        }

        // Plan positions, and the nullable rewrite below, index GROUP BY terms by key-member position, so every key
        // member must translate to exactly one distinct column, in key order.
        var terms = select.GroupBy.ToArray();
        if (terms.Distinct().Count() != terms.Length)
        {
            throw new InvalidOperationException(
                "A ROLLUP, CUBE or GROUPING SETS key cannot contain the same value more than once, because subtotal "
                + "rows could not set one copy to NULL. Remove the duplicate key member.");
        }

        var keyMembers = GetKeyMemberTranslations(shaper.KeySelector);
        if (terms.Length != plan.KeyMemberCount
            || keyMembers.Count != plan.KeyMemberCount
            || terms.Where((term, i) => term is not ColumnExpression || !ReferenceEquals(term, keyMembers[i])).Any())
        {
            throw new InvalidOperationException(
                "Each member of a ROLLUP, CUBE or GROUPING SETS key must be a single scalar value. "
                + "Entity, owned, complex-type and nested key members are not supported.");
        }

        ((SelectExpression)shaper.GroupingEnumerable.QueryExpression).ApplyPredicate(
            new DuckDBGroupingSetsElementAccessExpression(_typeMappingSource.FindMapping(typeof(bool))));
        ((DuckDBQueryCompilationContext)_queryCompilationContext).AddGroupingSets(new DuckDBGroupingSetsBinding(terms, plan));

        // Subtotal rows carry NULL for rolled-up key members, so the key must be read as nullable.
        var keySelectorBody = new RolledUpKeyNullabilityVisitor(terms, plan).Visit(shaper.KeySelector)!;
        return grouped.UpdateShaperExpression(
            new RelationalGroupByShaperExpression(keySelectorBody, shaper.ElementSelector, shaper.GroupingEnumerable));
    }

    private static IReadOnlyList<Expression> GetKeyMemberTranslations(Expression keySelector)
        => keySelector switch
        {
            NewExpression newExpression => newExpression.Arguments.Select(UnwrapConvert).ToArray(),
            MemberInitExpression memberInit => memberInit.Bindings
                // A non-assignment binding never matches a GROUP BY term, so the caller rejects it.
                .Select(binding => binding is MemberAssignment assignment ? UnwrapConvert(assignment.Expression) : Expression.Empty())
                .ToArray(),
            _ => [UnwrapConvert(keySelector)]
        };

    private static Expression UnwrapConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private sealed class RolledUpKeyNullabilityVisitor(
        IReadOnlyList<SqlExpression> terms,
        DuckDBGroupingSetsPlan plan) : ExpressionVisitor
    {
        public override Expression? Visit(Expression? node)
        {
            if (node is not SqlExpression sqlExpression)
            {
                return base.Visit(node);
            }

            for (var i = 0; i < terms.Count; i++)
            {
                if (ReferenceEquals(terms[i], sqlExpression) && plan.IsRolledUp(i))
                {
                    return ((ColumnExpression)sqlExpression).MakeNullable();
                }
            }

            return sqlExpression;
        }
    }
}
