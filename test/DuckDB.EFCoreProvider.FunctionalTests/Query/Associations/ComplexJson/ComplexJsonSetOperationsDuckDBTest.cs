using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;

public class ComplexJsonSetOperationsDuckDBTest: ComplexJsonSetOperationsRelationalTestBase<ComplexJsonDuckDBFixture>
{
    public ComplexJsonSetOperationsDuckDBTest(ComplexJsonDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Over_assocate_collection_Select_nested_with_aggregates_projected(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.Over_assocate_collection_Select_nested_with_aggregates_projected(queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Over_associate_collections()
    {
        return base.Over_associate_collections();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Over_nested_associate_collection()
    {
        return base.Over_nested_associate_collection();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Over_associate_collection_projected(QueryTrackingBehavior queryTrackingBehavior)
    {
        return base.Over_associate_collection_projected(queryTrackingBehavior);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Over_different_collection_properties()
    {
        return base.Over_different_collection_properties();
    }
}
