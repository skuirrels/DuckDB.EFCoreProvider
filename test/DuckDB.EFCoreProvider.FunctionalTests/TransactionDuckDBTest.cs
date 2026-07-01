using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class TransactionDuckDBTest : TransactionTestBase<TransactionDuckDBTest.TransactionDuckDBFixture>
{
    public TransactionDuckDBTest(TransactionDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task SaveChanges_uses_explicit_transaction_with_failure_behavior(bool async,
        AutoTransactionBehavior autoTransactionBehavior)
    {
        await base.SaveChanges_uses_explicit_transaction_with_failure_behavior(async, autoTransactionBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Query_uses_explicit_transaction(AutoTransactionBehavior autoTransactionBehavior)
    {
        base.Query_uses_explicit_transaction(autoTransactionBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task QueryAsync_uses_explicit_transaction(AutoTransactionBehavior autoTransactionBehavior)
    {
        await base.QueryAsync_uses_explicit_transaction(autoTransactionBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task SaveChanges_implicitly_creates_savepoint(bool async)
    {
        await base.SaveChanges_implicitly_creates_savepoint(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task SaveChanges_can_be_used_with_no_savepoint(bool async)
    {
        await base.SaveChanges_can_be_used_with_no_savepoint(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Savepoint_can_be_rolled_back(bool async)
    {
        await base.Savepoint_can_be_rolled_back(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Savepoint_can_be_released(bool async)
    {
        await base.Savepoint_can_be_released(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Savepoint_name_is_quoted(bool async)
    {
        await base.Savepoint_name_is_quoted(async);
    }

    protected override bool SnapshotSupported
        => false;

    protected override bool SavepointsSupported => false;

    protected override DbContext CreateContextWithConnectionString()
    {
        var options = Fixture.AddOptions(
                new DbContextOptionsBuilder().UseDuckDB(
                        TestStore.ConnectionString,
                        b => b.ReverseNullOrdering())
                    .ConfigureWarnings(w => w.Log(RelationalEventId.AmbientTransactionWarning)))
            .UseInternalServiceProvider(Fixture.ServiceProvider);

        return new DbContext(options.Options);
    }

    public class TransactionDuckDBFixture : TransactionFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public override async Task ReseedAsync()
        {
            using var context = CreateContext();
            context.Set<TransactionCustomer>().RemoveRange(await context.Set<TransactionCustomer>().ToListAsync());
            context.Set<TransactionOrder>().RemoveRange(await context.Set<TransactionOrder>().ToListAsync());
            await context.SaveChangesAsync();

            await SeedAsync(context);
        }

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder)
                .ConfigureWarnings(w => w.Log(RelationalEventId.AmbientTransactionWarning));
    }
}
