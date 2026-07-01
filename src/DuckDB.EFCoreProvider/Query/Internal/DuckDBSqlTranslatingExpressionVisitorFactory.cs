using Microsoft.EntityFrameworkCore.Query;

namespace DuckDB.EFCoreProvider.Query.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBSqlTranslatingExpressionVisitorFactory : IRelationalSqlTranslatingExpressionVisitorFactory
{
    private readonly RelationalSqlTranslatingExpressionVisitorDependencies _relationalDependencies;

    public DuckDBSqlTranslatingExpressionVisitorFactory(RelationalSqlTranslatingExpressionVisitorDependencies relationalDependencies)
    {
        _relationalDependencies = relationalDependencies;
    }

    /// <inheritdoc />
    public RelationalSqlTranslatingExpressionVisitor Create(
        QueryCompilationContext queryCompilationContext,
        QueryableMethodTranslatingExpressionVisitor queryableMethodTranslatingExpressionVisitor)
    {
        return new DuckDBSqlTranslatingExpressionVisitor(
            _relationalDependencies,
            queryCompilationContext,
            queryableMethodTranslatingExpressionVisitor);
    }
}
