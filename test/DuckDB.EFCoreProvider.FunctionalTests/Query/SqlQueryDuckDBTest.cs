using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Data.Common;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class SqlQueryDuckDBTest : SqlQueryTestBase<NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public SqlQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
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
    public override Task Multiple_occurrences_of_SqlQuery_with_db_parameter_adds_two_parameters(bool async)
    {
        return base.Multiple_occurrences_of_SqlQuery_with_db_parameter_adds_two_parameters(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQuery_queryable_multiple_composed_with_parameters_and_closure_parameters_interpolated(bool async)
    {
        return base.SqlQuery_queryable_multiple_composed_with_parameters_and_closure_parameters_interpolated(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_does_not_parameterize_interpolated_string(bool async)
    {
        return base.SqlQueryRaw_does_not_parameterize_interpolated_string(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_in_subquery_with_dbParameter(bool async)
    {
        return base.SqlQueryRaw_in_subquery_with_dbParameter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_in_subquery_with_positional_dbParameter_with_name(bool async)
    {
        return base.SqlQueryRaw_in_subquery_with_positional_dbParameter_with_name(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_in_subquery_with_positional_dbParameter_without_name(bool async)
    {
        return base.SqlQueryRaw_in_subquery_with_positional_dbParameter_without_name(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_queryable_composed_compiled_with_DbParameter(bool async)
    {
        return base.SqlQueryRaw_queryable_composed_compiled_with_DbParameter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_queryable_composed_compiled_with_nameless_DbParameter(bool async)
    {
        return base.SqlQueryRaw_queryable_composed_compiled_with_nameless_DbParameter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_queryable_multiple_composed(bool async)
    {
        return base.SqlQueryRaw_queryable_multiple_composed(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_queryable_multiple_composed_with_closure_parameters(bool async)
    {
        return base.SqlQueryRaw_queryable_multiple_composed_with_closure_parameters(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_queryable_multiple_composed_with_parameters_and_closure_parameters(bool async)
    {
        return base.SqlQueryRaw_queryable_multiple_composed_with_parameters_and_closure_parameters(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_queryable_simple_projection_composed(bool async)
    {
        return base.SqlQueryRaw_queryable_simple_projection_composed(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_queryable_with_null_parameter(bool async)
    {
        return base.SqlQueryRaw_queryable_with_null_parameter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task<string> SqlQueryRaw_queryable_with_parameters_and_closure(bool async)
    {
        return base.SqlQueryRaw_queryable_with_parameters_and_closure(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_with_db_parameters_called_multiple_times(bool async)
    {
        return base.SqlQueryRaw_with_db_parameters_called_multiple_times(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_with_dbParameter(bool async)
    {
        return base.SqlQueryRaw_with_dbParameter(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_with_dbParameter_mixed(bool async)
    {
        return base.SqlQueryRaw_with_dbParameter_mixed(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_with_dbParameter_mixed_in_subquery(bool async)
    {
        return base.SqlQueryRaw_with_dbParameter_mixed_in_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task SqlQueryRaw_with_dbParameter_without_name_prefix(bool async)
    {
        return base.SqlQueryRaw_with_dbParameter_without_name_prefix(async);
    }

    protected override DbParameter CreateDbParameter(string name, object value)
    {
        return new DuckDBParameter(
            name.StartsWith('$') || name.StartsWith('@')
                ? name[1..]
                : name,
            value);
    }
}