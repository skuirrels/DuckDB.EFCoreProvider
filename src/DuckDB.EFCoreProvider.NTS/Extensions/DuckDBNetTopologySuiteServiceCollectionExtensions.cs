using DuckDB.EFCoreProvider.NTS.Query.Internal;
using DuckDB.EFCoreProvider.NTS.Storage.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetTopologySuite;

namespace DuckDB.EFCoreProvider.NTS.Extensions;

/// <summary>
///     DuckDB NetTopologySuite specific extension methods for <see cref="IServiceCollection" />.
/// </summary>
public static class DuckDBNetTopologySuiteServiceCollectionExtensions
{
    /// <summary>
    ///     Adds the services required for NetTopologySuite spatial support with the DuckDB database provider
    ///     for Entity Framework to an <see cref="IServiceCollection" />. You use this method when using dependency
    ///     injection in your application, such as with ASP.NET. For more information on setting up dependency
    ///     injection, see <see href="https://go.microsoft.com/fwlink/?LinkId=526890">https://go.microsoft.com/fwlink/?LinkId=526890</see>.
    /// </summary>
    /// <remarks>
    ///     You use this method when using dependency injection in your application, such as with ASP.NET.
    ///     For applications that don't use dependency injection, calling
    ///     <see cref="DuckDBNetTopologySuiteDbContextOptionsBuilderExtensions.UseNetTopologySuite" /> instead is recommended.
    /// </remarks>
    /// <param name="serviceCollection">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <returns>The same service collection so that multiple calls can be chained.</returns>
    public static IServiceCollection AddEntityFrameworkDuckDBNetTopologySuite(
        this IServiceCollection serviceCollection)
    {
        serviceCollection.TryAddSingleton(NtsGeometryServices.Instance);

        new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<IRelationalTypeMappingSourcePlugin, DuckDBNetTopologySuiteTypeMappingSourcePlugin>()
            .TryAdd<IMethodCallTranslatorPlugin, DuckDBNetTopologySuiteMethodCallTranslatorPlugin>()
            .TryAdd<IAggregateMethodCallTranslatorPlugin, DuckDBNetTopologySuiteAggregateMethodCallTranslatorPlugin>()
            .TryAdd<IMemberTranslatorPlugin, DuckDBNetTopologySuiteMemberTranslatorPlugin>();

        // Replace the SQL generator factory so that geometry projections are wrapped
        // with ST_AsWKT() – DuckDB.NET cannot read native GEOMETRY columns (type ID 40).
        serviceCollection.Replace(
            ServiceDescriptor.Singleton<IQuerySqlGeneratorFactory, DuckDBNtsQuerySqlGeneratorFactory>());

        return serviceCollection;
    }
}