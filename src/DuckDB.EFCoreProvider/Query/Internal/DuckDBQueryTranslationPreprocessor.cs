using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBQueryTranslationPreprocessor : RelationalQueryTranslationPreprocessor
{
    public DuckDBQueryTranslationPreprocessor(
        QueryTranslationPreprocessorDependencies dependencies,
        RelationalQueryTranslationPreprocessorDependencies relationalDependencies,
        QueryCompilationContext queryCompilationContext)
        : base(dependencies, relationalDependencies, queryCompilationContext)
    {
    }

    /// <inheritdoc />
    public override Expression Process(Expression query)
    {
        query = new InvocationExpressionRemovingExpressionVisitor().Visit(query);
        query = new DuckDBArrayPreprocessor().Visit(query);
        query = NormalizeQueryableMethod(query);
        query = new CallForwardingExpressionVisitor().Visit(query);
        query = new NullCheckRemovingExpressionVisitor().Visit(query);
        query = new SubqueryMemberPushdownExpressionVisitor(QueryCompilationContext.Model).Visit(query);
        query = new DuckDBNavigationExpandingExpressionVisitor(
                this,
                QueryCompilationContext,
                Dependencies.EvaluatableExpressionFilter,
                Dependencies.NavigationExpansionExtensibilityHelper)
            .Expand(query);
        query = new QueryOptimizingExpressionVisitor().Visit(query);
        query = new NullCheckRemovingExpressionVisitor().Visit(query);

        return query;
    }
}
