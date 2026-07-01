using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore;

public class TPTTableSplittingDuckDBTest : TPTTableSplittingTestBase
{
    public TPTTableSplittingDuckDBTest(NonSharedFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_insert_dependent_with_just_one_parent()
    {
        await base.Can_insert_dependent_with_just_one_parent();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task No_warn_when_save_optional_dependent_at_least_one_none_null()
    {
        await base.No_warn_when_save_optional_dependent_at_least_one_none_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Warn_when_save_optional_dependent_with_null_values()
    {
        await base.Warn_when_save_optional_dependent_with_null_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Warn_when_save_optional_dependent_with_null_values_sensitive()
    {
        await base.Warn_when_save_optional_dependent_with_null_values_sensitive();
    }

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;
}