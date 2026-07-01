using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class QueryNoClientEvalDuckDBTest : QueryNoClientEvalTestBase<QueryNoClientEvalDuckDBFixture>
{
    public QueryNoClientEvalDuckDBTest(QueryNoClientEvalDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }
}
