using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindDbFunctionsQueryDuckDBTest : NorthwindDbFunctionsQueryRelationalTestBase<
    NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindDbFunctionsQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture) : base(fixture)
    {
    }

    public override async Task Like_literal(bool async)
    {
        await AssertCount(
            async,
            ss => ss.Set<Customer>(),
            ss => ss.Set<Customer>(),
            c => EF.Functions.Like(c.ContactName, "%M%"),
            c => c.ContactName.Contains("M"));
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Collate_case_sensitive(bool async)
    {
        return base.Collate_case_sensitive(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Collate_case_sensitive_constant(bool async)
    {
        return base.Collate_case_sensitive_constant(async);
    }

    protected override string CaseInsensitiveCollation => "NOCASE";

    protected override string CaseSensitiveCollation => string.Empty;
}
