using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.BulkUpdates;

public class TPCFiltersInheritanceBulkUpdatesDuckDBTest : TPCFiltersInheritanceBulkUpdatesTestBase<TPCFiltersInheritanceBulkUpdatesDuckDBFixture>
{
    public TPCFiltersInheritanceBulkUpdatesDuckDBTest(TPCFiltersInheritanceBulkUpdatesDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    protected override void ClearLog()
    {
        Fixture.TestSqlLoggerFactory.Clear();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.ReferencedRowsCannotBeUpdated)]
    public override Task Delete_where_using_hierarchy(bool async)
    {
        return base.Delete_where_using_hierarchy(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.ReferencedRowsCannotBeUpdated)]
    public override Task Delete_where_using_hierarchy_derived(bool async)
    {
        return base.Delete_where_using_hierarchy_derived(async);
    }
}