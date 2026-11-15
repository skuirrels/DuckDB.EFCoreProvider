#if NET11_0_OR_GREATER
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class TPTInheritanceTableSplittingQueryDuckDBTest(TPTInheritanceQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
    : TPTInheritanceTableSplittingQueryRelationalTestBase<TPTInheritanceQueryDuckDBFixture>(fixture, testOutputHelper);
#endif