using Microsoft.EntityFrameworkCore.ChangeTracking;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class DataBindingDuckDBTest : DataBindingTestBase<F1DuckDBFixture>
{
    public DataBindingDuckDBTest(F1DuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Entity_removed_from_navigation_property_binding_list_is_removed_from_nav_property_but_not_marked_Deleted(CascadeTiming deleteOrphansTiming)
    {
        base.Entity_removed_from_navigation_property_binding_list_is_removed_from_nav_property_but_not_marked_Deleted(deleteOrphansTiming);
    }
}
