using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.OwnedNavigations;

public class OwnedNavigationsSetOperationsDuckDBTest : OwnedNavigationsSetOperationsRelationalTestBase<OwnedNavigationsDuckDBFixture>
{
    public OwnedNavigationsSetOperationsDuckDBTest(OwnedNavigationsDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Over_associate_collections()
    {
        return base.Over_associate_collections();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Over_nested_associate_collection()
    {
        return base.Over_nested_associate_collection();
    }
}