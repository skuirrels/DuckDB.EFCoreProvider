using DuckDB.EFCoreProvider.Infrastructure;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Reflection;

namespace Microsoft.EntityFrameworkCore.Query;

public class AdHocQuerySplittingQueryDuckDBTest: AdHocQuerySplittingQueryTestBase
{
    private static readonly FieldInfo _querySplittingBehaviorFieldInfo =
        typeof(RelationalOptionsExtension).GetField("_querySplittingBehavior", BindingFlags.NonPublic | BindingFlags.Instance)!;
    
    public AdHocQuerySplittingQueryDuckDBTest(NonSharedFixture fixture) : base(fixture)
    {
    }

    protected override ITestStoreFactory TestStoreFactory
        => DuckDBTestStoreFactory.Instance;

    protected override DbContextOptionsBuilder SetQuerySplittingBehavior(DbContextOptionsBuilder optionsBuilder, QuerySplittingBehavior splittingBehavior)
    {
        new DuckDBDbContextOptionsBuilder(optionsBuilder).UseQuerySplittingBehavior(splittingBehavior);

        return optionsBuilder;
    }

    protected override DbContextOptionsBuilder ClearQuerySplittingBehavior(DbContextOptionsBuilder optionsBuilder)
    {
        var extension = optionsBuilder.Options.FindExtension<DuckDBOptionsExtension>();
        if (extension == null)
        {
            extension = new DuckDBOptionsExtension();
        }
        else
        {
            _querySplittingBehaviorFieldInfo.SetValue(extension, null);
        }

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);

        return optionsBuilder;
    }
}
