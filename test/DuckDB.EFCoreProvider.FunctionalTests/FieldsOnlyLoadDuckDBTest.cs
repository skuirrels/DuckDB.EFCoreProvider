using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class FieldsOnlyLoadDuckDBTest : FieldsOnlyLoadTestBase<FieldsOnlyLoadDuckDBTest.FieldsOnlyLoadDuckDBFixture>
{
    public FieldsOnlyLoadDuckDBTest(FieldsOnlyLoadDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Load_one_to_one_PK_to_PK_reference_to_dependent_already_loaded(EntityState state, bool async)
    {
        return base.Load_one_to_one_PK_to_PK_reference_to_dependent_already_loaded(state, async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Load_one_to_one_PK_to_PK_reference_to_dependent_using_Query_already_loaded(EntityState state, bool async)
    {
        return base.Load_one_to_one_PK_to_PK_reference_to_dependent_using_Query_already_loaded(state, async);
    }

    public class FieldsOnlyLoadDuckDBFixture : FieldsOnlyLoadFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
