using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public abstract class FindDuckDBTest(FindDuckDBTest.FindDuckDBFixture fixture) : FindTestBase<FindDuckDBTest.FindDuckDBFixture>(fixture)
{
    [ConditionalTheory(Skip = DuckDBSkipReasons.Tbd)]
    public override Task Find_int_key_from_store_async(CancellationType cancellationType)
    {
        return base.Find_int_key_from_store_async(cancellationType);
    }

    public class FindDuckDBTestSet(FindDuckDBFixture fixture) : FindDuckDBTest(fixture)
    {
        protected override TestFinder Finder { get; } = new FindViaSetFinder();
    }

    public class FindDuckDBTestContext(FindDuckDBFixture fixture) : FindDuckDBTest(fixture)
    {
        protected override TestFinder Finder { get; } = new FindViaContextFinder();
    }

    public class FindDuckDBTestNonGeneric(FindDuckDBFixture fixture) : FindDuckDBTest(fixture)
    {
        protected override TestFinder Finder { get; } = new FindViaNonGenericContextFinder();
    }

    public class FindDuckDBFixture : FindFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder.Entity<IntKey>(b =>
            {
                // This configuration for SQLite prevents attempts to use the default composite key config, which doesn't work
                // on SQLite. See #26708
                b.OwnsOne(
                    e => e.OwnedReference, b =>
                    {
                        b.OwnsOne(e => e.NestedOwned);
                        b.OwnsMany(e => e.NestedOwnedCollection).ToTable("NestedOwnedCollection").HasKey(e => e.Prop);
                    });

                b.OwnsMany(
                    e => e.OwnedCollection, b =>
                    {
                        b.ToTable("OwnedCollection").HasKey(e => e.Prop);
                        b.OwnsOne(e => e.NestedOwned);
                        b.OwnsMany(e => e.NestedOwnedCollection).ToTable("OwnedNestedOwnedCollection").HasKey(e => e.Prop);
                    });
            });
        }
    }
}