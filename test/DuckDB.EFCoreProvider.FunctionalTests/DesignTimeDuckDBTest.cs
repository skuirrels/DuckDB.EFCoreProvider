using DuckDB.EFCoreProvider.Design.Internal;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Reflection;

namespace Microsoft.EntityFrameworkCore;

public class DesignTimeDuckDBTest : DesignTimeTestBase<DesignTimeDuckDBTest.DesignTimeDuckDBFixture>
{
    public DesignTimeDuckDBTest(DesignTimeDuckDBFixture fixture) : base(fixture)
    {
    }

    protected override Assembly ProviderAssembly
        => typeof(DuckDBDesignTimeServices).Assembly;

    public class DesignTimeDuckDBFixture : DesignTimeFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => DuckDBTestStoreFactory.Instance;
    }
}
