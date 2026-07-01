using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Scaffolding;

public class DuckDBDatabaseModelFactoryTest : IClassFixture<DuckDBDatabaseModelFactoryTest.DuckDBDatabaseModelFixture>
{
    public DuckDBDatabaseModelFactoryTest(DuckDBDatabaseModelFixture fixture)
    {
        Fixture = fixture;
    }
    
    protected DuckDBDatabaseModelFixture Fixture { get; }
    
    public class DuckDBDatabaseModelFixture : SharedStoreFixtureBase<PoolableDbContext>
    {
        protected override string StoreName
            => nameof(DuckDBDatabaseModelFactoryTest);

        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;

        public new DuckDBTestStore TestStore
            => (DuckDBTestStore)base.TestStore;

        protected override bool ShouldLogCategory(string logCategory)
            => logCategory == DbLoggerCategory.Scaffolding.Name;
    }
}
