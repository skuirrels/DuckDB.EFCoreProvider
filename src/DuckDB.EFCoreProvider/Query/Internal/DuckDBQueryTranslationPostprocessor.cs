using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBQueryTranslationPostprocessor : RelationalQueryTranslationPostprocessor
{
    public DuckDBQueryTranslationPostprocessor(
        QueryTranslationPostprocessorDependencies dependencies,
        RelationalQueryTranslationPostprocessorDependencies relationalDependencies,
        RelationalQueryCompilationContext queryCompilationContext)
        : base(dependencies, relationalDependencies, queryCompilationContext)
    {
    }

    /// <inheritdoc />
    protected override Expression ProcessTypeMappings(Expression expression)
    {
        return new DuckDBTypeMappingPostprocessor(
                Dependencies,
                RelationalDependencies,
                (RelationalQueryCompilationContext)QueryCompilationContext)
            .Process(expression);
    }

    /// <inheritdoc />
    public override Expression Process(Expression query)
    {
        var result = base.Process(query);

        result = new DuckDBTierPartitionPruningExpressionVisitor(
                QueryCompilationContext.Model,
                RelationalDependencies.SqlExpressionFactory,
                RelationalDependencies.TypeMappingSource)
            .Visit(result);
        result = new DuckDBUnnestPostprocessor().Visit(result);
        result = new DuckDBFileSourceQueryRootRewritingExpressionVisitor(RelationalDependencies.SqlExpressionFactory)
            .Visit(result);

        return result;
    }
}