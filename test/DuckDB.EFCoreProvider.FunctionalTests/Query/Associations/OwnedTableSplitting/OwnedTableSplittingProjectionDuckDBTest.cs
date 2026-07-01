using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.OwnedTableSplitting;

public class OwnedTableSplittingProjectionDuckDBTest : OwnedTableSplittingProjectionRelationalTestBase<OwnedTableSplittingDuckDBFixture>
{
    public OwnedTableSplittingProjectionDuckDBTest(OwnedTableSplittingDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
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

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_associate(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.Select_associate(queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_associate_collection(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.Select_associate_collection(queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_optional_associate(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.Select_optional_associate(queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_subquery_optional_related_FirstOrDefault(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.Select_subquery_optional_related_FirstOrDefault(queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_subquery_required_related_FirstOrDefault(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.Select_subquery_required_related_FirstOrDefault(queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_associate_collection(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.SelectMany_associate_collection(queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_required_associate_via_optional_navigation(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.Select_required_associate_via_optional_navigation(queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Select_subquery_FirstOrDefault_complex_collection(QueryTrackingBehavior queryTrackingBehavior)
    {
        await base.Select_subquery_FirstOrDefault_complex_collection(queryTrackingBehavior);
    }
}