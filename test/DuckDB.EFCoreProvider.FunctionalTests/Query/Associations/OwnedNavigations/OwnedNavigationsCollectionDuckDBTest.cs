using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.OwnedNavigations;

public class OwnedNavigationsCollectionDuckDBTest : OwnedNavigationsCollectionRelationalTestBase<OwnedNavigationsDuckDBFixture>
{
    public OwnedNavigationsCollectionDuckDBTest(OwnedNavigationsDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Count()
    {
        return base.Count();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Distinct()
    {
        return base.Distinct();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GroupBy()
    {
        return base.GroupBy();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task OrderBy_ElementAt()
    {
        return base.OrderBy_ElementAt();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Where()
    {
        return base.Where();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Distinct_projected(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.Distinct_projected(queryTrackingBehavior);
    }
}
