using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.ComplexTableSplitting;

public class ComplexTableSplittingProjectionDuckDBTest: ComplexTableSplittingProjectionRelationalTestBase<ComplexTableSplittingDuckDBFixture>
{
    public ComplexTableSplittingProjectionDuckDBTest(ComplexTableSplittingDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }
}
