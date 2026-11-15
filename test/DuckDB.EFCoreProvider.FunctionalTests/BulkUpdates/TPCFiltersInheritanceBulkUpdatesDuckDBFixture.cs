namespace Microsoft.EntityFrameworkCore.BulkUpdates;

public class TPCFiltersInheritanceBulkUpdatesDuckDBFixture : TPCInheritanceBulkUpdatesDuckDBFixture
{
    public override bool EnableFilters => true;
}