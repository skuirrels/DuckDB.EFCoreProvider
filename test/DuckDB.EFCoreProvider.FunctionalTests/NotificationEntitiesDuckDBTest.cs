using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

public class NotificationEntitiesDuckDBTest : NotificationEntitiesTestBase<NotificationEntitiesDuckDBTest.NotificationEntitiesDuckDBFixture>
{
    public NotificationEntitiesDuckDBTest(NotificationEntitiesDuckDBFixture fixture) : base(fixture)
    {
    }

    public class NotificationEntitiesDuckDBFixture : NotificationEntitiesFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
