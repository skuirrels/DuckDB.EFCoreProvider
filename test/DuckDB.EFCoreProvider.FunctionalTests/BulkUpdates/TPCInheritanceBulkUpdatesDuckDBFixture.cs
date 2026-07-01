using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.BulkUpdates;

public class TPCInheritanceBulkUpdatesDuckDBFixture : TPCInheritanceBulkUpdatesFixture
{
    protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;

    public override bool UseGeneratedKeys => false;
}
