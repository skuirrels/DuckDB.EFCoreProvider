using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public abstract class SaveChangesInterceptionDuckDBTestBase(SaveChangesInterceptionDuckDBTestBase.InterceptionDuckDBFixtureBase fixture)
    : SaveChangesInterceptionTestBase(fixture)
{
    public abstract class InterceptionDuckDBFixtureBase : InterceptionTestBase.InterceptionFixtureBase
    {
        protected override string StoreName
            => "SaveChangesInterception";

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        protected override IServiceCollection InjectInterceptors(
            IServiceCollection serviceCollection,
            IEnumerable<IInterceptor> injectedInterceptors)
            => base.InjectInterceptors(serviceCollection.AddEntityFrameworkDuckDB(), injectedInterceptors);
    }

    public class SaveChangesInterceptionDuckDBTest(SaveChangesInterceptionDuckDBTest.InterceptionDuckDBFixture fixture)
        : SaveChangesInterceptionDuckDBTestBase(fixture), IClassFixture<SaveChangesInterceptionDuckDBTest.InterceptionDuckDBFixture>
    {
        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_SaveChanges_failed(bool async, bool inject, bool noAcceptChanges, bool concurrencyError)
        {
            await base.Intercept_SaveChanges_failed(async, inject, noAcceptChanges, concurrencyError);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_SaveChanges_passively(bool async, bool inject, bool noAcceptChanges)
        {
            await base.Intercept_SaveChanges_passively(async, inject, noAcceptChanges);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_SaveChanges_with_multiple_interceptors(bool async, bool inject, bool noAcceptChanges)
        {
            await base.Intercept_SaveChanges_with_multiple_interceptors(async, inject, noAcceptChanges);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_to_suppress_concurrency_exception(bool async, bool inject, bool noAcceptChanges)
        {
            await base.Intercept_to_suppress_concurrency_exception(async, inject, noAcceptChanges);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_SaveChanges_to_change_result(bool async, bool inject, bool noAcceptChanges)
        {
            await base.Intercept_SaveChanges_to_change_result(async, inject, noAcceptChanges);
        }

        public class InterceptionDuckDBFixture : InterceptionDuckDBFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener
                => false;
        }
    }

    public class SaveChangesInterceptionWithDiagnosticsDuckDBTest(
        SaveChangesInterceptionWithDiagnosticsDuckDBTest.InterceptionDuckDBFixture fixture)
        : SaveChangesInterceptionDuckDBTestBase(fixture),
            IClassFixture<SaveChangesInterceptionWithDiagnosticsDuckDBTest.InterceptionDuckDBFixture>
    {
        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_SaveChanges_failed(bool async, bool inject, bool noAcceptChanges, bool concurrencyError)
        {
            await base.Intercept_SaveChanges_failed(async, inject, noAcceptChanges, concurrencyError);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_SaveChanges_passively(bool async, bool inject, bool noAcceptChanges)
        {
            await base.Intercept_SaveChanges_passively(async, inject, noAcceptChanges);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_SaveChanges_with_multiple_interceptors(bool async, bool inject, bool noAcceptChanges)
        {
            await base.Intercept_SaveChanges_with_multiple_interceptors(async, inject, noAcceptChanges);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_to_suppress_concurrency_exception(bool async, bool inject, bool noAcceptChanges)
        {
            await base.Intercept_to_suppress_concurrency_exception(async, inject, noAcceptChanges);
        }

        [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
        public override async Task Intercept_SaveChanges_to_change_result(bool async, bool inject, bool noAcceptChanges)
        {
            await base.Intercept_SaveChanges_to_change_result(async, inject, noAcceptChanges);
        }

        public class InterceptionDuckDBFixture : InterceptionDuckDBFixtureBase
        {
            protected override bool ShouldSubscribeToDiagnosticListener
                => true;
        }
    }
}