using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace DuckDB.EFCoreProvider.Query.Internal;

internal sealed class DuckDBShapedQueryCompilingExpressionVisitorFactory(
    ShapedQueryCompilingExpressionVisitorDependencies dependencies,
    RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies)
    : RelationalShapedQueryCompilingExpressionVisitorFactory(dependencies, relationalDependencies)
{
    public override ShapedQueryCompilingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
        => new DuckDBShapedQueryCompilingExpressionVisitor(
            Dependencies,
            RelationalDependencies,
            queryCompilationContext);
}
