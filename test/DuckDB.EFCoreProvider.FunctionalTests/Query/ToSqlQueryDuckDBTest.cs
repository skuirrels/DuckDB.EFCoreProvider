using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class ToSqlQueryDuckDBTest : ToSqlQueryTestBase
{
    public ToSqlQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    public override Task Entity_type_with_navigation_mapped_to_SqlQuery(bool async)
    {
        return base.Entity_type_with_navigation_mapped_to_SqlQuery(async);
    }

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;
}