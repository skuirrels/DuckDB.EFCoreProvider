using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class ComplexNavigationsCollectionsSplitQueryDuckDBTest : ComplexNavigationsCollectionsSplitQueryRelationalTestBase<ComplexNavigationsQueryDuckDBFixture>
{
    public ComplexNavigationsCollectionsSplitQueryDuckDBTest(ComplexNavigationsQueryDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Filtered_include_outer_parameter_used_inside_filter(bool async)
    {
        return base.Filtered_include_outer_parameter_used_inside_filter(async);
    }
}
