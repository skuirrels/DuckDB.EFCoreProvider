using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

// TODO
/*
 * Can_build_monster_model_and_seed_data_using_dependent_navigations
 * Can_build_monster_model_and_seed_data_using_navigations_with_deferred_add
 * Can_build_monster_model_and_seed_data_using_principal_navigations
 * Composite_fixup_happens_when_FKs_change_test
 */
public abstract class MonsterFixupChangedOnlyDuckDBTest : MonsterFixupTestBase<MonsterFixupChangedOnlyDuckDBTest.MonsterFixupChangedOnlyDuckDBFixture>
{
    protected MonsterFixupChangedOnlyDuckDBTest(MonsterFixupChangedOnlyDuckDBFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_build_monster_model_and_seed_data_using_FKs()
    {
        await base.Can_build_monster_model_and_seed_data_using_FKs();
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override async Task Can_build_monster_model_and_seed_data_using_all_navigations()
    {
        await base.Can_build_monster_model_and_seed_data_using_all_navigations();
    }

    public class MonsterFixupChangedOnlyDuckDBFixture : MonsterFixupChangedOnlyFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        protected override void OnModelCreating<TMessage, TProduct, TProductPhoto, TProductReview, TComputerDetail, TDimensions>(
            ModelBuilder builder)
        {
            base.OnModelCreating<TMessage, TProduct, TProductPhoto, TProductReview, TComputerDetail, TDimensions>(builder);

            builder.Entity<TMessage>().HasKey(e => e.MessageId);
            builder.Entity<TProductPhoto>().HasKey(e => e.PhotoId);
            builder.Entity<TProductReview>().HasKey(e => e.ReviewId);
        }
    }
}
