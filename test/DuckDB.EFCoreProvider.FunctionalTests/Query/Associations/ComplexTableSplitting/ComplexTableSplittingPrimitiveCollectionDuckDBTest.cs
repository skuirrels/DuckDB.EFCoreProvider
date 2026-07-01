using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.ComplexTableSplitting;

public class ComplexTableSplittingPrimitiveCollectionDuckDBTest: ComplexTableSplittingPrimitiveCollectionRelationalTestBase<ComplexTableSplittingDuckDBFixture>
{
    public ComplexTableSplittingPrimitiveCollectionDuckDBTest(ComplexTableSplittingDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture, testOutputHelper)
    {
    }
}
