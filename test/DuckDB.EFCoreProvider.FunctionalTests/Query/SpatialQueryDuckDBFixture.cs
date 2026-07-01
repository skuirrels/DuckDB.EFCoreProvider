using DuckDB.EFCoreProvider.Infrastructure;
using DuckDB.EFCoreProvider.NTS.Extensions;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore.Query;

public class SpatialQueryDuckDBFixture : SpatialQueryRelationalFixture
{
    protected override ITestStoreFactory TestStoreFactory
        => SpatialDuckDBTestStoreFactory.Instance;

    protected override IServiceCollection AddServices(IServiceCollection serviceCollection)
        => base.AddServices(serviceCollection)
            .AddEntityFrameworkDuckDBNetTopologySuite();

    public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
    {
        var optionsBuilder = base.AddOptions(builder);
        new DuckDBDbContextOptionsBuilder(optionsBuilder).UseNetTopologySuite();
        return optionsBuilder;
    }
}