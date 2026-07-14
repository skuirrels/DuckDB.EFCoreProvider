using DuckDB.EFCoreProvider.NTS.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

public class DuckDBTestStoreFactory : RelationalTestStoreFactory
{
    public static DuckDBTestStoreFactory Instance { get; } = new();
    
    public override TestStore Create(string storeName)
    {
        return DuckDBTestStore.Create(storeName);
    }

    public override TestStore GetOrCreate(string storeName)
    {
        return DuckDBTestStore.GetOrCreate(storeName);
    }

    public override IServiceCollection AddProviderServices(IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddDuckDBTestStoreServices()
            .AddEntityFrameworkDuckDBNetTopologySuite();
    }
}
