using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;

public class OwnedJsonCollectionDuckDBTest: OwnedJsonCollectionRelationalTestBase<OwnedJsonDuckDBFixture>
{
    public OwnedJsonCollectionDuckDBTest(OwnedJsonDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
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
    public override Task Index_column()
    {
        return base.Index_column();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Index_on_nested_collection()
    {
        return base.Index_on_nested_collection();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Index_constant()
    {
        return base.Index_constant();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Index_out_of_bounds()
    {
        return base.Index_out_of_bounds();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Index_parameter()
    {
        return base.Index_parameter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task OrderBy_ElementAt()
    {
        return base.OrderBy_ElementAt();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_within_Select_within_Select_with_aggregates()
    {
        return base.Select_within_Select_within_Select_with_aggregates();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Where()
    {
        return base.Where();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Distinct_over_projected_filtered_nested_collection()
    {
        return base.Distinct_over_projected_filtered_nested_collection();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Distinct_over_projected_nested_collection()
    {
        return base.Distinct_over_projected_nested_collection();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Distinct_projected(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.Distinct_projected(queryTrackingBehavior);
    }
}