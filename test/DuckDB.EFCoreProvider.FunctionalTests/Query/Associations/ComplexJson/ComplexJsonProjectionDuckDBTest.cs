using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;

public class ComplexJsonProjectionDuckDBTest: ComplexJsonProjectionRelationalTestBase<ComplexJsonDuckDBFixture>
{
    public ComplexJsonProjectionDuckDBTest(ComplexJsonDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture, testOutputHelper)
    {
    }
}
