using DuckDB.EFCoreProvider.Infrastructure;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class NonSharedPrimitiveCollectionsQueryDuckDBTest : NonSharedPrimitiveCollectionsQueryRelationalTestBase
{
    public NonSharedPrimitiveCollectionsQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Multidimensional_array_is_not_supported()
    {
        return base.Multidimensional_array_is_not_supported();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Column_collection_inside_json_owned_entity()
    {
        return base.Column_collection_inside_json_owned_entity();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Parameter_collection_Contains_with_default_mode_EF_Parameter(ParameterTranslationMode mode)
    {
        return base.Parameter_collection_Contains_with_default_mode_EF_Parameter(mode);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Parameter_collection_Count_with_column_predicate_with_default_mode(ParameterTranslationMode mode)
    {
        return base.Parameter_collection_Count_with_column_predicate_with_default_mode(mode);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Parameter_collection_Count_with_column_predicate_with_default_mode_EF_Parameter(ParameterTranslationMode mode)
    {
        return base.Parameter_collection_Count_with_column_predicate_with_default_mode_EF_Parameter(mode);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Array_of_byte_array()
    {
        return base.Array_of_byte_array();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Subquery_over_primitive_collection_on_inheritance_derived_type()
    {
        return base.Subquery_over_primitive_collection_on_inheritance_derived_type();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Parameter_collection_Contains_with_default_mode(ParameterTranslationMode mode)
    {
        return base.Parameter_collection_Contains_with_default_mode(mode);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Parameter_collection_of_enum_Cast_from_different_enum_type(ParameterTranslationMode mode)
    {
        return base.Parameter_collection_of_enum_Cast_from_different_enum_type(mode);
    }

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;

    protected override DbContextOptionsBuilder SetParameterizedCollectionMode(DbContextOptionsBuilder optionsBuilder,
        ParameterTranslationMode parameterizedCollectionMode)
    {
        new DuckDBDbContextOptionsBuilder(optionsBuilder).UseParameterizedCollectionMode(parameterizedCollectionMode);

        return optionsBuilder;
    }
}
