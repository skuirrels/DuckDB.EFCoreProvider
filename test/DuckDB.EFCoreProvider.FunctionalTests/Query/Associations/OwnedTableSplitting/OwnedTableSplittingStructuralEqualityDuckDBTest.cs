using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.OwnedTableSplitting;

public class OwnedTableSplittingStructuralEqualityDuckDBTest : OwnedTableSplittingStructuralEqualityRelationalTestBase<OwnedTableSplittingDuckDBFixture>
{
    public OwnedTableSplittingStructuralEqualityDuckDBTest(OwnedTableSplittingDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Associate_with_inline_null()
    {
        return base.Associate_with_inline_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Associate_with_parameter_null()
    {
        return base.Associate_with_parameter_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_associate_with_inline_null()
    {
        return base.Nested_associate_with_inline_null();
    }
}