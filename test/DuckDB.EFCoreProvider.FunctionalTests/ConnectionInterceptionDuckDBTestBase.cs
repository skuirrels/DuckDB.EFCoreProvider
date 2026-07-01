using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public abstract class ConnectionInterceptionDuckDBTestBase : ConnectionInterceptionTestBase
{
    protected ConnectionInterceptionDuckDBTestBase(InterceptionFixtureBase fixture) : base(fixture)
    {
    }

    protected override DbContextOptionsBuilder ConfigureProvider(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseDuckDB();

    protected override BadUniverseContext CreateBadUniverse(DbContextOptionsBuilder optionsBuilder)
        => new(optionsBuilder.UseDuckDB("Data Source=file:data.db?mode=invalidmode").Options);

    public abstract class InterceptionDuckDBFixtureBase : InterceptionFixtureBase
    {
        protected override string StoreName
            => "ConnectionInterception";

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors)
            => base.InjectInterceptors(serviceCollection.AddEntityFrameworkDuckDB(), injectedInterceptors);
    }

    public class ConnectionInterceptionDuckDBTest(ConnectionInterceptionDuckDBTest.InterceptionDuckDBFixture fixture)
        : ConnectionInterceptionDuckDBTestBase(fixture), IClassFixture<ConnectionInterceptionDuckDBTest.InterceptionDuckDBFixture>
    {
        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_connection_creation_passively(bool async)
        {
            await base.Intercept_connection_creation_passively(async);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_connection_to_override_connection_after_creation(bool async)
        {
            await base.Intercept_connection_to_override_connection_after_creation(async);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_connection_to_override_creation(bool async)
        {
            await base.Intercept_connection_to_override_creation(async);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_connection_to_suppress_dispose(bool async)
        {
            await base.Intercept_connection_to_suppress_dispose(async);
        }

        public class InterceptionDuckDBFixture : InterceptionDuckDBFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener
                => false;
        }
    }

    public class ConnectionInterceptionWithDiagnosticsDuckDBTest(
        ConnectionInterceptionWithDiagnosticsDuckDBTest.InterceptionDuckDBFixture fixture)
        : ConnectionInterceptionDuckDBTestBase(fixture),
            IClassFixture<ConnectionInterceptionWithDiagnosticsDuckDBTest.InterceptionDuckDBFixture>
    {
        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_connection_creation_passively(bool async)
        {
            await base.Intercept_connection_creation_passively(async);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_connection_to_override_connection_after_creation(bool async)
        {
            await base.Intercept_connection_to_override_connection_after_creation(async);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_connection_to_override_creation(bool async)
        {
            await base.Intercept_connection_to_override_creation(async);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_connection_to_suppress_dispose(bool async)
        {
            await base.Intercept_connection_to_suppress_dispose(async);
        }

        public class InterceptionDuckDBFixture : InterceptionDuckDBFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener
                => true;
        }
    }
}
