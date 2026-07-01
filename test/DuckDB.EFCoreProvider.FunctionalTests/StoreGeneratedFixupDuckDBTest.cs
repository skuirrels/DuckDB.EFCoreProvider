using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class StoreGeneratedFixupDuckDBTest : StoreGeneratedFixupRelationalTestBase<
    StoreGeneratedFixupDuckDBTest.StoreGeneratedFixupDuckDBFixture>
{
    public StoreGeneratedFixupDuckDBTest(StoreGeneratedFixupDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_many_FK_set_both_navs_set()
    {
        await base.Add_dependent_but_not_principal_one_to_many_FK_set_both_navs_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_many_FK_not_set_both_navs_set()
    {
        await base.Add_dependent_but_not_principal_one_to_many_FK_not_set_both_navs_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_many_FK_set_no_navs_set()
    {
        await base.Add_dependent_but_not_principal_one_to_many_FK_set_no_navs_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_many_FK_set_principal_nav_set()
    {
        await base.Add_dependent_but_not_principal_one_to_many_FK_set_principal_nav_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_many_FK_set_dependent_nav_set()
    {
        await base.Add_dependent_but_not_principal_one_to_many_FK_set_dependent_nav_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_many_FK_not_set_principal_nav_set()
    {
        await base.Add_dependent_but_not_principal_one_to_many_FK_not_set_principal_nav_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_many_FK_not_set_dependent_nav_set()
    {
        await base.Add_dependent_but_not_principal_one_to_many_FK_not_set_dependent_nav_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_many_prin_uni_FK_set_no_navs_set()
    {
        await base.Add_dependent_but_not_principal_one_to_many_prin_uni_FK_set_no_navs_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_many_prin_uni_FK_set_principal_nav_set()
    {
        await base.Add_dependent_but_not_principal_one_to_many_prin_uni_FK_set_principal_nav_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_many_prin_uni_FK_not_set_principal_nav_set()
    {
        await base.Add_dependent_but_not_principal_one_to_many_prin_uni_FK_not_set_principal_nav_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_many_dep_uni_FK_set_no_navs_set()
    {
        await base.Add_dependent_but_not_principal_one_to_many_dep_uni_FK_set_no_navs_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_many_no_navs_FK_set_no_navs_set()
    {
        await base.Add_dependent_but_not_principal_one_to_many_no_navs_FK_set_no_navs_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_one_FK_set_both_navs_set()
    {
        await base.Add_dependent_but_not_principal_one_to_one_FK_set_both_navs_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_one_FK_not_set_both_navs_set()
    {
        await base.Add_dependent_but_not_principal_one_to_one_FK_not_set_both_navs_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_one_FK_set_no_navs_set()
    {
        await base.Add_dependent_but_not_principal_one_to_one_FK_set_no_navs_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_one_FK_set_principal_nav_set()
    {
        await base.Add_dependent_but_not_principal_one_to_one_FK_set_principal_nav_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_one_FK_set_dependent_nav_set()
    {
        await base.Add_dependent_but_not_principal_one_to_one_FK_set_dependent_nav_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_one_FK_not_set_principal_nav_set()
    {
        await base.Add_dependent_but_not_principal_one_to_one_FK_not_set_principal_nav_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_one_prin_uni_FK_set_no_navs_set()
    {
        await base.Add_dependent_but_not_principal_one_to_one_prin_uni_FK_set_no_navs_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_one_prin_uni_FK_set_principal_nav_set()
    {
        await base.Add_dependent_but_not_principal_one_to_one_prin_uni_FK_set_principal_nav_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_one_prin_uni_FK_not_set_principal_nav_set()
    {
        await base.Add_dependent_but_not_principal_one_to_one_prin_uni_FK_not_set_principal_nav_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_one_dep_uni_FK_set_no_navs_set()
    {
        await base.Add_dependent_but_not_principal_one_to_one_dep_uni_FK_set_no_navs_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Add_dependent_but_not_principal_one_to_one_no_navs_FK_set_no_navs_set()
    {
        await base.Add_dependent_but_not_principal_one_to_one_no_navs_FK_set_no_navs_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Multi_level_add_replace_and_save()
    {
        await base.Multi_level_add_replace_and_save();
    }

    protected override bool EnforcesFKs
        => true;

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());

    public class StoreGeneratedFixupDuckDBFixture : StoreGeneratedFixupRelationalFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
