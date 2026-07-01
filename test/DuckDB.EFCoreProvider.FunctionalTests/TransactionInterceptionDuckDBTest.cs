using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public abstract class TransactionInterceptionDuckDBTestBase(TransactionInterceptionDuckDBTestBase.InterceptionDuckDBFixtureBase fixture)
    : TransactionInterceptionTestBase(fixture)
{
    [ConditionalTheory(Skip = "DuckDB does not support savepoints")]
    public override async Task Intercept_CreateSavepoint(bool async)
    {
        await base.Intercept_CreateSavepoint(async);
    }

    [ConditionalTheory(Skip = "DuckDB does not support savepoints")]
    public override async Task Intercept_RollbackToSavepoint(bool async)
    {
        await base.Intercept_RollbackToSavepoint(async);
    }

    [ConditionalTheory(Skip = "DuckDB does not support savepoints")]
    public override async Task Intercept_ReleaseSavepoint(bool async)
    {
        await base.Intercept_ReleaseSavepoint(async);
    }

    public abstract class InterceptionDuckDBFixtureBase : InterceptionFixtureBase
    {
        protected override string StoreName
            => "TransactionInterception";

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors)
            => base.InjectInterceptors(serviceCollection.AddEntityFrameworkDuckDB(), injectedInterceptors);
    }

    public class TransactionInterceptionDuckDBTest : TransactionInterceptionDuckDBTestBase, IClassFixture<TransactionInterceptionDuckDBTest.InterceptionDuckDBFixture>
    {
        public TransactionInterceptionDuckDBTest(InterceptionDuckDBFixture fixture) : base(fixture)
        {
        }

        public class InterceptionDuckDBFixture : InterceptionDuckDBFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener
                => false;
        }
    }

    public class TransactionInterceptionWithDiagnosticsDuckDBTest : TransactionInterceptionDuckDBTestBase, IClassFixture<TransactionInterceptionWithDiagnosticsDuckDBTest.InterceptionDuckDBFixture>
    {
        public TransactionInterceptionWithDiagnosticsDuckDBTest(InterceptionDuckDBFixture fixture) : base(fixture)
        {
        }

        public class InterceptionDuckDBFixture : InterceptionDuckDBFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener
                => true;
        }
    }
}
