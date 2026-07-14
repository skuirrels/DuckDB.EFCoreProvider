using DuckDB.EFCoreProvider.Diagnostics.Internal;
using DuckDB.EFCoreProvider.Infrastructure;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Internal;
using DuckDB.EFCoreProvider.Metadata.Conventions;
using DuckDB.EFCoreProvider.Metadata.Internal;
using DuckDB.EFCoreProvider.Migrations;
using DuckDB.EFCoreProvider.Migrations.Internal;
using DuckDB.EFCoreProvider.Query.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.EFCoreProvider.Update.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     DuckDB specific extension methods for <see cref="IServiceCollection" />.
/// </summary>
public static class DuckDBServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the given Entity Framework <see cref="DbContext" /> as a service in the <see cref="IServiceCollection" />
    ///     and configures it to connect to a DuckDB database.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This method is a shortcut for configuring a <see cref="DbContext" /> to use DuckDB. It does not support all options.
    ///         Use <see cref="O:EntityFrameworkServiceCollectionExtensions.AddDbContext" /> and related methods for full control of
    ///         this process.
    ///     </para>
    ///     <para>
    ///         Use this method when using dependency injection in your application, such as with ASP.NET Core.
    ///         For applications that don't use dependency injection, consider creating <see cref="DbContext" />
    ///         instances directly with its constructor. The <see cref="DbContext.OnConfiguring" /> method can then be
    ///         overridden to configure the DuckDB provider and connection string.
    ///     </para>
    ///     <para>
    ///         To configure the <see cref="DbContextOptions{TContext}" /> for the context, either override the
    ///         <see cref="DbContext.OnConfiguring" /> method in your derived context, or supply
    ///         an optional action to configure the <see cref="DbContextOptions" /> for the context.
    ///     </para>
    ///     <para>
    ///         See <see href="https://aka.ms/efcore-docs-di">Using DbContext with dependency injection</see> for more information and examples.
    ///     </para>
    ///     <para>
    ///         See <see href="https://aka.ms/efcore-docs-dbcontext-options">Using DbContextOptions</see>.
    ///     </para>
    /// </remarks>
    /// <param name="serviceCollection">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <param name="connectionString">The connection string of the database to connect to.</param>
    /// <param name="DuckDBOptionsAction">An optional action to allow additional DuckDB-specific configuration.</param>
    /// <param name="optionsAction">An optional action to configure the <see cref="DbContextOptions" /> for the context.</param>
    /// <typeparam name="TContext">The type of context to be registered.</typeparam>
    /// <returns>The same service collection so that multiple calls can be chained.</returns>
    public static IServiceCollection AddDuckDB<TContext>(
        this IServiceCollection serviceCollection,
        string? connectionString,
        Action<DuckDBDbContextOptionsBuilder>? DuckDBOptionsAction = null,
        Action<DbContextOptionsBuilder>? optionsAction = null)
        where TContext : DbContext
        => serviceCollection.AddDbContext<TContext>(
            (_, options) =>
            {
                optionsAction?.Invoke(options);
                options.UseDuckDB(connectionString, DuckDBOptionsAction);
            });

    /// <summary>
    ///     <para>
    ///         Adds the services required by the DuckDB database provider for Entity Framework
    ///         to an <see cref="IServiceCollection" />.
    ///     </para>
    ///     <para>
    ///         Warning: Do not call this method accidentally. It is much more likely you need
    ///         to call <see cref="AddDuckDB{TContext}" />.
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     Calling this method is no longer necessary when building most applications, including those that
    ///     use dependency injection in ASP.NET or elsewhere.
    ///     It is only needed when building the internal service provider for use with
    ///     the <see cref="DbContextOptionsBuilder.UseInternalServiceProvider" /> method.
    ///     This is not recommend other than for some advanced scenarios.
    /// </remarks>
    /// <param name="serviceCollection">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <returns>
    ///     The same service collection so that multiple calls can be chained.
    /// </returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddEntityFrameworkDuckDB(this IServiceCollection serviceCollection)
    {
        var builder = new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<LoggingDefinitions, DuckDBLoggingDefinitions>()
            .TryAdd<IDatabaseProvider, DatabaseProvider<DuckDBOptionsExtension>>()
            .TryAdd<IRelationalTypeMappingSource, DuckDBTypeMappingSource>()
            .TryAdd<IRawSqlCommandBuilder, DuckDBRawSqlCommandBuilder>()
            .TryAdd<ISqlGenerationHelper, DuckDBSqlGenerationHelper>()
            .TryAdd<IRelationalAnnotationProvider, DuckDBAnnotationProvider>()
            .TryAdd<IModelValidator, DuckDBModelValidator>()
            .TryAdd<IProviderConventionSetBuilder, DuckDBConventionSetBuilder>()
            .TryAdd<IModificationCommandBatchFactory, DuckDBModificationCommandBatchFactory>()
            .TryAdd<IModificationCommandFactory, DuckDBModificationCommandFactory>()
            .TryAdd<IRelationalConnection>(p => p.GetRequiredService<IDuckDBRelationalConnection>())
            .TryAdd<IMigrationsSqlGenerator, DuckDBMigrationsSqlGenerator>()
            .TryAdd<IRelationalDatabaseCreator, DuckDBDatabaseCreator>()
            .TryAdd<IHistoryRepository, DuckDBHistoryRepository>()
            .TryAdd<ICompiledQueryCacheKeyGenerator, DuckDBCompiledQueryCacheKeyGenerator>()
            .TryAdd<IQueryCompilationContextFactory, DuckDBQueryCompilationContextFactory>()
            .TryAdd<IMethodCallTranslatorProvider, DuckDBMethodCallTranslatorProvider>()
            .TryAdd<IAggregateMethodCallTranslatorProvider, DuckDBAggregateMethodCallTranslatorProvider>()
            .TryAdd<IMemberTranslatorProvider, DuckDBMemberTranslatorProvider>()
            .TryAdd<IEvaluatableExpressionFilter, DuckDBEvaluatableExpressionFilter>()
            .TryAdd<IRelationalTransactionFactory, DuckDBRelationalTransactionFactory>()
            .TryAdd<IQuerySqlGeneratorFactory, DuckDBQuerySqlGeneratorFactory>()
            .TryAdd<IQueryableMethodTranslatingExpressionVisitorFactory, DuckDBQueryableMethodTranslatingExpressionVisitorFactory>()
            .TryAdd<IRelationalSqlTranslatingExpressionVisitorFactory, DuckDBSqlTranslatingExpressionVisitorFactory>()
            .TryAdd<IQueryTranslationPreprocessorFactory, DuckDBQueryTranslationPreprocessorFactory>()
            .TryAdd<IQueryTranslationPostprocessorFactory, DuckDBQueryTranslationPostprocessorFactory>()
            .TryAdd<IUpdateSqlGenerator, DuckDBUpdateSqlGenerator>()
            .TryAdd<ISqlExpressionFactory, DuckDBSqlExpressionFactory>()
            .TryAdd<IRelationalParameterBasedSqlProcessorFactory, DuckDBParameterBasedSqlProcessorFactory>()
            .TryAdd<ISingletonOptions, IDuckDBSingletonOptions>(p => p.GetRequiredService<IDuckDBSingletonOptions>())
            .TryAdd<ISingletonOptions, IDuckLakeSingletonOptions>(p => p.GetRequiredService<IDuckLakeSingletonOptions>())
            .TryAddProviderSpecificServices(b => b
                .TryAddSingleton<IDuckDBSingletonOptions, DuckDBSingletonOptions>()
                .TryAddSingleton<IDuckLakeSingletonOptions, DuckLakeSingletonOptions>()
                .TryAddScoped<IDuckDBArchiveFileProbe, DuckDBArchiveFileProbe>()
                .TryAddScoped<IDuckDBRelationalConnection, DuckDBRelationalConnection>());

        builder.TryAddCoreServices();

        return serviceCollection;
    }
}