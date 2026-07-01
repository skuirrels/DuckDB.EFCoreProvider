using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Migrations;

public class MigrationsInfrastructureDuckDBTest : MigrationsInfrastructureTestBase<MigrationsInfrastructureDuckDBTest.MigrationsInfrastructureDuckDBFixture>
{
    public MigrationsInfrastructureDuckDBTest(MigrationsInfrastructureDuckDBFixture fixture, ITestOutputHelper testOutputHelper) : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_diff_against_2_2_model()
    {
        throw new NotImplementedException();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_diff_against_3_0_ASP_NET_Identity_model()
    {
        throw new NotImplementedException();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_diff_against_2_2_ASP_NET_Identity_model()
    {
        throw new NotImplementedException();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_diff_against_2_1_ASP_NET_Identity_model()
    {
        throw new NotImplementedException();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_apply_all_migrations()
    {
        base.Can_apply_all_migrations();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_apply_all_migrations_async()
    {
        return base.Can_apply_all_migrations_async();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_apply_one_migration()
    {
        base.Can_apply_one_migration();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_apply_one_migration_in_parallel()
    {
        base.Can_apply_one_migration_in_parallel();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_apply_one_migration_in_parallel_async()
    {
        return base.Can_apply_one_migration_in_parallel_async();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_apply_range_of_migrations()
    {
        base.Can_apply_range_of_migrations();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_apply_second_migration_in_parallel()
    {
        base.Can_apply_second_migration_in_parallel();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_apply_second_migration_in_parallel_async()
    {
        return base.Can_apply_second_migration_in_parallel_async();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_apply_two_migrations_in_transaction()
    {
        base.Can_apply_two_migrations_in_transaction();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_apply_two_migrations_in_transaction_async()
    {
        return base.Can_apply_two_migrations_in_transaction_async();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_generate_idempotent_up_and_down_scripts()
    {
        return base.Can_generate_idempotent_up_and_down_scripts();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_generate_idempotent_up_and_down_scripts_noTransactions()
    {
        return base.Can_generate_idempotent_up_and_down_scripts_noTransactions();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_generate_migration_from_initial_database_to_initial()
    {
        return base.Can_generate_migration_from_initial_database_to_initial();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_generate_no_migration_script()
    {
        return base.Can_generate_no_migration_script();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_generate_one_up_and_down_script()
    {
        return base.Can_generate_one_up_and_down_script();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_generate_up_and_down_script_using_names()
    {
        return base.Can_generate_up_and_down_script_using_names();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_generate_up_and_down_scripts()
    {
        return base.Can_generate_up_and_down_scripts();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Can_generate_up_and_down_scripts_noTransactions()
    {
        return base.Can_generate_up_and_down_scripts_noTransactions();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_get_active_provider()
    {
        base.Can_get_active_provider();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_revert_all_migrations()
    {
        base.Can_revert_all_migrations();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_revert_one_migrations()
    {
        base.Can_revert_one_migrations();
    }

    protected override Task ExecuteSqlAsync(string value)
    {
        ((DuckDBTestStore)Fixture.TestStore).ExecuteNonQuery(value);
        return Task.CompletedTask;
    }

    public class MigrationsInfrastructureDuckDBFixture : MigrationsInfrastructureFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
