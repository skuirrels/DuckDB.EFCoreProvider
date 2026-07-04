using DuckDB.EFCoreProvider.NTS.Scaffolding.Internal;
using DuckDB.EFCoreProvider.NTS.Storage.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DuckDB.EFCoreProvider.NTS.Design.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
public class DuckDBNetTopologySuiteDesignTimeServices : IDesignTimeServices
{
    public virtual void ConfigureDesignTimeServices(IServiceCollection serviceCollection)
    {
        serviceCollection
            .AddSingleton<IRelationalTypeMappingSourcePlugin, DuckDBNetTopologySuiteTypeMappingSourcePlugin>()
            .AddSingleton<IProviderCodeGeneratorPlugin, DuckDBNetTopologySuiteCodeGeneratorPlugin>();
    }
}