using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.NTS.Extensions;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class BadDataJsonDeserializationDuckDBTest : BadDataJsonDeserializationTestBase
{
    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Throws_for_bad_collection_of_nullable_long_JSON_values(string json)
    {
        base.Throws_for_bad_collection_of_nullable_long_JSON_values(json);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Throws_for_bad_collection_of_sbyte_JSON_values(string json)
    {
        base.Throws_for_bad_collection_of_sbyte_JSON_values(json);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Throws_for_bad_nullable_long_JSON_values(string json)
    {
        base.Throws_for_bad_nullable_long_JSON_values(json);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Throws_for_bad_point_as_GeoJson(string json)
    {
        base.Throws_for_bad_point_as_GeoJson(json);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override void Throws_for_bad_sbyte_JSON_values(string json)
    {
        base.Throws_for_bad_sbyte_JSON_values(json);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => base.OnConfiguring(optionsBuilder.UseDuckDB(b => b.UseNetTopologySuite()));
}