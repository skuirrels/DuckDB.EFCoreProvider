using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query.Associations.OwnedJson;

public class OwnedJsonStructuralEqualityDuckDBTest: OwnedJsonStructuralEqualityRelationalTestBase<OwnedJsonDuckDBFixture>
{
    public OwnedJsonStructuralEqualityDuckDBTest(OwnedJsonDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture, testOutputHelper)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Associate_with_inline_null()
    {
        return base.Associate_with_inline_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Nested_associate_with_inline_null()
    {
        return base.Nested_associate_with_inline_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_with_nested_and_composed_operators()
    {
        return base.Contains_with_nested_and_composed_operators();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_with_operators_composed_on_the_collection()
    {
        return base.Contains_with_operators_composed_on_the_collection();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_with_parameter()
    {
        return base.Contains_with_parameter();
    }
}
