using System.Linq.Expressions;
using System.Reflection;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     Resolves a grouping key selector and its grouping-set selectors into a validated
///     <see cref="DuckDBGroupingSetsPlan" />.
/// </summary>
internal static class DuckDBGroupingSetsPlanner
{
    public static DuckDBGroupingSetsPlan Plan(
        DuckDBGroupingSetsKind kind,
        LambdaExpression keySelector,
        IReadOnlyList<LambdaExpression> groupingSets)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(groupingSets);

        var members = GetKeyMembers(keySelector);
        var sets = kind == DuckDBGroupingSetsKind.GroupingSets
            ? groupingSets.Select(set => ResolveSet(set, members)).ToArray()
            : [];

        var plan = new DuckDBGroupingSetsPlan(kind, members.Count, sets);

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (plan.IsRolledUp(i) && member.Type.IsValueType && Nullable.GetUnderlyingType(member.Type) is null)
            {
                throw new InvalidOperationException(
                    member.Name is null
                        ? $"The grouping key type '{member.Type.Name}' is NULL in subtotal rows. Use a nullable key, "
                        + $"for example 'x => ({member.Type.Name}?)x.Value'."
                        : $"Grouping key member '{member.Name}' has non-nullable type '{member.Type.Name}' but is NULL in "
                        + $"subtotal rows. Declare it as nullable, for example '{member.Name} = ({member.Type.Name}?)x.{member.Name}'.");
            }
        }

        return plan;
    }

    private static IReadOnlyList<KeyMember> GetKeyMembers(LambdaExpression keySelector)
    {
        switch (keySelector.Body)
        {
            case NewExpression { Members: { } newMembers } newExpression when newExpression.Arguments.Count > 0:
                return newExpression.Arguments
                    .Select((argument, i) => CreateKeyMember(newMembers[i].Name, argument))
                    .ToArray();

            case NewExpression newExpression when newExpression.Arguments.Count > 0:
                throw new InvalidOperationException(
                    $"The grouping key constructor for '{newExpression.Type.Name}' does not identify its members. "
                    + "Use an anonymous type or an object initializer.");

            case NewExpression:
                throw new InvalidOperationException("A multi-level grouping key must contain at least one member.");

            case MemberInitExpression memberInit:
                if (memberInit.NewExpression.Arguments.Count > 0
                    || memberInit.Bindings.Any(binding => binding is not MemberAssignment))
                {
                    throw new InvalidOperationException(
                        $"The grouping key initializer for '{memberInit.Type.Name}' must assign members only.");
                }

                if (memberInit.Bindings.Count == 0)
                {
                    throw new InvalidOperationException("A multi-level grouping key must contain at least one member.");
                }

                return memberInit.Bindings
                    .Cast<MemberAssignment>()
                    .Select(binding => CreateKeyMember(binding.Member.Name, binding.Expression))
                    .ToArray();

            default:
                return [new KeyMember(null, keySelector.Body.Type)];
        }
    }

    private static KeyMember CreateKeyMember(string name, Expression argument)
        => StripConvert(argument) is NewExpression or MemberInitExpression
            ? throw new InvalidOperationException(
                $"Grouping key member '{name}' must be a single scalar value. Flatten nested key members into the "
                + "grouping key.")
            : new KeyMember(name, argument.Type);

    private static IReadOnlyList<int> ResolveSet(LambdaExpression setSelector, IReadOnlyList<KeyMember> members)
    {
        ArgumentNullException.ThrowIfNull(setSelector);

        if (setSelector.Parameters.Count != 1)
        {
            throw new InvalidOperationException("A grouping set selector must take the grouping key as its only parameter.");
        }

        var key = setSelector.Parameters[0];
        var body = StripConvert(setSelector.Body);

        return body switch
        {
            ConstantExpression { Value: null } => [],
            NewExpression { Arguments.Count: 0 } => [],
            NewExpression newExpression => newExpression.Arguments.Select(argument => ResolveMember(argument, key, members, setSelector)).ToArray(),
            _ => ResolveWhole(body, key, members, setSelector)
        };
    }

    private static IReadOnlyList<int> ResolveWhole(
        Expression body,
        ParameterExpression key,
        IReadOnlyList<KeyMember> members,
        LambdaExpression setSelector)
        => body == key
            ? Enumerable.Range(0, members.Count).ToArray()
            : [ResolveMember(body, key, members, setSelector)];

    private static int ResolveMember(
        Expression expression,
        ParameterExpression key,
        IReadOnlyList<KeyMember> members,
        LambdaExpression setSelector)
    {
        expression = StripConvert(expression);

        if (expression == key && members is [{ Name: null }])
        {
            return 0;
        }

        if (expression is MemberExpression { Member: PropertyInfo or FieldInfo } member && member.Expression == key)
        {
            for (var i = 0; i < members.Count; i++)
            {
                if (members[i].Name == member.Member.Name)
                {
                    return i;
                }
            }
        }

        throw new InvalidOperationException(
            $"The grouping set '{setSelector}' must select members of the grouping key, for example "
            + "'k => new { k.Region, k.Product }', 'k => k.Region' or 'k => new { }' for the grand total.");
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private sealed record KeyMember(string? Name, Type Type);
}
