using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;

public class OwnedJsonBulkUpdateDuckDBTest: OwnedJsonBulkUpdateRelationalTestBase<OwnedJsonDuckDBFixture>
{
    public OwnedJsonBulkUpdateDuckDBTest(OwnedJsonDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }
}