namespace Microsoft.EntityFrameworkCore.BulkUpdates;

public class TPTFiltersInheritanceBulkUpdatesDuckDBFixture : TPTInheritanceBulkUpdatesDuckDBFixture
{
    public override bool EnableFilters => true;
}
