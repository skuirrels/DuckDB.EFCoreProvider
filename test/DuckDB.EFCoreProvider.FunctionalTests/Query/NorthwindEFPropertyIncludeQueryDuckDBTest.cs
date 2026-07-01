using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindEFPropertyIncludeQueryDuckDBTest : NorthwindEFPropertyIncludeQueryTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindEFPropertyIncludeQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        fixture.TestSqlLoggerFactory.Clear();
        fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_last_no_orderby(bool async)
    {
        return base.Include_collection_with_last_no_orderby(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_multiple_conditional_order_by(bool async)
    {
        return base.Include_collection_with_multiple_conditional_order_by(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_collection_with_outer_apply_with_filter(bool async)
    {
        return base.Include_collection_with_outer_apply_with_filter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Repro9735(bool async)
    {
        return base.Repro9735(async);
    }
}
