using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class SerializationDuckDBTest : SerializationTestBase<F1DuckDBFixture>
{
    public SerializationDuckDBTest(F1DuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_round_trip_through_JSON(bool useNewtonsoft, bool ignoreLoops, bool writeIndented)
    {
        base.Can_round_trip_through_JSON(useNewtonsoft, ignoreLoops, writeIndented);
    }
}