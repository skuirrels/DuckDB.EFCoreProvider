using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.ComplexJson;

public class ComplexJsonMiscellaneousDuckDBTest : ComplexJsonMiscellaneousRelationalTestBase<ComplexJsonDuckDBFixture>
{
    public ComplexJsonMiscellaneousDuckDBTest(ComplexJsonDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture, testOutputHelper)
    {
    }
}
