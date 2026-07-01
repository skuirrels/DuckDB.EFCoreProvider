using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore;

public class DuckDBServiceCollectionExtensionsTest : RelationalServiceCollectionExtensionsTestBase
{
    public DuckDBServiceCollectionExtensionsTest()
        : base(DuckDBTestHelpers.Instance)
    {
    }
}