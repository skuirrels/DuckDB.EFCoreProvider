using DuckDB.EFCoreProvider.Infrastructure;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data.Common;

namespace DuckDB.EFCoreProvider.Extensions;

/// <summary>
///     DuckDB specific extension methods for <see cref="DbContextOptionsBuilder" />.
/// </summary>
public static class DuckDBDbContextOptionsBuilderExtensions
{
    /// <summary>Configures a context to execute LINQ, SaveChanges, generated values, transactions, and provider commands remotely over Quack.</summary>
    /// <remarks>
    ///     This profile is opt-in and experimental because Quack remains experimental in DuckDB 1.5.x.
    ///     The authentication token is retained in the context options but is excluded from EF logging and cache keys.
    /// </remarks>
    public static DbContextOptionsBuilder UseQuack(
        this DbContextOptionsBuilder optionsBuilder,
        string endpoint,
        string token,
        Action<QuackDbContextOptionsBuilder>? quackOptionsAction = null,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return optionsBuilder.UseDuckDB(
            "Data Source=:memory:",
            duckDB =>
            {
                duckDBOptionsAction?.Invoke(duckDB);
                duckDB.UseQuack(endpoint, token, quackOptionsAction);
            });
    }

    /// <summary>Configures a typed context to execute against a remote DuckDB server over Quack.</summary>
    public static DbContextOptionsBuilder<TContext> UseQuack<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string endpoint,
        string token,
        Action<QuackDbContextOptionsBuilder>? quackOptionsAction = null,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseQuack(
            (DbContextOptionsBuilder)optionsBuilder,
            endpoint,
            token,
            quackOptionsAction,
            duckDBOptionsAction);

    /// <summary>
    ///     Configures the context to use a DuckLake catalog backed by a local metadata file.
    /// </summary>
    /// <remarks>
    ///     The provider creates an in-memory DuckDB host connection, installs and loads the DuckLake extension,
    ///     attaches the catalog before EF uses provider-owned or caller-owned connections, and selects it as the default catalog.
    ///     For remote metadata, use the action overload and call
    ///     <see cref="DuckLakeDbContextOptionsBuilder.UseNamedSecret" /> or
    ///     <see cref="DuckLakeDbContextOptionsBuilder.UseDefaultSecret" />.
    /// </remarks>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="metadataPath">The local DuckDB file used for DuckLake metadata.</param>
    /// <param name="duckLakeOptionsAction">Optional DuckLake catalog configuration.</param>
    /// <param name="duckDBOptionsAction">Optional host DuckDB configuration, including extension and secret setup.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseDuckLake(
        this DbContextOptionsBuilder optionsBuilder,
        string metadataPath,
        Action<DuckLakeDbContextOptionsBuilder>? duckLakeOptionsAction = null,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataPath);

        return optionsBuilder.UseDuckDB(
            "Data Source=:memory:",
            duckDB =>
            {
                duckDBOptionsAction?.Invoke(duckDB);
                duckDB.UseDuckLake(metadataPath, duckLakeOptionsAction);
            });
    }

    /// <summary>
    ///     Configures the context to use a DuckLake catalog. The profile action must select local metadata or a DuckDB secret.
    /// </summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="duckLakeOptionsAction">DuckLake metadata, catalog, and access-mode configuration.</param>
    /// <param name="duckDBOptionsAction">Optional host DuckDB configuration, including extension and secret setup.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseDuckLake(
        this DbContextOptionsBuilder optionsBuilder,
        Action<DuckLakeDbContextOptionsBuilder> duckLakeOptionsAction,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(duckLakeOptionsAction);

        return optionsBuilder.UseDuckDB(
            "Data Source=:memory:",
            duckDB =>
            {
                duckDBOptionsAction?.Invoke(duckDB);
                duckDB.UseDuckLake(duckLakeOptionsAction);
            });
    }

    /// <summary>Configures the context to use a local DuckLake catalog.</summary>
    /// <remarks>
    ///     For remote metadata, use the action overload and call
    ///     <see cref="DuckLakeDbContextOptionsBuilder.UseNamedSecret" /> or
    ///     <see cref="DuckLakeDbContextOptionsBuilder.UseDefaultSecret" />.
    /// </remarks>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="metadataPath">The local DuckDB file used for DuckLake metadata.</param>
    /// <param name="duckLakeOptionsAction">Optional DuckLake catalog configuration.</param>
    /// <param name="duckDBOptionsAction">Optional host DuckDB configuration.</param>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <returns>The typed options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseDuckLake<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string metadataPath,
        Action<DuckLakeDbContextOptionsBuilder>? duckLakeOptionsAction = null,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDuckLake(
            (DbContextOptionsBuilder)optionsBuilder,
            metadataPath,
            duckLakeOptionsAction,
            duckDBOptionsAction);

    /// <summary>Configures the context to use a DuckLake catalog selected by the profile action.</summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="duckLakeOptionsAction">DuckLake metadata, catalog, and access-mode configuration.</param>
    /// <param name="duckDBOptionsAction">Optional host DuckDB configuration.</param>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <returns>The typed options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseDuckLake<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        Action<DuckLakeDbContextOptionsBuilder> duckLakeOptionsAction,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseDuckLake(
            (DbContextOptionsBuilder)optionsBuilder,
            duckLakeOptionsAction,
            duckDBOptionsAction);

    /// <summary>
    ///     Configures the context to store its data in an encrypted DuckDB database file.
    /// </summary>
    /// <remarks>
    ///     The provider hosts the encrypted file on a shared in-memory DuckDB database, attaches it with the key
    ///     returned by <paramref name="keyProvider" />, and selects it as the default catalog, so entities,
    ///     migrations, and the migrations history table all live inside the encrypted file. See
    ///     <see cref="DuckDBDbContextOptionsBuilder.UseEncryptedDatabase" /> for the key-handling and coverage
    ///     details.
    /// </remarks>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="path">The encrypted DuckDB database file. It is created on first attachment if missing.</param>
    /// <param name="keyProvider">Resolves the encryption key. It is invoked whenever the database is attached or an existing attachment is verified against this context's key.</param>
    /// <param name="encryptedDatabaseOptionsAction">Optional catalog alias, access-mode, and temporary-file configuration.</param>
    /// <param name="duckDBOptionsAction">Optional host DuckDB configuration, including extension and secret setup.</param>
    /// <returns>The options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder UseEncryptedDuckDB(
        this DbContextOptionsBuilder optionsBuilder,
        string path,
        Func<string> keyProvider,
        Action<DuckDBEncryptedDatabaseOptionsBuilder>? encryptedDatabaseOptionsAction = null,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(keyProvider);

        return optionsBuilder.UseDuckDB(
            DuckDBConnectionStringBuilder.InMemorySharedConnectionString,
            duckDB =>
            {
                duckDBOptionsAction?.Invoke(duckDB);
                duckDB.UseEncryptedDatabase(path, keyProvider, encryptedDatabaseOptionsAction);
            });
    }

    /// <summary>Configures a typed context to store its data in an encrypted DuckDB database file.</summary>
    /// <param name="optionsBuilder">The builder being used to configure the context.</param>
    /// <param name="path">The encrypted DuckDB database file. It is created on first attachment if missing.</param>
    /// <param name="keyProvider">Resolves the encryption key. It is invoked whenever the database is attached or an existing attachment is verified against this context's key.</param>
    /// <param name="encryptedDatabaseOptionsAction">Optional catalog alias, access-mode, and temporary-file configuration.</param>
    /// <param name="duckDBOptionsAction">Optional host DuckDB configuration, including extension and secret setup.</param>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <returns>The typed options builder so that further configuration can be chained.</returns>
    public static DbContextOptionsBuilder<TContext> UseEncryptedDuckDB<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder,
        string path,
        Func<string> keyProvider,
        Action<DuckDBEncryptedDatabaseOptionsBuilder>? encryptedDatabaseOptionsAction = null,
        Action<DuckDBDbContextOptionsBuilder>? duckDBOptionsAction = null)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseEncryptedDuckDB(
            (DbContextOptionsBuilder)optionsBuilder,
            path,
            keyProvider,
            encryptedDatabaseOptionsAction,
            duckDBOptionsAction);

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