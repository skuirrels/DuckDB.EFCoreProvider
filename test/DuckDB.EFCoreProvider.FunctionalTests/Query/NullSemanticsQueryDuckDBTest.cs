using DuckDB.EFCoreProvider.Infrastructure;
using Microsoft.EntityFrameworkCore.TestModels.NullSemanticsModel;

namespace Microsoft.EntityFrameworkCore.Query;

public class NullSemanticsQueryDuckDBTest : NullSemanticsQueryTestBase<NullSemanticsQueryDuckDBFixture>
{
    public NullSemanticsQueryDuckDBTest(NullSemanticsQueryDuckDBFixture fixture) : base(fixture)
    {
    }

    protected override NullSemanticsContext CreateContext(bool useRelationalNulls = false)
    {
        var options = new DbContextOptionsBuilder(Fixture.CreateOptions());

        if (useRelationalNulls)
        {
            new DuckDBDbContextOptionsBuilder(options).UseRelationalNulls();
        }

        var context = new NullSemanticsContext(options.Options);

        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        return context;
    }
}
