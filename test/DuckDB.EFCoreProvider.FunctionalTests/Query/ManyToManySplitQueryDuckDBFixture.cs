using DuckDB.EFCoreProvider.Extensions;

namespace Microsoft.EntityFrameworkCore.Query;

public class ManyToManySplitQueryDuckDBFixture : ManyToManyQueryDuckDBFixture
{
    protected override string StoreName
        => "ManyToManySplitQuery";

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
        => base.AddOptions(builder.UseDuckDB(b => b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
}
