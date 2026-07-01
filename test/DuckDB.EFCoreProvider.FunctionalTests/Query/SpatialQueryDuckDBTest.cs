using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class SpatialQueryDuckDBTest : SpatialQueryRelationalTestBase<SpatialQueryDuckDBFixture>
{
    public SpatialQueryDuckDBTest(SpatialQueryDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Buffer_quadrantSegments(bool async)
    {
        return base.Buffer_quadrantSegments(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Distance_constant_srid_4326(bool async)
    {
        return base.Distance_constant_srid_4326(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetGeometryN(bool async)
    {
        return base.GetGeometryN(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GetGeometryN_with_null_argument(bool async)
    {
        return base.GetGeometryN_with_null_argument(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Item(bool async)
    {
        return base.Item(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Relate(bool async)
    {
        return base.Relate(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SRID(bool async)
    {
        return base.SRID(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SRID_geometry(bool async)
    {
        return base.SRID_geometry(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Union_void(bool async)
    {
        return base.Union_void(async);
    }
}
