using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.BulkUpdates;

public class TPCInheritanceBulkUpdatesDuckDBTest : TPCInheritanceBulkUpdatesTestBase<TPCInheritanceBulkUpdatesDuckDBFixture>
{
    public TPCInheritanceBulkUpdatesDuckDBTest(TPCInheritanceBulkUpdatesDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture, testOutputHelper)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Update_base_and_derived_types(bool async)
    {
        return base.Update_base_and_derived_types(async);
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