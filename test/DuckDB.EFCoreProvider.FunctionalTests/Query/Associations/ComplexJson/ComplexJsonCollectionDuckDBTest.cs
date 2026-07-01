using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;

public class ComplexJsonCollectionDuckDBTest : ComplexJsonCollectionRelationalTestBase<ComplexJsonDuckDBFixture>
{
    public ComplexJsonCollectionDuckDBTest(ComplexJsonDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture, testOutputHelper)
    {
    }
}
