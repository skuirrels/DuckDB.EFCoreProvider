using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud.fakeDb;
using System.Data.Common;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class AdoNetSubstitutionTests
{
    [ConditionalFact]
    public void Native_parameterized_query_preserves_values_and_normalizes_parameter_names()
    {
        var interceptor = new CommandCaptureInterceptor();
        using var connection = new DuckDBConnection("Data Source=:memory:");
        using var context = CreateNativeContext(connection, interceptor);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        context.Add(new Widget { Id = 1, Name = "Ada" });
        context.SaveChanges();
        interceptor.Commands.Clear();

        var name = "Ada";
        var widget = context.Widgets.Single(row => row.Name == name);

        Assert.Equal(1, widget.Id);
        var command = Assert.Single(interceptor.Commands.Where(
            command => command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)));
        AssertNativeParameter(command, "Ada");
    }

    [ConditionalFact]
    public void Native_SaveChanges_preserves_values_and_normalizes_parameter_names()
    {
        var interceptor = new CommandCaptureInterceptor();
        using var connection = new DuckDBConnection("Data Source=:memory:");
        using var context = CreateNativeContext(connection, interceptor);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        interceptor.Commands.Clear();

        context.Add(new Widget { Id = 7, Name = "Grace" });
        context.SaveChanges();

        var command = Assert.Single(interceptor.Commands.Where(
            command => command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase)));
        AssertNativeParameter(command, 7);
        AssertNativeParameter(command, "Grace");
    }

    [Fact]
    public void Parameter_helper_keeps_substituted_names_and_records_metadata_for_every_parameter_type()
    {
        using var metadata = DuckDBParameterMetadataRegistry.BeginCapture();
        var mapping = DuckDBStringTypeMapping.Default;
        var nativeParameter = new DuckDBParameter("$native", "Ada");
        var substitutedParameter = new fakeDbParameter
        {
            ParameterName = "$substituted",
            Value = "Grace"
        };

        nativeParameter.ConfigureNameAndMetadata(mapping);
        substitutedParameter.ConfigureNameAndMetadata(mapping);

        Assert.Equal("native", nativeParameter.ParameterName);
        Assert.Equal("$substituted", substitutedParameter.ParameterName);
        Assert.True(metadata.TryGetTypeMapping(nativeParameter, out var nativeMapping));
        Assert.Same(mapping, nativeMapping);
        Assert.True(metadata.TryGetTypeMapping(substitutedParameter, out var substitutedMapping));
        Assert.Same(mapping, substitutedMapping);
    }

    [Fact]
    public void Substituted_connection_uses_its_strict_parameter_type_and_keeps_parameter_names()
    {
        var connection = new CapturingFakeDbConnection();
        using var context = CreateSubstitutedContext(connection);
        connection.EnqueueReaderResult(
            [new Dictionary<string, object?> { [nameof(Widget.Id)] = 1 }]);

        var name = "Ada";
        var ids = context.Widgets.Where(row => row.Name == name).Select(row => row.Id).ToList();

        Assert.Equal([1], ids);
        var command = Assert.Single(connection.ExecutedReaderCommands);
        var parameter = Assert.Single(command.Parameters);
        Assert.Equal(typeof(fakeDbParameter), parameter.ParameterType);
        Assert.StartsWith("$", parameter.Name);
        Assert.Contains(parameter.Name, command.CommandText);
        Assert.Equal("Ada", parameter.Value);
    }

    [Fact]
    public void EnsureCreated_uses_the_substituted_commands_parameter_type_for_HasTables()
    {
        var connection = new CapturingFakeDbConnection();
        using var context = CreateSubstitutedContext(connection);
        connection.EnqueueScalarResult(false);

        var created = context.Database.EnsureCreated();

        Assert.True(created);
        var command = Assert.Single(connection.ExecutedScalarCommands.Where(
            command => command.CommandText.Contains("duckdb_tables", StringComparison.OrdinalIgnoreCase)));
        var parameter = Assert.Single(command.Parameters);
        Assert.Equal(typeof(fakeDbParameter), parameter.ParameterType);
        Assert.Equal("default_table_name", parameter.Name);
        Assert.Equal("__EFMigrationsHistory", parameter.Value);
    }

    [Fact]
    public void Registered_factory_creates_the_connection_used_by_EF_end_to_end()
    {
        var connection = new CapturingFakeDbConnection();
        var factory = new CapturingProviderFactory(connection);
        using var serviceProvider = CreateServiceProvider(factory);
        var options = new DbContextOptionsBuilder<WidgetContext>()
            .UseDuckDB("Data Source=:memory:")
            .UseInternalServiceProvider(serviceProvider)
            .Options;
        using var context = new WidgetContext(options);
        connection.EnqueueReaderResult(
            [new Dictionary<string, object?> { [nameof(Widget.Id)] = 42 }]);

        var minimumId = 40;
        var ids = context.Widgets.Where(row => row.Id > minimumId).Select(row => row.Id).ToList();

        Assert.Same(factory, serviceProvider.GetRequiredService<DbProviderFactory>());
        Assert.Same(connection, context.Database.GetDbConnection());
        Assert.Equal(1, factory.CreateConnectionCount);
        Assert.Equal([42], ids);
        var parameter = Assert.Single(Assert.Single(connection.ExecutedReaderCommands).Parameters);
        Assert.Equal(typeof(fakeDbParameter), parameter.ParameterType);
        Assert.Equal(40, parameter.Value);
    }

    [Fact]
    public void Substituted_connection_preserves_array_parameter_type_name_and_value()
    {
        var connection = new CapturingFakeDbConnection();
        using var context = CreateSubstitutedContext(connection);
        connection.EnqueueReaderResult(
            [new Dictionary<string, object?> { [nameof(Widget.Id)] = 9 }]);

        var scores = new[] { 3, 5, 8 };
        var ids = context.Widgets.Where(row => row.Scores == scores).Select(row => row.Id).ToList();

        Assert.Equal([9], ids);
        var command = Assert.Single(connection.ExecutedReaderCommands);
        var parameter = Assert.Single(command.Parameters);
        Assert.Equal(typeof(fakeDbParameter), parameter.ParameterType);
        Assert.StartsWith("$", parameter.Name);
        Assert.Contains(parameter.Name, command.CommandText);
        Assert.Equal(scores, Assert.IsAssignableFrom<IEnumerable<int>>(parameter.Value));
    }

    [Fact]
    public void Substituted_connection_materializes_temporal_values_through_DbDataReader()
    {
        var connection = new CapturingFakeDbConnection();
        using var context = CreateSubstitutedContext(connection);
        var occurredAt = new DateTime(2026, 9, 5, 14, 30, 15, DateTimeKind.Unspecified);
        var startTime = new TimeOnly(8, 45, 30);
        connection.EnqueueReaderResult(
            [new Dictionary<string, object?>
            {
                [nameof(Widget.OccurredAt)] = occurredAt,
                [nameof(Widget.StartTime)] = startTime
            }]);

        var value = context.Widgets
            .Select(row => new { row.OccurredAt, row.StartTime })
            .Single();

        Assert.Equal(occurredAt, value.OccurredAt);
        Assert.Equal(startTime, value.StartTime);
    }

    [Fact]
    public void Substituted_factory_rejects_a_native_connection_initializer_before_configuration_runs()
    {
        var connection = new CapturingFakeDbConnection();
        var factory = new CapturingProviderFactory(connection);
        using var serviceProvider = CreateServiceProvider(factory);
        var initializerCalled = false;
        var options = new DbContextOptionsBuilder<WidgetContext>()
            .UseDuckDB(
                "Data Source=:memory:",
                duckDb => duckDb.ConfigureConnection(_ => initializerCalled = true))
            .UseInternalServiceProvider(serviceProvider)
            .Options;
        using var context = new WidgetContext(options);

        var exception = Assert.Throws<NotSupportedException>(() => context.Database.OpenConnection());

        Assert.Contains("requires a native DuckDBConnection", exception.Message);
        Assert.False(initializerCalled);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void Quack_profile_keeps_connection_creation_native_when_a_factory_is_registered()
    {
        var substitutedConnection = new CapturingFakeDbConnection();
        var factory = new CapturingProviderFactory(substitutedConnection);
        using var serviceProvider = CreateServiceProvider(factory);
        var options = new DbContextOptionsBuilder<WidgetContext>()
            .UseQuack("quack:localhost:1", "valid-token")
            .UseInternalServiceProvider(serviceProvider)
            .Options;
        using var context = new WidgetContext(options);

        Assert.IsType<QuackDbConnection>(context.Database.GetDbConnection());
        Assert.Equal(0, factory.CreateConnectionCount);
    }

    [Fact]
    public void Appender_operations_reject_a_substituted_connection_explicitly()
    {
        var connection = new CapturingFakeDbConnection();
        using var context = CreateSubstitutedContext(connection);

        var exception = Assert.Throws<NotSupportedException>(
            () => context.BulkInsert([new Widget { Id = 1, Name = "Ada" }]));

        Assert.Contains("BulkInsert requires", exception.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void Archive_operations_reject_a_substituted_connection_before_opening_it()
    {
        var connection = new CapturingFakeDbConnection();
        using var context = new TieredWidgetContext(
            new DbContextOptionsBuilder<TieredWidgetContext>()
                .UseDuckDB(connection)
                .Options);

        var exception = Assert.Throws<NotSupportedException>(
            () => context.Database.EnsureTieredStoresCreated());

        Assert.Contains("require a native DuckDBConnection", exception.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    private static WidgetContext CreateNativeContext(
        DbConnection connection,
        CommandCaptureInterceptor interceptor)
        => new(
            new DbContextOptionsBuilder<WidgetContext>()
                .UseDuckDB(connection)
                .AddInterceptors(interceptor)
                .Options);

    private static WidgetContext CreateSubstitutedContext(DbConnection connection)
        => new(
            new DbContextOptionsBuilder<WidgetContext>()
                .UseDuckDB(connection)
                .Options);

    private static ServiceProvider CreateServiceProvider(DbProviderFactory factory)
    {
        var services = new ServiceCollection();
        services.AddSingleton<DbProviderFactory>(factory);
        services.AddEntityFrameworkDuckDB();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static void AssertNativeParameter(CapturedCommand command, object expectedValue)
    {
        var parameter = Assert.Single(command.Parameters.Where(parameter => Equals(parameter.Value, expectedValue)));
        Assert.Equal(typeof(DuckDBParameter), parameter.ParameterType);
        Assert.NotEmpty(parameter.Name);
        Assert.DoesNotContain('$', parameter.Name);
        Assert.Contains("$" + parameter.Name, command.CommandText);
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<CapturedCommand> Commands { get; } = [];

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Capture(command);
            return result;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Capture(command);
            return result;
        }

        private void Capture(DbCommand command)
            => Commands.Add(
                new CapturedCommand(
                    command.CommandText,
                    command.Parameters.Cast<DbParameter>()
                        .Select(parameter => new CapturedParameter(
                            parameter.ParameterName,
                            parameter.Value,
                            parameter.GetType()))
                        .ToList()));
    }

    private sealed class Widget
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int[] Scores { get; set; } = [];

        public DateTime OccurredAt { get; set; }

        public TimeOnly StartTime { get; set; }
    }

    private sealed class WidgetContext(DbContextOptions<WidgetContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Widget>().Property(widget => widget.Id).ValueGeneratedNever();
    }

    private sealed class TieredWidgetContext(DbContextOptions<TieredWidgetContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Widget>().Property(widget => widget.Id).ValueGeneratedNever();
            modelBuilder.ToTieredStore<Widget>(
                widget => widget.OccurredAt,
                Path.Combine(Path.GetTempPath(), "duckdb-substitution-archive"));
        }
    }
}
