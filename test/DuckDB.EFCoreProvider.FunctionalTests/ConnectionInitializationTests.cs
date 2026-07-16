using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class ConnectionInitializationTests : DuckDBTestBase
{
    [ConditionalFact]
    public void ConfigureConnection_runs_after_open_and_can_apply_secret_style_setup()
    {
        var initialized = false;
        using var context = new InitContext(FileOptions<InitContext>(options => options.ConfigureConnection(connection =>
        {
            initialized = true;
            using var command = connection.CreateCommand();
            command.CommandText = "SET threads = 2";
            command.ExecuteNonQuery();
        })));

        context.Database.OpenConnection();

        Assert.True(initialized);
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT current_setting('threads')";
        Assert.Equal(2L, command.ExecuteScalar());
    }

    [ConditionalFact]
    public void LoadExtension_and_ConfigureConnection_are_stored_and_names_are_validated()
    {
        using var context = new InitContext(FileOptions<InitContext>(options => options
            .LoadExtension("httpfs")
            .ConfigureConnection(_ => { })));
        var extension = context.GetService<IDbContextOptions>().FindExtension<DuckDBOptionsExtension>()!;

        Assert.Equal(["httpfs"], extension.ExtensionsToLoad);
        Assert.Equal(
            new DuckDBExtensionConfiguration("httpfs", DuckDBExtensionLoadMode.InstallAndLoad),
            Assert.Single(extension.ConfiguredExtensions));
        Assert.NotNull(extension.ConnectionInitializer);
        Assert.Throws<ArgumentException>(() => FileOptions<InitContext>(options => options.LoadExtension("httpfs; DROP TABLE x")));
    }

    [ConditionalFact]
    public void Caller_managed_extension_is_recorded_without_running_install_or_load_sql()
    {
        using var context = new InitContext(FileOptions<InitContext>(options =>
            options.LoadExtension("deployment_owned", DuckDBExtensionLoadMode.CallerManaged)));

        context.Database.OpenConnection();

        var extension = context.GetService<IDbContextOptions>().FindExtension<DuckDBOptionsExtension>()!;
        Assert.Equal(
            new DuckDBExtensionConfiguration("deployment_owned", DuckDBExtensionLoadMode.CallerManaged),
            Assert.Single(extension.ConfiguredExtensions));
        Assert.Equal(ConnectionState.Open, context.Database.GetDbConnection().State);
    }

    [ConditionalFact]
    public void Failed_connection_initialization_closes_the_connection()
    {
        using var context = new InitContext(FileOptions<InitContext>(options => options
            .ConfigureConnection(_ => throw new InvalidOperationException("setup failed"))));

        Assert.Throws<InvalidOperationException>(() => context.Database.OpenConnection());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private sealed class InitContext(DbContextOptions<InitContext> options) : DbContext(options);
}