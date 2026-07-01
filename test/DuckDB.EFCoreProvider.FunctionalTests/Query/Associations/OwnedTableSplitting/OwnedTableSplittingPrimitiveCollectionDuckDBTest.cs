using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.OwnedTableSplitting;

public class OwnedTableSplittingPrimitiveCollectionDuckDBTest : OwnedTableSplittingPrimitiveCollectionRelationalTestBase<OwnedTableSplittingDuckDBFixture>
{
    public OwnedTableSplittingPrimitiveCollectionDuckDBTest(OwnedTableSplittingDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Any_predicate()
    {
        return base.Any_predicate();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains()
    {
        return base.Contains();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Count()
    {
        return base.Count();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Index()
    {
        return base.Index();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_Count()
    {
        return base.Nested_Count();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_Sum()
    {
        return base.Select_Sum();
    }
}