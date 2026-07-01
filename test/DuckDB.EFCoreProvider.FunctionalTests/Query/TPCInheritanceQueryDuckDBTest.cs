using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class TPCInheritanceQueryDuckDBTest: TPCInheritanceQueryTestBase<TPCInheritanceQueryDuckDBFixture>
{
    public TPCInheritanceQueryDuckDBTest(TPCInheritanceQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_insert_update_delete()
    {
        return base.Can_insert_update_delete();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_query_all_animals(bool async)
    {
        return base.Can_query_all_animals(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetType_in_hierarchy_in_leaf_type_with_sibling(bool async)
    {
        return base.GetType_in_hierarchy_in_leaf_type_with_sibling(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Setting_foreign_key_to_a_different_type_throws()
    {
        return base.Setting_foreign_key_to_a_different_type_throws();
    }

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());
}
