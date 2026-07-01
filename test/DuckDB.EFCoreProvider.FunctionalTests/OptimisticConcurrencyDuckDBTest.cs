using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class OptimisticConcurrencyULongDuckDBTest(F1ULongDuckDBFixture fixture)
    : OptimisticConcurrencyDuckDBTestBase<F1ULongDuckDBFixture, ulong?>(fixture)
{
    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Property_entry_original_value_is_set()
    {
        base.Property_entry_original_value_is_set();
    }
}

public class OptimisticConcurrencyDuckDBTest(F1DuckDBFixture fixture)
    : OptimisticConcurrencyDuckDBTestBase<F1DuckDBFixture, byte[]>(fixture)
{
    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Property_entry_original_value_is_set()
    {
        base.Property_entry_original_value_is_set();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Attempting_to_delete_same_relationship_twice_for_many_to_many_results_in_independent_association_exception()
    {
        await base.Attempting_to_delete_same_relationship_twice_for_many_to_many_results_in_independent_association_exception();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_GetDatabaseValues_on_owned_entity_works(bool async)
    {
        await base.Calling_GetDatabaseValues_on_owned_entity_works(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_Reload_on_a_Deleted_entity_makes_the_entity_unchanged(bool async)
    {
        await base.Calling_Reload_on_a_Deleted_entity_makes_the_entity_unchanged(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_Reload_on_a_Detached_entity_makes_the_entity_unchanged(bool async)
    {
        await base.Calling_Reload_on_a_Detached_entity_makes_the_entity_unchanged(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_Reload_on_a_Modified_entity_makes_the_entity_unchanged(bool async)
    {
        await base.Calling_Reload_on_a_Modified_entity_makes_the_entity_unchanged(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_Reload_on_an_Added_entity_that_was_saved_elsewhere_makes_the_entity_unchanged(bool async)
    {
        await base.Calling_Reload_on_an_Added_entity_that_was_saved_elsewhere_makes_the_entity_unchanged(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_Reload_on_an_Unchanged_entity_makes_the_entity_unchanged(bool async)
    {
        await base.Calling_Reload_on_an_Unchanged_entity_makes_the_entity_unchanged(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_Reload_on_owned_entity_works(bool async)
    {
        await base.Calling_Reload_on_owned_entity_works(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Concurrency_issue_where_the_FK_is_the_concurrency_token_can_be_handled()
    {
        await base.Concurrency_issue_where_the_FK_is_the_concurrency_token_can_be_handled();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Deleting_the_same_entity_twice_results_in_DbUpdateConcurrencyException()
    {
        await base.Deleting_the_same_entity_twice_results_in_DbUpdateConcurrencyException();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Deleting_then_updating_the_same_entity_results_in_DbUpdateConcurrencyException()
    {
        await base.Deleting_then_updating_the_same_entity_results_in_DbUpdateConcurrencyException();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Deleting_then_updating_the_same_entity_results_in_DbUpdateConcurrencyException_which_can_be_resolved_with_store_values()
    {
        await base.Deleting_then_updating_the_same_entity_results_in_DbUpdateConcurrencyException_which_can_be_resolved_with_store_values();
    }
    
    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Nullable_client_side_concurrency_token_can_be_used()
    {
        base.Nullable_client_side_concurrency_token_can_be_used();
    }
}

public abstract class OptimisticConcurrencyDuckDBTestBase<TFixture, TRowVersion>(TFixture fixture)
    : OptimisticConcurrencyRelationalTestBase<TFixture, TRowVersion>(fixture)
    where TFixture : F1RelationalFixture<TRowVersion>, new()
{
    public override void Property_entry_original_value_is_set()
    {
        base.Property_entry_original_value_is_set();

        AssertSql(
            """
SELECT "e"."Id", "e"."EngineSupplierId", "e"."Name", "e"."StorageLocation_Latitude", "e"."StorageLocation_Longitude"
FROM "Engines" AS "e"
ORDER BY "e"."Id"
LIMIT 1
""",
            //
            """
@p1='1'
@p2='Mercedes' (Size = 8)
@p0='FO 108X' (Size = 7)
@p3='ChangedEngine' (Size = 13)
@p4='47.64491' (Nullable = true)
@p5='-122.128101' (Nullable = true)

UPDATE "Engines" SET "Name" = @p0
WHERE "Id" = @p1 AND "EngineSupplierId" = @p2 AND "Name" = @p3 AND "StorageLocation_Latitude" = @p4 AND "StorageLocation_Longitude" = @p5
RETURNING 1;
""");
    }

    [ConditionalFact(Skip = "Optimistic Offline Lock #2195")]
    public override Task Simple_concurrency_exception_can_be_resolved_with_store_values()
        => Task.FromResult(true);

    [ConditionalFact(Skip = "Optimistic Offline Lock #2195")]
    public override Task Simple_concurrency_exception_can_be_resolved_with_client_values()
        => Task.FromResult(true);

    [ConditionalFact(Skip = "Optimistic Offline Lock #2195")]
    public override Task Simple_concurrency_exception_can_be_resolved_with_new_values()
        => Task.FromResult(true);

    [ConditionalFact(Skip = "Optimistic Offline Lock #2195")]
    public override Task Simple_concurrency_exception_can_be_resolved_with_store_values_using_equivalent_of_accept_changes()
        => Task.FromResult(true);

    [ConditionalFact(Skip = "Optimistic Offline Lock #2195")]
    public override Task Simple_concurrency_exception_can_be_resolved_with_store_values_using_Reload()
        => Task.FromResult(true);

    [ConditionalFact(Skip = "Optimistic Offline Lock #2195")]
    public override Task Updating_then_deleting_the_same_entity_results_in_DbUpdateConcurrencyException()
        => Task.FromResult(true);

    [ConditionalFact(Skip = "Optimistic Offline Lock #2195")]
    public override Task
        Updating_then_deleting_the_same_entity_results_in_DbUpdateConcurrencyException_which_can_be_resolved_with_store_values()
        => Task.FromResult(true);

    [ConditionalFact(Skip = "Optimistic Offline Lock #2195")]
    public override Task
        Change_in_independent_association_after_change_in_different_concurrency_token_results_in_independent_association_exception()
        => Task.FromResult(true);

    [ConditionalFact(Skip = "Optimistic Offline Lock #2195")]
    public override Task Change_in_independent_association_results_in_independent_association_exception()
        => Task.FromResult(true);

    [ConditionalFact(Skip = "Optimistic Offline Lock #2195")]
    public override Task Two_concurrency_issues_in_one_to_many_related_entities_can_be_handled_by_dealing_with_dependent_first()
        => Task.FromResult(true);

    [ConditionalFact(Skip = "Optimistic Offline Lock #2195")]
    public override Task Two_concurrency_issues_in_one_to_one_related_entities_can_be_handled_by_dealing_with_dependent_first()
        => Task.FromResult(true);

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Attempting_to_delete_same_relationship_twice_for_many_to_many_results_in_independent_association_exception()
    {
        await base.Attempting_to_delete_same_relationship_twice_for_many_to_many_results_in_independent_association_exception();
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_GetDatabaseValues_on_owned_entity_works(bool async)
    {
        await base.Calling_GetDatabaseValues_on_owned_entity_works(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_Reload_on_a_Deleted_entity_makes_the_entity_unchanged(bool async)
    {
        await base.Calling_Reload_on_a_Deleted_entity_makes_the_entity_unchanged(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_Reload_on_a_Detached_entity_makes_the_entity_unchanged(bool async)
    {
        await base.Calling_Reload_on_a_Detached_entity_makes_the_entity_unchanged(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_Reload_on_a_Modified_entity_makes_the_entity_unchanged(bool async)
    {
        await base.Calling_Reload_on_a_Modified_entity_makes_the_entity_unchanged(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_Reload_on_an_Added_entity_that_was_saved_elsewhere_makes_the_entity_unchanged(bool async)
    {
        await base.Calling_Reload_on_an_Added_entity_that_was_saved_elsewhere_makes_the_entity_unchanged(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_Reload_on_an_Unchanged_entity_makes_the_entity_unchanged(bool async)
    {
        await base.Calling_Reload_on_an_Unchanged_entity_makes_the_entity_unchanged(async);
    }

    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Calling_Reload_on_owned_entity_works(bool async)
    {
        await base.Calling_Reload_on_owned_entity_works(async);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Concurrency_issue_where_the_FK_is_the_concurrency_token_can_be_handled()
    {
        await base.Concurrency_issue_where_the_FK_is_the_concurrency_token_can_be_handled();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Deleting_the_same_entity_twice_results_in_DbUpdateConcurrencyException()
    {
        await base.Deleting_the_same_entity_twice_results_in_DbUpdateConcurrencyException();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Deleting_then_updating_the_same_entity_results_in_DbUpdateConcurrencyException()
    {
        await base.Deleting_then_updating_the_same_entity_results_in_DbUpdateConcurrencyException();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Deleting_then_updating_the_same_entity_results_in_DbUpdateConcurrencyException_which_can_be_resolved_with_store_values()
    {
        await base.Deleting_then_updating_the_same_entity_results_in_DbUpdateConcurrencyException_which_can_be_resolved_with_store_values();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Nullable_client_side_concurrency_token_can_be_used()
    {
        base.Nullable_client_side_concurrency_token_can_be_used();
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());
}