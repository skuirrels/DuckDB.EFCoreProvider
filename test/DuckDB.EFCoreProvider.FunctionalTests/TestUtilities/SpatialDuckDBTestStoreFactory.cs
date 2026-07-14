using DuckDB.EFCoreProvider.NTS.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

public class SpatialDuckDBTestStoreFactory : RelationalTestStoreFactory
{
    public static SpatialDuckDBTestStoreFactory Instance { get; } = new();

    public override TestStore Create(string storeName)
        => DuckDBTestStore.Create(storeName).WithSpatialExtension();

    public override TestStore GetOrCreate(string storeName)
        => DuckDBTestStore.GetOrCreate(storeName).WithSpatialExtension();

    public override IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
        => serviceCollection
            .AddDuckDBTestStoreServices()
            .AddEntityFrameworkDuckDBNetTopologySuite();
}
