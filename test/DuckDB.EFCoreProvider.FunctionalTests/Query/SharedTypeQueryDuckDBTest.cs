using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class SharedTypeQueryDuckDBTest : SharedTypeQueryRelationalTestBase
{
    public SharedTypeQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Ad_hoc_query_for_default_shared_type_entity_type_throws()
    {
        return base.Ad_hoc_query_for_default_shared_type_entity_type_throws();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Ad_hoc_query_for_shared_type_entity_type_works()
    {
        return base.Ad_hoc_query_for_shared_type_entity_type_works();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_shared_type_entity_type_in_query_filter_with_from_sql(bool async)
    {
        return base.Can_use_shared_type_entity_type_in_query_filter_with_from_sql(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_shared_type_entity_type_in_query_filter(bool async)
    {
        return base.Can_use_shared_type_entity_type_in_query_filter(async);
    }

    protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;
}