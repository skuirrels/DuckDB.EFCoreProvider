using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class TPCFiltersInheritanceQueryDuckDBTest : TPCFiltersInheritanceQueryTestBase<TPCFiltersInheritanceQueryDuckDBFixture>
{
    public TPCFiltersInheritanceQueryDuckDBTest(TPCFiltersInheritanceQueryDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_derived_set(bool async)
    {
        return base.Can_use_derived_set(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_IgnoreQueryFilters_and_GetDatabaseValues(bool async)
    {
        return base.Can_use_IgnoreQueryFilters_and_GetDatabaseValues(async);
    }
    
    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_is_kiwi_in_projection(bool async)
    {
        return base.Can_use_is_kiwi_in_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_of_type_animal(bool async)
    {
        return base.Can_use_of_type_animal(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_of_type_bird(bool async)
    {
        return base.Can_use_of_type_bird(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_use_of_type_bird_with_projection(bool async)
    {
        return base.Can_use_of_type_bird_with_projection(async);
    }
}