using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Update;

public class StoreValueGenerationDuckDBTest : StoreValueGenerationTestBase<StoreValueGenerationDuckDBFixture>
{
    public StoreValueGenerationDuckDBTest(StoreValueGenerationDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_Add_with_different_entity_types_and_all_generated_values(bool async)
    {
        return base.Add_Add_with_different_entity_types_and_all_generated_values(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_Add_with_different_entity_types_and_generated_values(bool async)
    {
        return base.Add_Add_with_different_entity_types_and_generated_values(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_Add_with_different_entity_types_and_no_generated_values(bool async)
    {
        return base.Add_Add_with_different_entity_types_and_no_generated_values(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_Add_with_same_entity_type_and_all_generated_values(bool async)
    {
        return base.Add_Add_with_same_entity_type_and_all_generated_values(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_Add_with_same_entity_type_and_generated_values(bool async)
    {
        return base.Add_Add_with_same_entity_type_and_generated_values(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Add_Add_with_same_entity_type_and_no_generated_values(bool async)
    {
        return base.Add_Add_with_same_entity_type_and_no_generated_values(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete(bool async)
    {
        return base.Delete(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_Add_with_same_entity_types(bool async)
    {
        return base.Delete_Add_with_same_entity_types(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_Delete_with_different_entity_types(bool async)
    {
        return base.Delete_Delete_with_different_entity_types(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Delete_Delete_with_same_entity_type(bool async)
    {
        return base.Delete_Delete_with_same_entity_type(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Modify_Modify_with_different_entity_types_and_generated_values(bool async)
    {
        return base.Modify_Modify_with_different_entity_types_and_generated_values(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Modify_Modify_with_different_entity_types_and_no_generated_values(bool async)
    {
        return base.Modify_Modify_with_different_entity_types_and_no_generated_values(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Modify_Modify_with_same_entity_type_and_generated_values(bool async)
    {
        return base.Modify_Modify_with_same_entity_type_and_generated_values(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Modify_Modify_with_same_entity_type_and_no_generated_values(bool async)
    {
        return base.Modify_Modify_with_same_entity_type_and_no_generated_values(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Modify_with_generated_values(bool async)
    {
        return base.Modify_with_generated_values(async);
    }
}
