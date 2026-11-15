namespace Microsoft.EntityFrameworkCore.BulkUpdates;

public class TPHFiltersInheritanceBulkUpdatesDuckDBFixture : TPHInheritanceBulkUpdatesDuckDBFixture
{
    public override bool EnableFilters => true;
}