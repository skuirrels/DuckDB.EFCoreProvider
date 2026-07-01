using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class ConferencePlannerDuckDBTest : ConferencePlannerTestBase<ConferencePlannerDuckDBTest.ConferencePlannerDuckDBFixture>
{
    public ConferencePlannerDuckDBTest(ConferencePlannerDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task AttendeesController_RemoveSession()
    {
        await base.AttendeesController_RemoveSession();
    }

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());

    public class ConferencePlannerDuckDBFixture : ConferencePlannerFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
