using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.ComplexTableSplitting;

public class ComplexTableSplittingStructuralEqualityDuckDBTest: ComplexTableSplittingStructuralEqualityRelationalTestBase<ComplexTableSplittingDuckDBFixture>
{
    public ComplexTableSplittingStructuralEqualityDuckDBTest(ComplexTableSplittingDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }
}