#if NET11_0_OR_GREATER
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class TPCInheritanceTableSplittingQueryDuckDBTest(TPCInheritanceQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
    : TPCInheritanceTableSplittingQueryRelationalTestBase<TPCInheritanceQueryDuckDBFixture>(fixture, testOutputHelper);
#endif