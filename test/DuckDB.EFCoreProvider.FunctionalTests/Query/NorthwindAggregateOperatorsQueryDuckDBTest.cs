using AwesomeAssertions;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query;

public class NorthwindAggregateOperatorsQueryDuckDBTest : NorthwindAggregateOperatorsQueryRelationalTestBase<
    NorthwindQueryDuckDBFixture<NoopModelCustomizer>>
{
    public NorthwindAggregateOperatorsQueryDuckDBTest(NorthwindQueryDuckDBFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        var asserters = (Dictionary<Type, object>)Fixture.EntityAsserters;

        static void Comparer(decimal expected, decimal actual)
        {
            actual.Should().BeApproximately(expected, 0.001m);
        }

        asserters.TryAdd(typeof(decimal), (Action<decimal, decimal>)Comparer);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_with_local_object_enumerable_closure(bool async)
    {
        return base.Contains_with_local_object_enumerable_closure(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_with_local_object_list_closure(bool async)
    {
        return base.Contains_with_local_object_list_closure(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_with_local_object_ordered_enumerable_closure(bool async)
    {
        return base.Contains_with_local_object_ordered_enumerable_closure(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_with_local_object_read_only_collection_closure(bool async)
    {
        return base.Contains_with_local_object_read_only_collection_closure(async);
    }

    [Theory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_over_keyless_entity_throws(bool async)
    {
        return base.Contains_over_keyless_entity_throws(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Average_over_max_subquery(bool async)
    {
        return base.Average_over_max_subquery(async);
    }

    [Theory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Max_over_nested_subquery(bool async)
    {
        return base.Max_over_nested_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Average_with_division_on_decimal(bool async)
    {
        return base.Average_with_division_on_decimal(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Average_with_division_on_decimal_no_significant_digits(bool async)
    {
        return base.Average_with_division_on_decimal_no_significant_digits(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_over_entityType_with_null_should_rewrite_to_false(bool async)
    {
        return base.Contains_over_entityType_with_null_should_rewrite_to_false(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_over_non_nullable_scalar_with_null_in_subquery_simplifies_to_false(bool async)
    {
        return base.Contains_over_non_nullable_scalar_with_null_in_subquery_simplifies_to_false(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_with_local_anonymous_type_array_closure(bool async)
    {
        return base.Contains_with_local_anonymous_type_array_closure(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Contains_with_local_tuple_array_closure(bool async)
    {
        return base.Contains_with_local_tuple_array_closure(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Multiple_collection_navigation_with_FirstOrDefault_chained(bool async)
    {
        return base.Multiple_collection_navigation_with_FirstOrDefault_chained(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Sum_over_Any_subquery(bool async)
    {
        return base.Sum_over_Any_subquery(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Sum_over_scalar_returning_subquery(bool async)
    {
        return base.Sum_over_scalar_returning_subquery(async);
    }
}
