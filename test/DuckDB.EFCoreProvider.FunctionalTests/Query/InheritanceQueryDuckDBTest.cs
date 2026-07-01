using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class InheritanceQueryDuckDBTest : TPHInheritanceQueryTestBase<TPHInheritanceQueryDuckDBFixture>
{
    public InheritanceQueryDuckDBTest(TPHInheritanceQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Byte_enum_value_constant_used_in_projection(bool async)
    {
        return base.Byte_enum_value_constant_used_in_projection(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Setting_foreign_key_to_a_different_type_throws()
    {
        return base.Setting_foreign_key_to_a_different_type_throws();
    }

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());
}
