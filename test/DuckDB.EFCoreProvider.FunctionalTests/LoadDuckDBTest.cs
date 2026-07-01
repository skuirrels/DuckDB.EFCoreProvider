using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class LoadDuckDBTest : LoadTestBase<LoadDuckDBTest.LoadDuckDBFixture>
{
    public LoadDuckDBTest(LoadDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Lazy_load_one_to_one_PK_to_PK_reference_to_principal_already_loaded(EntityState state, QueryTrackingBehavior queryTrackingBehavior)
    {
        base.Lazy_load_one_to_one_PK_to_PK_reference_to_principal_already_loaded(state, queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_one_to_one_PK_to_PK_reference_to_principal_already_loaded(EntityState state, bool async)
    {
        await base.Load_one_to_one_PK_to_PK_reference_to_principal_already_loaded(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Load_one_to_one_PK_to_PK_reference_to_principal_using_Query_already_loaded(EntityState state, bool async)
    {
        await base.Load_one_to_one_PK_to_PK_reference_to_principal_using_Query_already_loaded(state, async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Setting_navigation_to_null_is_detected_by_local_DetectChanges()
    {
        base.Setting_navigation_to_null_is_detected_by_local_DetectChanges();
    }

    public class LoadDuckDBFixture : LoadFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}