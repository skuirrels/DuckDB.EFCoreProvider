using Xunit;

namespace Microsoft.EntityFrameworkCore.Update;

public class JsonUpdateDuckDBTest : JsonUpdateTestBase<JsonUpdateDuckDBFixture>
{
    public JsonUpdateDuckDBTest(JsonUpdateDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_and_update_nested_optional_owned_collection_to_JSON(bool? value)
    {
        return base.Add_and_update_nested_optional_owned_collection_to_JSON(value);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_and_update_nested_optional_primitive_collection(bool? value)
    {
        return base.Add_and_update_nested_optional_primitive_collection(value);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_and_update_top_level_optional_owned_collection_to_JSON(bool? value)
    {
        return base.Add_and_update_top_level_optional_owned_collection_to_JSON(value);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_element_to_json_collection_branch()
    {
        return base.Add_element_to_json_collection_branch();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_element_to_json_collection_leaf()
    {
        return base.Add_element_to_json_collection_leaf();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_element_to_json_collection_on_derived()
    {
        return base.Add_element_to_json_collection_on_derived();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_element_to_json_collection_root()
    {
        return base.Add_element_to_json_collection_root();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_element_to_json_collection_root_null_navigations()
    {
        return base.Add_element_to_json_collection_root_null_navigations();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_entity_with_json()
    {
        return base.Add_entity_with_json();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_entity_with_json_null_navigations()
    {
        return base.Add_entity_with_json_null_navigations();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_json_reference_leaf()
    {
        return base.Add_json_reference_leaf();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_json_reference_root()
    {
        return base.Add_json_reference_root();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_entity_with_json()
    {
        return base.Delete_entity_with_json();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_json_collection_branch()
    {
        return base.Delete_json_collection_branch();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_json_collection_root()
    {
        return base.Delete_json_collection_root();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_json_reference_leaf()
    {
        return base.Delete_json_reference_leaf();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_json_reference_root()
    {
        return base.Delete_json_reference_root();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_a_scalar_property_and_another_property_behind_reference_navigation_on_the_same_entity()
    {
        return base.Edit_a_scalar_property_and_another_property_behind_reference_navigation_on_the_same_entity();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_a_scalar_property_and_collection_navigation_on_the_same_entity()
    {
        return base.Edit_a_scalar_property_and_collection_navigation_on_the_same_entity();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_element_in_json_collection_root1()
    {
        return base.Edit_element_in_json_collection_root1();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_element_in_json_collection_root2()
    {
        return base.Edit_element_in_json_collection_root2();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_element_in_json_collection_branch()
    {
        return base.Edit_element_in_json_collection_branch();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_element_in_json_multiple_levels_partial_update()
    {
        return base.Edit_element_in_json_multiple_levels_partial_update();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_element_in_json_branch_collection_and_add_element_to_the_same_collection()
    {
        return base.Edit_element_in_json_branch_collection_and_add_element_to_the_same_collection();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_two_elements_in_the_same_json_collection()
    {
        return base.Edit_two_elements_in_the_same_json_collection();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_two_elements_in_the_same_json_collection_at_the_root()
    {
        return base.Edit_two_elements_in_the_same_json_collection_at_the_root();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_collection_element_and_reference_at_once()
    {
        return base.Edit_collection_element_and_reference_at_once();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_enum_property()
    {
        return base.Edit_single_enum_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_numeric_property()
    {
        return base.Edit_single_numeric_property();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_bool()
    {
        return base.Edit_single_property_bool();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_byte()
    {
        return base.Edit_single_property_byte();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_char()
    {
        return base.Edit_single_property_char();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_datetime()
    {
        return base.Edit_single_property_datetime();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_datetimeoffset()
    {
        return base.Edit_single_property_datetimeoffset();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_decimal()
    {
        return base.Edit_single_property_decimal();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_double()
    {
        return base.Edit_single_property_double();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_guid()
    {
        return base.Edit_single_property_guid();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_int16()
    {
        return base.Edit_single_property_int16();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_int32()
    {
        return base.Edit_single_property_int32();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_int64()
    {
        return base.Edit_single_property_int64();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_signed_byte()
    {
        return base.Edit_single_property_signed_byte();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_single()
    {
        return base.Edit_single_property_single();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_timespan()
    {
        return base.Edit_single_property_timespan();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_dateonly()
    {
        return base.Edit_single_property_dateonly();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_timeonly()
    {
        return base.Edit_single_property_timeonly();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_uint16()
    {
        return base.Edit_single_property_uint16();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_uint32()
    {
        return base.Edit_single_property_uint32();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_uint64()
    {
        return base.Edit_single_property_uint64();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_nullable_int32()
    {
        return base.Edit_single_property_nullable_int32();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_nullable_int32_set_to_null()
    {
        return base.Edit_single_property_nullable_int32_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_enum()
    {
        return base.Edit_single_property_enum();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_enum_with_int_converter()
    {
        return base.Edit_single_property_enum_with_int_converter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_nullable_enum()
    {
        return base.Edit_single_property_nullable_enum();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_nullable_enum_set_to_null()
    {
        return base.Edit_single_property_nullable_enum_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_nullable_enum_with_int_converter()
    {
        return base.Edit_single_property_nullable_enum_with_int_converter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_nullable_enum_with_int_converter_set_to_null()
    {
        return base.Edit_single_property_nullable_enum_with_int_converter_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_nullable_enum_with_converter_that_handles_nulls()
    {
        return base.Edit_single_property_nullable_enum_with_converter_that_handles_nulls();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_nullable_enum_with_converter_that_handles_nulls_set_to_null()
    {
        return base.Edit_single_property_nullable_enum_with_converter_that_handles_nulls_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_two_properties_on_same_entity_updates_the_entire_entity()
    {
        return base.Edit_two_properties_on_same_entity_updates_the_entire_entity();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_a_scalar_property_and_reference_navigation_on_the_same_entity()
    {
        return base.Edit_a_scalar_property_and_reference_navigation_on_the_same_entity();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_with_converter_bool_to_int_zero_one()
    {
        return base.Edit_single_property_with_converter_bool_to_int_zero_one();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_with_converter_bool_to_string_True_False()
    {
        return base.Edit_single_property_with_converter_bool_to_string_True_False();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_with_converter_bool_to_string_Y_N()
    {
        return base.Edit_single_property_with_converter_bool_to_string_Y_N();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_with_converter_int_zero_one_to_bool()
    {
        return base.Edit_single_property_with_converter_int_zero_one_to_bool();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_with_converter_string_True_False_to_bool()
    {
        return base.Edit_single_property_with_converter_string_True_False_to_bool();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_with_converter_string_Y_N_to_bool()
    {
        return base.Edit_single_property_with_converter_string_Y_N_to_bool();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_numeric()
    {
        return base.Edit_single_property_collection_of_numeric();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_string()
    {
        return base.Edit_single_property_collection_of_string();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_bool()
    {
        return base.Edit_single_property_collection_of_bool();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_byte()
    {
        return base.Edit_single_property_collection_of_byte();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_char()
    {
        return base.Edit_single_property_collection_of_char();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_datetime()
    {
        return base.Edit_single_property_collection_of_datetime();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_datetimeoffset()
    {
        return base.Edit_single_property_collection_of_datetimeoffset();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_decimal()
    {
        return base.Edit_single_property_collection_of_decimal();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_double()
    {
        return base.Edit_single_property_collection_of_double();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_guid()
    {
        return base.Edit_single_property_collection_of_guid();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_int16()
    {
        return base.Edit_single_property_collection_of_int16();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_int32()
    {
        return base.Edit_single_property_collection_of_int32();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_int64()
    {
        return base.Edit_single_property_collection_of_int64();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_signed_byte()
    {
        return base.Edit_single_property_collection_of_signed_byte();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_single()
    {
        return base.Edit_single_property_collection_of_single();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_timespan()
    {
        return base.Edit_single_property_collection_of_timespan();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_dateonly()
    {
        return base.Edit_single_property_collection_of_dateonly();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_timeonly()
    {
        return base.Edit_single_property_collection_of_timeonly();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_uint16()
    {
        return base.Edit_single_property_collection_of_uint16();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_uint32()
    {
        return base.Edit_single_property_collection_of_uint32();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_uint64()
    {
        return base.Edit_single_property_collection_of_uint64();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_nullable_int32()
    {
        return base.Edit_single_property_collection_of_nullable_int32();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_nullable_int32_set_to_null()
    {
        return base.Edit_single_property_collection_of_nullable_int32_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_enum()
    {
        return base.Edit_single_property_collection_of_enum();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_enum_with_int_converter()
    {
        return base.Edit_single_property_collection_of_enum_with_int_converter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_nullable_enum()
    {
        return base.Edit_single_property_collection_of_nullable_enum();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_nullable_enum_set_to_null()
    {
        return base.Edit_single_property_collection_of_nullable_enum_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_nullable_enum_with_int_converter()
    {
        return base.Edit_single_property_collection_of_nullable_enum_with_int_converter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_nullable_enum_with_int_converter_set_to_null()
    {
        return base.Edit_single_property_collection_of_nullable_enum_with_int_converter_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_nullable_enum_with_converter_that_handles_nulls()
    {
        return base.Edit_single_property_collection_of_nullable_enum_with_converter_that_handles_nulls();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_nullable_enum_with_converter_that_handles_nulls_set_to_null()
    {
        return base.Edit_single_property_collection_of_nullable_enum_with_converter_that_handles_nulls_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_bool()
    {
        return base.Edit_single_property_relational_collection_of_bool();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_byte()
    {
        return base.Edit_single_property_relational_collection_of_byte();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_char()
    {
        return base.Edit_single_property_relational_collection_of_char();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_datetime()
    {
        return base.Edit_single_property_relational_collection_of_datetime();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_datetimeoffset()
    {
        return base.Edit_single_property_relational_collection_of_datetimeoffset();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_decimal()
    {
        return base.Edit_single_property_relational_collection_of_decimal();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_double()
    {
        return base.Edit_single_property_relational_collection_of_double();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_guid()
    {
        return base.Edit_single_property_relational_collection_of_guid();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_int16()
    {
        return base.Edit_single_property_relational_collection_of_int16();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_int32()
    {
        return base.Edit_single_property_relational_collection_of_int32();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_int64()
    {
        return base.Edit_single_property_relational_collection_of_int64();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_signed_byte()
    {
        return base.Edit_single_property_relational_collection_of_signed_byte();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_single()
    {
        return base.Edit_single_property_relational_collection_of_single();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_timespan()
    {
        return base.Edit_single_property_relational_collection_of_timespan();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_uint16()
    {
        return base.Edit_single_property_relational_collection_of_uint16();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_uint32()
    {
        return base.Edit_single_property_relational_collection_of_uint32();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_uint64()
    {
        return base.Edit_single_property_relational_collection_of_uint64();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_nullable_int32()
    {
        return base.Edit_single_property_relational_collection_of_nullable_int32();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_nullable_int32_set_to_null()
    {
        return base.Edit_single_property_relational_collection_of_nullable_int32_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_enum()
    {
        return base.Edit_single_property_relational_collection_of_enum();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_enum_with_int_converter()
    {
        return base.Edit_single_property_relational_collection_of_enum_with_int_converter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_nullable_enum()
    {
        return base.Edit_single_property_relational_collection_of_nullable_enum();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_nullable_enum_set_to_null()
    {
        return base.Edit_single_property_relational_collection_of_nullable_enum_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_nullable_enum_with_int_converter()
    {
        return base.Edit_single_property_relational_collection_of_nullable_enum_with_int_converter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_nullable_enum_with_int_converter_set_to_null()
    {
        return base.Edit_single_property_relational_collection_of_nullable_enum_with_int_converter_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_nullable_enum_with_converter_that_handles_nulls()
    {
        return base.Edit_single_property_relational_collection_of_nullable_enum_with_converter_that_handles_nulls();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_relational_collection_of_nullable_enum_with_converter_that_handles_nulls_set_to_null()
    {
        return base.Edit_single_property_relational_collection_of_nullable_enum_with_converter_that_handles_nulls_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_collection_of_bool()
    {
        return base.Edit_single_property_collection_of_collection_of_bool();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_collection_of_char()
    {
        return base.Edit_single_property_collection_of_collection_of_char();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_collection_of_double()
    {
        return base.Edit_single_property_collection_of_collection_of_double();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_collection_of_int16()
    {
        return base.Edit_single_property_collection_of_collection_of_int16();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_collection_of_int32()
    {
        return base.Edit_single_property_collection_of_collection_of_int32();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_collection_of_int64()
    {
        return base.Edit_single_property_collection_of_collection_of_int64();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_collection_of_single()
    {
        return base.Edit_single_property_collection_of_collection_of_single();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_collection_of_nullable_int32()
    {
        return base.Edit_single_property_collection_of_collection_of_nullable_int32();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_collection_of_nullable_int32_set_to_null()
    {
        return base.Edit_single_property_collection_of_collection_of_nullable_int32_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_collection_of_nullable_enum_set_to_null()
    {
        return base.Edit_single_property_collection_of_collection_of_nullable_enum_set_to_null();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_collection_of_collection_of_nullable_enum_with_int_converter()
    {
        return base.Edit_single_property_collection_of_collection_of_nullable_enum_with_int_converter();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Edit_single_property_with_non_ascii_characters()
    {
        return base.Edit_single_property_with_non_ascii_characters();
    }

    protected override void ClearLog()
    {
        Fixture.TestSqlLoggerFactory.Clear();
    }
}
