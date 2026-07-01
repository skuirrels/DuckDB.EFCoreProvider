using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.OwnedNavigations;

public class OwnedNavigationsMiscellaneousDuckDBTest: OwnedNavigationsMiscellaneousRelationalTestBase<OwnedNavigationsDuckDBFixture>
{
    public OwnedNavigationsMiscellaneousDuckDBTest(OwnedNavigationsDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Where_on_associate_scalar_property()
    {
        return base.Where_on_associate_scalar_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Where_on_nested_associate_scalar_property()
    {
        return base.Where_on_nested_associate_scalar_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Where_on_optional_associate_scalar_property()
    {
        return base.Where_on_optional_associate_scalar_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromSql_on_root()
    {
        return base.FromSql_on_root();
    }
}