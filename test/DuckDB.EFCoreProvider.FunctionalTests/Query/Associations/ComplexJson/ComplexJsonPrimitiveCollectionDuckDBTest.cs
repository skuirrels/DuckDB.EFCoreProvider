using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;

public class ComplexJsonPrimitiveCollectionDuckDBTest: ComplexJsonPrimitiveCollectionRelationalTestBase<ComplexJsonDuckDBFixture>
{
    public ComplexJsonPrimitiveCollectionDuckDBTest(ComplexJsonDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture, testOutputHelper)
    {
    }
}
