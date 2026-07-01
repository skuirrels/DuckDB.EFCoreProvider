using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure;
using DuckDB.NET.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Data.Common;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

public class DuckDBTestStore : RelationalTestStore
{
    public const int CommandTimeout = 30;

    public static DuckDBTestStore GetOrCreate(string name)
        => new(name);

    public static async Task<DuckDBTestStore> GetOrCreateInitializedAsync(string name)
        => await new DuckDBTestStore(name).InitializeDuckDBAsync(
            new ServiceCollection().AddEntityFrameworkDuckDB().BuildServiceProvider(validateScopes: true),
            (Func<DbContext>?)null,
            null);

    public static DuckDBTestStore GetExisting(string name)
        => new(name, seed: false);

    public static DuckDBTestStore Create(string name)
        => new(name, shared: false);

    private readonly bool _seed;
    private bool _loadSpatial;

    private DuckDBTestStore(string name, bool seed = true, bool shared = true)
        : base(name, shared, CreateConnection(name))
        => _seed = seed;

    public DuckDBTestStore WithSpatialExtension()
    {
        _loadSpatial = true;
        return this;
    }

    public virtual DbContextOptionsBuilder AddProviderOptions(
        DbContextOptionsBuilder builder,
        Action<DuckDBDbContextOptionsBuilder>? configureDuckDB)
        => UseConnectionString
            ? builder.UseDuckDB(
                ConnectionString, b =>
                {
                    b.CommandTimeout(CommandTimeout);
                    b.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
                    configureDuckDB?.Invoke(b);
                    b.ReverseNullOrdering();
                })
            : builder.UseDuckDB(
                Connection, b =>
                {
                    b.CommandTimeout(CommandTimeout);
                    b.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
                    configureDuckDB?.Invoke(b);
                    b.ReverseNullOrdering();
                });

    public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
        => AddProviderOptions(builder, configureDuckDB: null);

    public async Task<DuckDBTestStore> InitializeDuckDBAsync(
        IServiceProvider? serviceProvider,
        Func<DbContext>? createContext,
        Func<DbContext, Task>? seed)
        => (DuckDBTestStore)await InitializeAsync(serviceProvider, createContext, seed);

    public async Task<DuckDBTestStore> InitializeDuckDBAsync(
        IServiceProvider serviceProvider,
        Func<DuckDBTestStore, DbContext> createContext,
        Func<DbContext, Task> seed)
        => (DuckDBTestStore)await InitializeAsync(serviceProvider, () => createContext(this), seed);

    public override Task CleanAsync(DbContext context)
    {
        context.Database.EnsureClean();
        return Task.CompletedTask;
    }

    public int ExecuteNonQuery(string sql, params object[] parameters)
    {
        using var command = CreateCommand(sql, parameters);
        return command.ExecuteNonQuery();
    }

    public T ExecuteScalar<T>(string sql, params object[] parameters)
    {
        using var command = CreateCommand(sql, parameters);
        return (T)command.ExecuteScalar()!;
    }

    private DbCommand CreateCommand(string commandText, object[] parameters)
    {
        var command = (DuckDBCommand)Connection.CreateCommand();

        command.CommandText = commandText;
        command.CommandTimeout = CommandTimeout;

        for (var i = 0; i < parameters.Length; i++)
        {
            command.Parameters.Add(new DuckDBParameter("p" + i, parameters[i]));
        }

        return command;
    }

    private static DuckDBConnection CreateConnection(string name)
    {
        var connectionString = new DuckDBConnectionStringBuilder
        {
            DataSource = name + ".db",
        }.ToString();

        return new DuckDBConnection(connectionString);
    }

    public override void OpenConnection()
    {
        var connection = (DuckDBConnection)Connection;

        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
            LoadSpatialExtensionIfNeeded();
        }
    }

    public override async Task OpenConnectionAsync()
    {
        var connection = (DuckDBConnection)Connection;

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
            LoadSpatialExtensionIfNeeded();
        }
    }

    private void LoadSpatialExtensionIfNeeded()
    {
        if (!_loadSpatial)
        {
            return;
        }

        ExecuteNonQuery("INSTALL spatial");
        ExecuteNonQuery("LOAD spatial");
    }

    protected override string OpenDelimiter => "\"";

    protected override string CloseDelimiter => "\"";
}
