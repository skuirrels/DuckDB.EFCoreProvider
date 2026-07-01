using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public abstract class QueryExpressionInterceptionDuckDBTestBase(
    QueryExpressionInterceptionDuckDBTestBase.InterceptionDuckDBFixtureBase fixture)
    : QueryExpressionInterceptionTestBase(fixture)
{
    public abstract class InterceptionDuckDBFixtureBase : InterceptionTestBase.InterceptionFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors)
            => base.InjectInterceptors(serviceCollection.AddEntityFrameworkDuckDB(), injectedInterceptors);
    }

    public class QueryExpressionInterceptionDuckDBTest(QueryExpressionInterceptionDuckDBTest.InterceptionDuckDBFixture fixture)
        : QueryExpressionInterceptionDuckDBTestBase(fixture), IClassFixture<QueryExpressionInterceptionDuckDBTest.InterceptionDuckDBFixture>
    {
        public class InterceptionDuckDBFixture : InterceptionDuckDBFixtureBase
        {
            protected override string StoreName
                => "QueryExpressionInterception";

            protected override bool ShouldSubscribeToDiagnosticListener
                => false;
        }
    }

    public class QueryExpressionInterceptionWithDiagnosticsDuckDBTest(
        QueryExpressionInterceptionWithDiagnosticsDuckDBTest.InterceptionDuckDBFixture fixture)
        : QueryExpressionInterceptionDuckDBTestBase(fixture),
            IClassFixture<QueryExpressionInterceptionWithDiagnosticsDuckDBTest.InterceptionDuckDBFixture>
    {
        public class InterceptionDuckDBFixture : InterceptionDuckDBFixtureBase
        {
            protected override string StoreName
                => "QueryExpressionInterceptionWithDiagnostics";

            protected override bool ShouldSubscribeToDiagnosticListener
                => true;
        }
    }
}