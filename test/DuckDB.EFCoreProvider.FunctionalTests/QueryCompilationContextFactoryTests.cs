using DuckDB.EFCoreProvider.Query.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class QueryCompilationContextFactoryTests
{
    [Fact]
    public void Query_factory_retains_released_and_dependency_aware_constructors()
    {
        var releasedConstructor = typeof(DuckDBQueryCompilationContextFactory).GetConstructor(
            [
                typeof(QueryCompilationContextDependencies),
                typeof(RelationalQueryCompilationContextDependencies)
            ]);
        var dependencyAwareConstructor = typeof(DuckDBQueryCompilationContextFactory).GetConstructor(
            [
                typeof(QueryCompilationContextDependencies),
                typeof(RelationalQueryCompilationContextDependencies),
                typeof(ShapedQueryCompilingExpressionVisitorDependencies),
                typeof(RelationalShapedQueryCompilingExpressionVisitorDependencies)
            ]);

        Assert.NotNull(releasedConstructor);
        Assert.NotNull(dependencyAwareConstructor);
    }
}