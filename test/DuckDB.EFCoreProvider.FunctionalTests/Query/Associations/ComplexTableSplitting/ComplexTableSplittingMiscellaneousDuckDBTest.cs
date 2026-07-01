using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.ComplexTableSplitting;

public class ComplexTableSplittingMiscellaneousDuckDBTest: ComplexTableSplittingMiscellaneousRelationalTestBase<ComplexTableSplittingDuckDBFixture>
{
    public ComplexTableSplittingMiscellaneousDuckDBTest(ComplexTableSplittingDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }
}