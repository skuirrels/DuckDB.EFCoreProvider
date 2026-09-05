using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindJoinQueryDuckDBTest : NorthwindJoinQueryRelationalTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
#if NET11_0_OR_GREATER
    [ConditionalTheory]
    public override async Task Join_local_bytes_closure_is_cached_correctly(bool async)
    {
        // EF11's VALUES parameterization supports this shape for DuckDB; verify both parameter sets.
        byte[] ids = [1, 2];
        await AssertQueryScalar(async, source =>
            from employee in source.Set<Employee>()
            join id in ids on employee.EmployeeID equals id
            select employee.EmployeeID);

        ids = [3];
        await AssertQueryScalar(async, source =>
            from employee in source.Set<Employee>()
            join id in ids on employee.EmployeeID equals id
            select employee.EmployeeID);
    }
#endif

    public NorthwindJoinQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SelectMany_with_selecting_outer_entity_column_and_inner_column(bool async)
    {
        return base.SelectMany_with_selecting_outer_entity_column_and_inner_column(async);
    }
}
