using DuckDB.EFCoreProvider.Infrastructure;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     DuckDB specific extension methods for <see cref="DbContextOptionsBuilder" />.
/// </summary>
public static class DuckDBDbContextOptionsBuilderExtensions
{
    /// <summary>
    ///     Configures the context to connect to a DuckDB database, but without initially setting any
    ///     <see cref="DbConnection"/> or connection string.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The connection or connection string must be set before the <see cref="DbContext" /> is used to connect
    ///         to a database. Set a connection using <see cref="RelationalDatabaseFacadeExtensions.SetDbConnection" />.
    ///         Set a connection string using <see cref="RelationalDatabaseFacadeExtensions.SetConnectionString" />.
    ///     </para>
    /// </remarks>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="duckDBOptionsAction">An optional action to allow additional DuckDB specific configuration</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseDuckDB(
        this DbContextOptionsBuilder optionsBuilder,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
    {
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(GetOrCreateExtension(optionsBuilder));
        
        ConfigureWarnings(optionsBuilder);
        
        duckDBOptionsAction?.Invoke(new DuckDBDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    /// <summary>
    ///     Configures the context to connect to a DuckDB database.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connectionString">The connection string of the database to connect to.</param>
    /// <param name="duckDBOptionsAction">An optional action to allow additional DuckDB specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseDuckDB(
        this DbContextOptionsBuilder optionsBuilder,
        string? connectionString,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
    {
        var extension = (DuckDBOptionsExtension)GetOrCreateExtension(optionsBuilder).WithConnectionString(connectionString);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        
        ConfigureWarnings(optionsBuilder);
        
        duckDBOptionsAction?.Invoke(new DuckDBDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    /// <summary>
    ///     Configures the context to connect to a DuckDB database.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">
    ///     An existing <see cref="DbConnection" /> to be used to connect to the database. If the connection is
    ///     in the open state then EF will not open or close the connection. If the connection is in the closed
    ///     state then EF will open and close the connection as needed. The caller owns the connection and is
    ///     responsible for its disposal.
    /// </param>
    /// <param name="duckDBOptionsAction">An optional action to allow additional DuckDB-specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseDuckDB(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
        => UseDuckDB(optionsBuilder, connection, false, duckDBOptionsAction);

    /// <summary>
    ///     Configures the context to connect to a DuckDB database.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">
    ///     An existing <see cref="DbConnection" /> to be used to connect to the database. If the connection is
    ///     in the open state then EF will not open or close the connection. If the connection is in the closed
    ///     state then EF will open and close the connection as needed.
    /// </param>
    /// <param name="contextOwnsConnection">
    ///     If <see langword="true" />, then EF will take ownership of the connection and will
    ///     dispose it in the same way it would dispose a connection created by EF. If <see langword="false" />, then the caller still
    ///     owns the connection and is responsible for its disposal.
    /// </param>
    /// <param name="duckDBOptionsAction">An optionals action to allow additional DuckDB specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseDuckDB(
        this DbContextOptionsBuilder optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var extension = (DuckDBOptionsExtension)GetOrCreateExtension(optionsBuilder).WithConnection(connection, contextOwnsConnection);
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        
        ConfigureWarnings(optionsBuilder);
        
        duckDBOptionsAction?.Invoke(new DuckDBDbContextOptionsBuilder(optionsBuilder));

        return optionsBuilder;
    }

    /// <summary>
    ///     Configures the context to connect to a DuckDB database, but without initially setting any
    ///     <see cref="DbConnection"/> or connection string.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The connection or connection string must be set before the <see cref="DbContext" /> is used to connect
    ///         to a database. Set a connection using <see cref="RelationalDatabaseFacadeExtensions.SetDbConnection" />.
    ///         Set a connection string using <see cref="RelationalDatabaseFacadeExtensions.SetConnectionString" />.
    ///     </para>
    /// </remarks>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="duckDBOptionsAction">An optional action to allow additional DuckDB specific configuration.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseDuckDB<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDuckDB(
            (DbContextOptionsBuilder)optionsBuilder, duckDBOptionsAction);

    /// <summary>
    ///     Configures the context to connect to a DuckDB database.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connectionString">The connection string of the database to connect to.</param>
    /// <param name="duckDBOptionsAction">An optional action to allow additional DuckDB specific configuration.</param>
    /// <typeparam name="TContext">The type of context to be configured.</typeparam>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseDuckDB<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string? connectionString,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDuckDB(
            (DbContextOptionsBuilder)optionsBuilder, connectionString, duckDBOptionsAction);

    /// <summary>
    ///     Configures the context to connect to a DuckDB database.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">
    ///     An existing <see cref="DbConnection" /> to be used to connect to the database. If the connection is
    ///     in the open state then EF will not open or close the connection. If the connection is in the closed
    ///     state then EF will open and close the connection as needed. The caller owns the connection and is
    ///     responsible for its disposal.
    /// </param>
    /// <param name="duckDBOptionsAction">An optional action to allow additional DuckDB specific configuration.</param>
    /// <typeparam name="TContext">The type of context to be configured.</typeparam>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseDuckDB<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDuckDB(
            (DbContextOptionsBuilder)optionsBuilder, connection, duckDBOptionsAction);

    /// <summary>
    ///     Configures the context to connect to a DuckDB database.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="connection">
    ///     An existing <see cref="DbConnection" /> to be used to connect to the database. If the connection is
    ///     in the open state then EF will not open or close the connection. If the connection is in the closed
    ///     state then EF will open and close the connection as needed.
    /// </param>
    /// <param name="contextOwnsConnection">
    ///     If <see langword="true" />, then EF will take ownership of the connection and will
    ///     dispose it in the same way it would dispose a connection created by EF. If <see langword="false" />, then the caller still
    ///     owns the connection and is responsible for its disposal.
    /// </param>
    /// <param name="duckDBOptionsAction">An optional action to allow additional DuckDB specific configuration.</param>
    /// <typeparam name="TContext">The type of context to be configured.</typeparam>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseDuckDB<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        DbConnection connection,
        bool contextOwnsConnection,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDuckDB(
            (DbContextOptionsBuilder)optionsBuilder, connection, contextOwnsConnection, duckDBOptionsAction);

    private static DuckDBOptionsExtension GetOrCreateExtension(DbContextOptionsBuilder options)
        => options.Options.FindExtension<DuckDBOptionsExtension>()
           ?? new DuckDBOptionsExtension();

    private static void ConfigureWarnings(DbContextOptionsBuilder optionsBuilder)
    {
        var coreOptionsExtension
            = optionsBuilder.Options.FindExtension<CoreOptionsExtension>()
              ?? new CoreOptionsExtension();

        coreOptionsExtension = RelationalOptionsExtension.WithDefaultWarningConfiguration(coreOptionsExtension);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(coreOptionsExtension);
    }
}