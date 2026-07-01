using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

[CollectionDefinition("CommandInterceptionDuckDB", DisableParallelization = true)]
public class CommandInterceptionDuckDBCollection;

public abstract class CommandInterceptionDuckDBTestBase : CommandInterceptionTestBase
{
    public CommandInterceptionDuckDBTestBase(InterceptionDuckDBFixtureBase fixture)
        : base(fixture)
    {
    }

    public abstract class InterceptionDuckDBFixtureBase : InterceptionFixtureBase
    {
        protected override string StoreName
            => "CommandInterception";

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors)
            => base.InjectInterceptors(serviceCollection.AddEntityFrameworkDuckDB(), injectedInterceptors);
    }

    [Collection("CommandInterceptionDuckDB")]
    public class CommandInterceptionDuckDBTest(CommandInterceptionDuckDBTest.InterceptionDuckDBFixture fixture)
        : CommandInterceptionDuckDBTestBase(fixture), IClassFixture<CommandInterceptionDuckDBTest.InterceptionDuckDBFixture>
    {
        public class InterceptionDuckDBFixture : InterceptionDuckDBFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener
                => false;
        }
    }

    [Collection("CommandInterceptionDuckDB")]
    public class CommandInterceptionWithDiagnosticsDuckDBTest(
        CommandInterceptionWithDiagnosticsDuckDBTest.InterceptionDuckDBFixture fixture)
        : CommandInterceptionDuckDBTestBase(fixture), IClassFixture<CommandInterceptionWithDiagnosticsDuckDBTest.InterceptionDuckDBFixture>
    {
        public class InterceptionDuckDBFixture : InterceptionDuckDBFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener
                => true;
        }
    }
}
