using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class WarningsDuckDBTest: WarningsTestBase<QueryNoClientEvalDuckDBFixture>
{
    public WarningsDuckDBTest(QueryNoClientEvalDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}