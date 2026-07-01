using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.GraphUpdates;

public abstract class GraphUpdatesDuckDBTestBase<TFixture> : GraphUpdatesTestBase<TFixture>
    where TFixture : GraphUpdatesDuckDBTestBase<TFixture>.GraphUpdatesDuckDBFixtureBase, new()
{
    protected GraphUpdatesDuckDBTestBase(TFixture fixture) : base(fixture)
    {
    }

    protected override IQueryable<Root> ModifyQueryRoot(IQueryable<Root> query)
        => query.AsSplitQuery();

    protected override void UseTransaction(DatabaseFacade facade, IDbContextTransaction transaction)
        => facade.UseTransaction(transaction.GetDbTransaction());

    public abstract class GraphUpdatesDuckDBFixtureBase : GraphUpdatesFixtureBase
    {
        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public override bool AutoDetectChanges
            => false;

        public override PoolableDbContext CreateContext()
        {
            var context = base.CreateContext();
            context.ChangeTracker.AutoDetectChangesEnabled = AutoDetectChanges;

            return context;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder.Entity<OwnerRoot>(b =>
            {
                b.OwnsMany(
                    e => e.OptionalChildren, b =>
                    {
                        b.HasKey("Id");
                        b.OwnsMany(
                            e => e.Children, b =>
                            {
                                b.HasKey("Id");
                            });
                    });
                b.OwnsMany(
                    e => e.RequiredChildren, b =>
                    {
                        b.HasKey("Id");
                        b.OwnsMany(
                            e => e.Children, b =>
                            {
                                b.HasKey("Id");
                            });
                    });
            });

            modelBuilder.Entity<AccessState>(b =>
            {
                b.Property(e => e.AccessStateId).ValueGeneratedNever();
                b.HasData(new AccessState { AccessStateId = 1 });
            });

            modelBuilder.Entity<Cruiser>(b =>
            {
                b.Property(e => e.IdUserState).HasDefaultValue(1);
                b.HasOne(e => e.UserState).WithMany(e => e.Users).HasForeignKey(e => e.IdUserState);
            });

            modelBuilder.Entity<AccessStateWithSentinel>(b =>
            {
                b.Property(e => e.AccessStateWithSentinelId).ValueGeneratedNever();
                b.HasData(new AccessStateWithSentinel { AccessStateWithSentinelId = 1 });
            });

            modelBuilder.Entity<CruiserWithSentinel>(b =>
            {
                b.Property(e => e.IdUserState).HasDefaultValue(1).HasSentinel(667);
                b.HasOne(e => e.UserState).WithMany(e => e.Users).HasForeignKey(e => e.IdUserState);
            });

            modelBuilder.Entity<SomethingOfCategoryA>().Property<int>("CategoryId").HasDefaultValue(1);
            modelBuilder.Entity<SomethingOfCategoryB>().Property(e => e.CategoryId).HasDefaultValue(2);

            modelBuilder.Entity<CompositeKeyWith<int>>(b =>
            {
                b.Property(e => e.PrimaryGroup).HasDefaultValue(1).HasSentinel(1);
            });

            modelBuilder.Entity<CompositeKeyWith<bool>>(b =>
            {
                b.Property(e => e.PrimaryGroup).HasDefaultValue(true);
            });

            modelBuilder.Entity<CompositeKeyWith<bool?>>(b =>
            {
                b.Property(e => e.PrimaryGroup).HasDefaultValue(true);
            });
        }
    }
}