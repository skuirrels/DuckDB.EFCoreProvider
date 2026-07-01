using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public class TwoDatabasesDuckDBTest : TwoDatabasesTestBase, IClassFixture<TwoDatabasesDuckDBTest.TwoDatabasesFixture>
{
    public TwoDatabasesDuckDBTest(TwoDatabasesFixture fixture) : base(fixture)
    {
    }

    [ConditionalFact(Skip = DuckDBSkipReasons.Tbd)]
    public override void Can_query_from_one_connection_string_and_save_changes_to_another()
    {
        base.Can_query_from_one_connection_string_and_save_changes_to_another();
    }

    protected new TwoDatabasesFixture Fixture
        => (TwoDatabasesFixture)base.Fixture;

    protected override DbContextOptionsBuilder CreateTestOptions(
        DbContextOptionsBuilder optionsBuilder,
        bool withConnectionString = false,
        bool withNullConnectionString = false)
        => withConnectionString
            ? withNullConnectionString
                ? optionsBuilder.UseDuckDB((string?)null)
                : optionsBuilder.UseDuckDB(DummyConnectionString)
            : optionsBuilder.UseDuckDB();

    protected override TwoDatabasesWithDataContext CreateBackingContext(string databaseName)
        => new(Fixture.CreateOptions(DuckDBTestStore.Create(databaseName)));

    protected override string DummyConnectionString
        => "DataSource=DummyDatabase";

    public class TwoDatabasesFixture : ServiceProviderFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}