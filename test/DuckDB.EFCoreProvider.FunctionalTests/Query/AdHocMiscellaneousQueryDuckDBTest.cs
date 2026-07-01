using DuckDB.EFCoreProvider.Infrastructure;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

// TODO Query_when_null_key_in_database_should_throw - is not virtual
public abstract class AdHocMiscellaneousQueryDuckDBTest : AdHocMiscellaneousQueryRelationalTestBase
{
    protected AdHocMiscellaneousQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    protected override ITestStoreFactory TestStoreFactory => DuckDBTestStoreFactory.Instance;

    protected override DbContextOptionsBuilder SetParameterizedCollectionMode(
        DbContextOptionsBuilder optionsBuilder,
        ParameterTranslationMode parameterizedCollectionMode)
    {
        new DuckDBDbContextOptionsBuilder(optionsBuilder).UseParameterizedCollectionMode(parameterizedCollectionMode);

        return optionsBuilder;
    }

    protected override async Task Seed2951(Context2951 context)
    {
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE ZeroKey (Id int);
            INSERT INTO ZeroKey VALUES (NULL)
            """);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Check_inlined_constants_redacting(bool async, bool enableSensitiveDataLogging)
    {
        return base.Check_inlined_constants_redacting(async, enableSensitiveDataLogging);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Aggregate_over_subquery_in_group_by_projection(bool async)
    {
        return base.Aggregate_over_subquery_in_group_by_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Aggregate_over_subquery_in_group_by_projection_2(bool async)
    {
        return base.Aggregate_over_subquery_in_group_by_projection_2(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Average_with_cast()
    {
        return base.Average_with_cast();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Bool_discriminator_column_works(bool async)
    {
        return base.Bool_discriminator_column_works(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Conditional_expression_with_conditions_does_not_collapse_if_nullable_bool()
    {
        return base.Conditional_expression_with_conditions_does_not_collapse_if_nullable_bool();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Discriminator_type_is_handled_correctly()
    {
        return base.Discriminator_type_is_handled_correctly();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Enum_has_flag_applies_explicit_cast_for_constant()
    {
        return base.Enum_has_flag_applies_explicit_cast_for_constant();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Enum_has_flag_does_not_apply_explicit_cast_for_non_constant()
    {
        return base.Enum_has_flag_does_not_apply_explicit_cast_for_non_constant();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task First_FirstOrDefault_ix_async()
    {
        return base.First_FirstOrDefault_ix_async();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task GroupBy_Aggregate_over_navigations_repeated(bool async)
    {
        return base.GroupBy_Aggregate_over_navigations_repeated(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task New_instances_in_projection_are_not_shared_across_results()
    {
        return base.New_instances_in_projection_are_not_shared_across_results();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Operators_combine_nullability_of_entity_shapers()
    {
        return base.Operators_combine_nullability_of_entity_shapers();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Parameterless_ctor_on_inner_DTO_gets_called_for_every_row()
    {
        return base.Parameterless_ctor_on_inner_DTO_gets_called_for_every_row();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task QueryBuffer_requirement_is_computed_when_querying_base_type_while_derived_type_has_shadow_prop()
    {
        return base.QueryBuffer_requirement_is_computed_when_querying_base_type_while_derived_type_has_shadow_prop();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Repeated_parameters_in_generated_query_sql()
    {
        return base.Repeated_parameters_in_generated_query_sql();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_where_Select(bool async)
    {
        return base.SelectMany_where_Select(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Shadow_property_with_inheritance()
    {
        return base.Shadow_property_with_inheritance();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Subquery_first_member_compared_to_null(bool async)
    {
        return base.Subquery_first_member_compared_to_null(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Union_and_insert_works_correctly_together()
    {
        return base.Union_and_insert_works_correctly_together();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Unwrap_convert_node_over_projection_when_translating_contains_over_subquery(bool async)
    {
        return base.Unwrap_convert_node_over_projection_when_translating_contains_over_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Unwrap_convert_node_over_projection_when_translating_contains_over_subquery_2(bool async)
    {
        return base.Unwrap_convert_node_over_projection_when_translating_contains_over_subquery_2(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Unwrap_convert_node_over_projection_when_translating_contains_over_subquery_3(bool async)
    {
        return base.Unwrap_convert_node_over_projection_when_translating_contains_over_subquery_3(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Mapping_JsonElement_property_throws_a_meaningful_exception()
    {
        return base.Mapping_JsonElement_property_throws_a_meaningful_exception();
    }
}
