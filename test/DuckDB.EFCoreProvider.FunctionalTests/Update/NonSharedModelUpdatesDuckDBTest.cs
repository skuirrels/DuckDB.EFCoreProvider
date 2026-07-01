using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Update;

public class NonSharedModelUpdatesDuckDBTest : NonSharedModelUpdatesTestBase
{
    public NonSharedModelUpdatesDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task DbUpdateException_Entries_is_correct_with_multiple_inserts(bool async)
    {
        return base.DbUpdateException_Entries_is_correct_with_multiple_inserts(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Principal_and_dependent_roundtrips_with_cycle_breaking(bool async)
    {
        return base.Principal_and_dependent_roundtrips_with_cycle_breaking(async);
    }

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;
}
