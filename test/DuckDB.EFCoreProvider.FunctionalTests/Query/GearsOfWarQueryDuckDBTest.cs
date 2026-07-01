using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class GearsOfWarQueryDuckDBTest : GearsOfWarQueryRelationalTestBase<GearsOfWarQueryDuckDBFixture>
{
    public GearsOfWarQueryDuckDBTest(GearsOfWarQueryDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task DateTimeOffset_Contains_Less_than_Greater_than(bool async)
    {
        return base.DateTimeOffset_Contains_Less_than_Greater_than(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task DateTimeOffset_Date_returns_datetime(bool async)
    {
        return base.DateTimeOffset_Date_returns_datetime(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task DateTimeOffsetNow_minus_timespan(bool async)
    {
        return base.DateTimeOffsetNow_minus_timespan(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FirstOrDefault_on_empty_collection_of_DateTime_in_subquery(bool async)
    {
        return base.FirstOrDefault_on_empty_collection_of_DateTime_in_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Non_string_concat_uses_appropriate_type_mapping(bool async)
    {
        return base.Non_string_concat_uses_appropriate_type_mapping(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Outer_parameter_in_group_join_with_DefaultIfEmpty(bool async)
    {
        return base.Outer_parameter_in_group_join_with_DefaultIfEmpty(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Projecting_property_converted_to_nullable_into_member_access(bool async)
    {
        return base.Projecting_property_converted_to_nullable_into_member_access(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Select_null_propagation_negative4(bool async)
    {
        return base.Select_null_propagation_negative4(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Where_equals_method_on_nullable_with_object_overload(bool async)
    {
        return base.Where_equals_method_on_nullable_with_object_overload(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Where_subquery_with_ElementAt_using_column_as_index(bool async)
    {
        return base.Where_subquery_with_ElementAt_using_column_as_index(async);
    }
}
