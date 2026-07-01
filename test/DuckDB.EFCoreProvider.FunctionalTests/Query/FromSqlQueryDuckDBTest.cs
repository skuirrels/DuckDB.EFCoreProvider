using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Data.Common;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class FromSqlQueryDuckDBTest : FromSqlQueryTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public FromSqlQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    protected override DbParameter CreateDbParameter(string name, object value)
    {
        return new DuckDBParameter
        {
            ParameterName = name.StartsWith('$') || name.StartsWith('@')
                ? name[1..]
                : name,
            Value = value
        };
    }

    [ConditionalTheory, MemberData(nameof(IsAsyncData))]
    public override async Task FromSqlRaw_in_subquery_with_dbParameter(bool async)
        => await AssertQuery(
            async,
            ss => ss.Set<Order>().Where(o => ((DbSet<Customer>)ss.Set<Customer>()).FromSqlRaw(
                    NormalizeDelimitersInRawString(@"SELECT * FROM Customers WHERE City = $city"),
                    // ReSharper disable once FormatStringProblem
                    CreateDbParameter("city", "London"))
                .Select(c => c.CustomerID)
                .Contains(o.CustomerID)),
            ss => ss.Set<Order>().Where(o => ss.Set<Customer>().Where(x => x.City == "London")
                .Select(c => c.CustomerID)
                .Contains(o.CustomerID)));

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Bad_data_error_handling_invalid_cast(bool async)
    {
        return base.Bad_data_error_handling_invalid_cast(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Bad_data_error_handling_invalid_cast_projection(bool async)
    {
        return base.Bad_data_error_handling_invalid_cast_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Bad_data_error_handling_null(bool async)
    {
        return base.Bad_data_error_handling_null(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Bad_data_error_handling_null_no_tracking(bool async)
    {
        return base.Bad_data_error_handling_null_no_tracking(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Bad_data_error_handling_null_projection(bool async)
    {
        return base.Bad_data_error_handling_null_projection(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromSqlRaw_in_subquery_with_positional_dbParameter_without_name(bool async)
    {
        return base.FromSqlRaw_in_subquery_with_positional_dbParameter_without_name(async);
    }

    public override async Task FromSqlRaw_queryable_composed_compiled_with_DbParameter(bool async)
    {
        if (async)
        {
            var query = EF.CompileAsyncQuery((NorthwindContext context) => context.Set<Customer>()
                .FromSqlRaw(
                    NormalizeDelimitersInRawString("SELECT * FROM Customers WHERE CustomerID = $customer"),
                    CreateDbParameter("customer", "CONSH"))
                .Where(c => c.ContactName.Contains("z")));

            using (var context = CreateContext())
            {
                var actual = await query(context).ToListAsync();

                Assert.Single(actual);
            }
        }
        else
        {
            var query = EF.CompileQuery((NorthwindContext context) => context.Set<Customer>()
                .FromSqlRaw(
                    NormalizeDelimitersInRawString("SELECT * FROM Customers WHERE CustomerID = $customer"),
                    CreateDbParameter("customer", "CONSH"))
                .Where(c => c.ContactName.Contains("z")));

            using (var context = CreateContext())
            {
                var actual = query(context).ToArray();

                Assert.Single(actual);
            }
        }
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromSqlRaw_queryable_composed_compiled_with_nameless_DbParameter(bool async)
    {
        return base.FromSqlRaw_queryable_composed_compiled_with_nameless_DbParameter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task FromSqlRaw_queryable_simple_projection_composed(bool async)
    {
        await base.FromSqlRaw_queryable_simple_projection_composed(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task<string?> FromSqlRaw_queryable_with_parameters_and_closure(bool async)
    {
        return base.FromSqlRaw_queryable_with_parameters_and_closure(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromSqlRaw_with_db_parameters_called_multiple_times(bool async)
    {
        return base.FromSqlRaw_with_db_parameters_called_multiple_times(async);
    }

    public override async Task FromSqlRaw_with_dbParameter(bool async)
    {
        var parameter = CreateDbParameter("city", "London");

        await AssertQuery(
            async,
            ss => ((DbSet<Customer>)ss.Set<Customer>()).FromSqlRaw(
                NormalizeDelimitersInRawString("SELECT * FROM Customers WHERE City = $city"), parameter),
            ss => ss.Set<Customer>().Where(x => x.City == "London"));
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromSqlRaw_with_dbParameter_and_regular_parameter_with_same_name(bool async)
    {
        return base.FromSqlRaw_with_dbParameter_and_regular_parameter_with_same_name(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromSqlRaw_with_dbParameter_mixed(bool async)
    {
        return base.FromSqlRaw_with_dbParameter_mixed(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromSqlRaw_with_dbParameter_mixed_in_subquery(bool async)
    {
        return base.FromSqlRaw_with_dbParameter_mixed_in_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task FromSqlRaw_with_dbParameter_without_name_prefix(bool async)
    {
        return base.FromSqlRaw_with_dbParameter_without_name_prefix(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_closed_connection_opened_by_it_when_buffering(bool async)
    {
        return base.Include_closed_connection_opened_by_it_when_buffering(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Include_does_not_close_user_opened_connection_for_empty_result(bool async)
    {
        return base.Include_does_not_close_user_opened_connection_for_empty_result(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Multiple_occurrences_of_FromSql_with_db_parameter_adds_two_parameters(bool async)
    {
        return base.Multiple_occurrences_of_FromSql_with_db_parameter_adds_two_parameters(async);
    }
}
