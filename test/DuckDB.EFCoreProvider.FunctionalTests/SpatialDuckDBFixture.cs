using DuckDB.EFCoreProvider.Infrastructure;
using DuckDB.EFCoreProvider.NTS.Extensions;
using Microsoft.EntityFrameworkCore.TestModels.SpatialModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore;

public class SpatialDuckDBFixture  : SpatialFixtureBase
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

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);

        // DuckDB only has a single GEOMETRY type — sub-types like POINTZ are not supported
        modelBuilder.Entity<PointEntity>().Property(e => e.PointZ).HasColumnType("GEOMETRY");
        modelBuilder.Entity<PointEntity>().Property(e => e.PointM).HasColumnType("GEOMETRY");
        modelBuilder.Entity<PointEntity>().Property(e => e.PointZM).HasColumnType("GEOMETRY");
    }
}