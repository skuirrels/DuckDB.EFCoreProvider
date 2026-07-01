using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class OptionalDependentQueryDuckDBTest: OptionalDependentQueryTestBase<OptionalDependentQueryDuckDBFixture>
{
    public OptionalDependentQueryDuckDBTest(OptionalDependentQueryDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Basic_projection_entity_with_all_optional(bool async)
    {
        return base.Basic_projection_entity_with_all_optional(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Basic_projection_entity_with_some_required(bool async)
    {
        return base.Basic_projection_entity_with_some_required(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_nested_optional_dependent_with_all_optional_compared_to_not_null(bool async)
    {
        return base.Filter_nested_optional_dependent_with_all_optional_compared_to_not_null(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_nested_optional_dependent_with_all_optional_compared_to_null(bool async)
    {
        return base.Filter_nested_optional_dependent_with_all_optional_compared_to_null(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_nested_optional_dependent_with_some_required_compared_to_not_null(bool async)
    {
        return base.Filter_nested_optional_dependent_with_some_required_compared_to_not_null(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_nested_optional_dependent_with_some_required_compared_to_null(bool async)
    {
        return base.Filter_nested_optional_dependent_with_some_required_compared_to_null(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_optional_dependent_with_all_optional_compared_to_not_null(bool async)
    {
        return base.Filter_optional_dependent_with_all_optional_compared_to_not_null(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_optional_dependent_with_all_optional_compared_to_null(bool async)
    {
        return base.Filter_optional_dependent_with_all_optional_compared_to_null(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_optional_dependent_with_some_required_compared_to_not_null(bool async)
    {
        return base.Filter_optional_dependent_with_some_required_compared_to_not_null(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filter_optional_dependent_with_some_required_compared_to_null(bool async)
    {
        return base.Filter_optional_dependent_with_some_required_compared_to_null(async);
    }
}