using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query;

public class QueryNoClientEvalDuckDBFixture : NorthwindQueryDuckDBFixture<NoopModelCustomizer>
{
}
