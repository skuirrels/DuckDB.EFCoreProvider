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
}
