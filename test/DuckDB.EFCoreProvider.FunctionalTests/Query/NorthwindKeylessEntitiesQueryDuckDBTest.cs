using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindKeylessEntitiesQueryDuckDBTest: NorthwindKeylessEntitiesQueryRelationalTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindKeylessEntitiesQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Entity_mapped_to_view_on_right_side_of_join(bool async)
    {
        return base.Entity_mapped_to_view_on_right_side_of_join(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task KeylessEntity_by_database_view(bool async)
    {
        return base.KeylessEntity_by_database_view(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task KeylessEntity_with_nav_defining_query(bool async)
    {
        return base.KeylessEntity_with_nav_defining_query(async);
    }
}
