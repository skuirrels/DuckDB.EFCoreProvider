using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;

public class OwnedJsonProjectionDuckDBTest: OwnedJsonProjectionRelationalTestBase<OwnedJsonDuckDBFixture>
{
    public OwnedJsonProjectionDuckDBTest(OwnedJsonDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture, testOutputHelper)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_root(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.Select_root(queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_root_duplicated(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.Select_root_duplicated(queryTrackingBehavior);
    }
}
