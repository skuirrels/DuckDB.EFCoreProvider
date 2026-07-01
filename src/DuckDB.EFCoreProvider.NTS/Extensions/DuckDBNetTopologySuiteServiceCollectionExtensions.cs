using DuckDB.EFCoreProvider.NTS.Query.Internal;
using DuckDB.EFCoreProvider.NTS.Storage.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetTopologySuite;

namespace DuckDB.EFCoreProvider.NTS.Extensions;

public static class DuckDBNetTopologySuiteServiceCollectionExtensions
{
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