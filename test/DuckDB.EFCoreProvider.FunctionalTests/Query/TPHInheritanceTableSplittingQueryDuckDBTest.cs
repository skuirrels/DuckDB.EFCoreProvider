#if NET11_0_OR_GREATER
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query;

public class TPHInheritanceTableSplittingQueryDuckDBTest(TPHInheritanceQueryDuckDBFixture fixture, ITestOutputHelper testOutputHelper)
    : TPHInheritanceTableSplittingQueryRelationalTestBase<TPHInheritanceQueryDuckDBFixture>(fixture, testOutputHelper);
#endif