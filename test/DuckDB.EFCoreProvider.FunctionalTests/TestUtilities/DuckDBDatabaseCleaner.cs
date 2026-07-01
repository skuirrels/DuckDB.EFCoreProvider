using DuckDB.EFCoreProvider.Design.Internal;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

public class DuckDBDatabaseCleaner : RelationalDatabaseCleaner
{
    protected override IDatabaseModelFactory CreateDatabaseModelFactory(ILoggerFactory loggerFactory)
    {
        var services = new ServiceCollection();
        services.AddEntityFrameworkDuckDB();

        new DuckDBDesignTimeServices().ConfigureDesignTimeServices(services);

        return services
            .BuildServiceProvider()
            .GetRequiredService<IDatabaseModelFactory>();
    }

    public override void Clean(DatabaseFacade facade)
    {
        base.Clean(facade);
        facade.EnsureCreated();
    }
}
