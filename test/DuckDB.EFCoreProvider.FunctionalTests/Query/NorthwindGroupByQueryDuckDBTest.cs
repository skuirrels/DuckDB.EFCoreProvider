using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindGroupByQueryDuckDBTest : NorthwindGroupByQueryRelationalTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindGroupByQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper) : base(fixture)
    {
        fixture.TestSqlLoggerFactory.Clear();
        fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GroupBy_aggregate_projecting_conditional_expression(bool async)
    {
        return base.GroupBy_aggregate_projecting_conditional_expression(async);
    }

#if NET11_0_OR_GREATER
    [ConditionalTheory(Skip = DuckDBSkipReasons.UnorderedFirstInGroup)]
    public override Task GroupBy_Select_Entire_Entity_Where(bool async)
    {
        return base.GroupBy_Select_Entire_Entity_Where(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.UnorderedFirstInGroup)]
    public override Task GroupBy_Select_Entire_Entity_Order(bool async)
    {
        return base.GroupBy_Select_Entire_Entity_Order(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.UnorderedFirstInGroup)]
    public override Task GroupBy_Select_Entire_Entity_composite_key_Select(bool async)
    {
        return base.GroupBy_Select_Entire_Entity_composite_key_Select(async);
    }
#endif
}