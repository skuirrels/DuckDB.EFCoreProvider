using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Extensions.Internal;

internal static class DuckDBPropertySelector
{
    public static IReadOnlyList<string> GetPropertyNames<TEntity>(
        Expression<Func<TEntity, object?>> expression,
        string selectorName,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var body = UnwrapConvert(expression.Body);
        MemberExpression[] members = body switch
        {
            MemberExpression member => [member],
            NewExpression @new => @new.Arguments.Select(UnwrapConvert).OfType<MemberExpression>().ToArray(),
            _ => [],
        };

        if (members.Length == 0
            || body is NewExpression newExpression && members.Length != newExpression.Arguments.Count
            || members.Any(member => member.Expression is not ParameterExpression))
        {
            throw new ArgumentException(
                $"The {selectorName} selector must contain direct property accesses, for example "
                + "'e => e.ExternalId' or 'e => new { e.ParentId, e.Sequence }'.",
                parameterName);
        }

        var names = members.Select(member => member.Member.Name).ToArray();
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            throw new ArgumentException(
                $"A {selectorName} property can only be selected once.",
                parameterName);
        }

        return names;
    }

    private static Expression UnwrapConvert(Expression expression)
        => expression is UnaryExpression
        {
            NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked,
            Operand: var operand,
        }
            ? operand
            : expression;
}