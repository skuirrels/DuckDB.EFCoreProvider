using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBEvaluatableExpressionFilter : RelationalEvaluatableExpressionFilter
{
    public DuckDBEvaluatableExpressionFilter(
        EvaluatableExpressionFilterDependencies dependencies,
        RelationalEvaluatableExpressionFilterDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    /// <inheritdoc />
    public override bool IsEvaluatableExpression(Expression expression, IModel model)
    {
        switch (expression)
        {
            case MethodCallExpression methodCallExpression:
                var declaringType = methodCallExpression.Method.DeclaringType;
                var method = methodCallExpression.Method;

                if (declaringType == typeof(ValueTuple) && method.Name == nameof(ValueTuple.Create))
                {
                    return false;
                }

                break;

            case NewExpression newExpression when newExpression.Type.IsAssignableTo(typeof(ITuple)):
                return false;
        }

        return base.IsEvaluatableExpression(expression, model);
    }
}
