using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

public class F1ULongDuckDBFixture : F1DuckDBFixtureBase<ulong?>
{
    protected override string StoreName
        => "F1ULongTest";
}

public class F1DuckDBFixture : F1DuckDBFixtureBase<byte[]>;

public abstract class F1DuckDBFixtureBase<TRowVersion> : F1RelationalFixture<TRowVersion>
{
    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;

    public override TestHelpers TestHelpers
        => DuckDBTestHelpers.Instance;
}
