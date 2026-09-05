using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Extensions.Internal;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Query.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data;
using System.Globalization;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class QuackProfileTests : DuckDBTestBase
{
    [QuackIntegrationFact]
    public async Task Quack_profile_round_trips_ef_commands_transactions_bulk_and_upsert()
    {
        var extensionPath = Environment.GetEnvironmentVariable("DUCKDB_QUACK_EXTENSION_PATH")!;
        var port = Environment.GetEnvironmentVariable("DUCKDB_QUACK_TEST_PORT") ?? "19494";
        var endpoint = $"quack:localhost:{port}";
        const string clientConnectionString = "Data Source=:memory:;allow_unsigned_extensions=true";
        var serverOptions = new DbContextOptionsBuilder<QuackContext>()
            .UseDuckDB(
                $"Data Source={DbPath};allow_unsigned_extensions=true",
                duckDb => duckDb.LoadExtension("httpfs"))
            .Options;

        await using var serverContext = new QuackContext(serverOptions);
        await using var server = await serverContext.Database.StartQuackServerAsync(new DuckDBQuackServerOptions
        {
            Uri = endpoint,
            Token = "integration-token",
            DisableSsl = true,
            ExtensionPath = extensionPath
        });

        var clientOptions = new DbContextOptionsBuilder<QuackContext>()
            .UseDuckDB(
                clientConnectionString,
                duckDb => duckDb.UseQuack(
                    endpoint,
                    "integration-token",
                    quack => quack.DisableSsl().ExtensionPath(extensionPath)))
            .Options;
        await using var client = new QuackContext(clientOptions);

        Assert.True(await client.Database.EnsureCreatedAsync());
        Assert.Equal(
            1,
            await serverContext.Database.SqlQueryRaw<long>(
                    "SELECT count(*) AS Value FROM duckdb_constraints() WHERE constraint_type = 'FOREIGN KEY'")
                .SingleAsync());

        client.Items.Add(new QuackItem { Id = 1, Name = "created" });
        Assert.Equal(1, await client.SaveChangesAsync());
        var tracked = await client.Items.SingleAsync(item => item.Id == 1);
        tracked.Name = "updated";
        Assert.Equal(1, await client.SaveChangesAsync());

        await using (var transaction = await client.Database.BeginTransactionAsync())
        {
            client.Items.Add(new QuackItem { Id = 2, Name = "rolled-back" });
            Assert.Equal(1, await client.SaveChangesAsync());
            await transaction.RollbackAsync();
        }
        client.ChangeTracker.Clear();
        Assert.False(await client.Items.AnyAsync(item => item.Id == 2));

        Assert.Equal(2, await client.BulkInsertAsync(
        [
            new QuackItem { Id = 3, Name = "bulk-three" },
            new QuackItem { Id = 4, Name = "bulk-four" }
        ]));

        await using (var transaction = await client.Database.BeginTransactionAsync())
        {
            Assert.Equal(1, await client.BulkInsertAsync(
            [
                new QuackItem { Id = 6, Name = "rolled-back-bulk" }
            ]));
            Assert.Equal(2, await client.UpsertAsync(
            [
                new QuackItem { Id = 1, Name = "rolled-back-upsert" },
                new QuackItem { Id = 7, Name = "rolled-back-upsert-insert" }
            ]));
            await transaction.RollbackAsync();
        }
        client.ChangeTracker.Clear();
        Assert.False(await client.Items.AnyAsync(item => item.Id == 6 || item.Id == 7));
        Assert.Equal("updated", await client.Items.Where(item => item.Id == 1).Select(item => item.Name).SingleAsync());

        client.Remove(await client.Items.SingleAsync(item => item.Id == 4));
        Assert.Equal(1, await client.SaveChangesAsync());
        Assert.Equal(2, await client.UpsertAsync(
        [
            new QuackItem { Id = 1, Name = "upserted" },
            new QuackItem { Id = 5, Name = "inserted" }
        ]));
        Assert.Equal(0, await client.UpsertAsync(Array.Empty<QuackItem>()));
        await Assert.ThrowsAnyAsync<Exception>(() => client.UpsertAsync(
        [
            new QuackItem { Id = 8, Name = null! }
        ]));
        Assert.Equal(
            0,
            await serverContext.Database.SqlQueryRaw<long>(
                    "SELECT count(*) AS Value FROM duckdb_tables() WHERE table_name LIKE '__duckdb_upsert_%'")
                .SingleAsync());

        var plan = client.Database.GetDuckDBCommandPlan(
            client.Items.Where(item => item.Id >= 3).OrderBy(item => item.Id));
        await using var replay = await client.Database.ReplayQuackCommandAsync(plan);
        var replayed = new List<int>();
        await foreach (var row in replay.ReadRowsAsync())
        {
            replayed.Add(Convert.ToInt32(row.Span[0], System.Globalization.CultureInfo.InvariantCulture));
        }
        Assert.Equal([3, 5], replayed);

        var rows = await client.Items.AsNoTracking().OrderBy(item => item.Id).ToListAsync();
        Assert.Equal([1, 3, 5], rows.Select(item => item.Id));
        Assert.Equal("upserted", rows[0].Name);

        var generated = new GeneratedQuackItem { Name = "generated" };
        client.GeneratedItems.Add(generated);
        Assert.Equal(1, await client.SaveChangesAsync());
        Assert.True(generated.Id > 0);
        Assert.Equal("literal-default", generated.LiteralDefault);
        Assert.NotEqual(default, generated.CreatedAt);
        Assert.NotEqual(Guid.Empty, generated.CorrelationId);
        Assert.Equal("GENERATED", generated.UpperName);

        generated.Name = "changed";
        Assert.Equal(1, await client.SaveChangesAsync());
        Assert.Equal("CHANGED", generated.UpperName);

        var schemaGenerated = new SchemaGeneratedQuackItem { Name = "schema-generated" };
        client.SchemaGeneratedItems.Add(schemaGenerated);
        Assert.Equal(1, await client.SaveChangesAsync());
        Assert.True(schemaGenerated.Id > 0);
        Assert.NotEqual(default, schemaGenerated.CreatedAt);

        var parent = new QuackParent
        {
            Id = 1,
            Name = "parent",
            Children =
            [
                new QuackChild { Id = 1, Value = "first" },
                new QuackChild { Id = 2, Value = "second" }
            ]
        };
        client.Parents.Add(parent);
        Assert.Equal(3, await client.SaveChangesAsync());
        client.ChangeTracker.Clear();
        var included = await client.Parents
            .AsNoTracking()
            .AsSplitQuery()
            .Include(value => value.Children)
            .SingleAsync(value => value.Id == 1);
        Assert.Equal(["first", "second"], included.Children.OrderBy(value => value.Id).Select(value => value.Value));

        // Regression coverage for duckdb-quack#248: a fresh client must import the catalog without disabling its FK.
        await using (var constraintClient = new QuackContext(clientOptions))
        {
            constraintClient.Children.Add(new QuackChild { Id = 3, ParentId = 999, Value = "orphan" });
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => constraintClient.SaveChangesAsync());
            Assert.Contains("foreign key", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        Assert.False(await client.Children.AnyAsync(child => child.Id == 3));

        var raw = await client.Items
            .FromSqlInterpolated($"SELECT * FROM \"Items\" WHERE \"Id\" = {3}")
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal("bulk-three", raw.Name);
        Assert.Equal(1, await client.Items.Where(item => item.Id == 3)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Name, "set-based")));
        Assert.Equal("set-based", await client.Items.Where(item => item.Id == 3).Select(item => item.Name).SingleAsync());

        await using (var batchingClient = new QuackContext(
                         new DbContextOptionsBuilder<QuackContext>()
                             .UseDuckDB(
                                 clientConnectionString,
                                 duckDb => duckDb
                                     .EnableBulkInsertBatching()
                                     .EnableBulkUpdateBatching()
                                     .EnableBulkDeleteBatching()
                                     .UseQuack(
                                         endpoint,
                                         "integration-token",
                                         quack => quack.DisableSsl().ExtensionPath(extensionPath)))
                             .Options))
        {
            var first = new GeneratedQuackItem { Name = "batch-one" };
            var second = new GeneratedQuackItem { Name = "batch-two" };
            batchingClient.GeneratedItems.AddRange(first, second);
            Assert.Equal(2, await batchingClient.SaveChangesAsync());
            Assert.True(first.Id > generated.Id);
            Assert.True(second.Id > first.Id);
            Assert.Equal("BATCH-ONE", first.UpperName);
            Assert.Equal("BATCH-TWO", second.UpperName);

            first.Name = "batch-one-updated";
            second.Name = "batch-two-updated";
            Assert.Equal(2, await batchingClient.SaveChangesAsync());
            Assert.Equal("BATCH-ONE-UPDATED", first.UpperName);
            Assert.Equal("BATCH-TWO-UPDATED", second.UpperName);

            batchingClient.GeneratedItems.RemoveRange(first, second);
            Assert.Equal(2, await batchingClient.SaveChangesAsync());
            Assert.False(await batchingClient.GeneratedItems.AnyAsync(
                item => item.Id == first.Id || item.Id == second.Id));

            var secondParent = new QuackParent { Id = 2, Name = "batch-parent-two" };
            var thirdParent = new QuackParent { Id = 3, Name = "batch-parent-three" };
            batchingClient.Parents.AddRange(secondParent, thirdParent);
            Assert.Equal(2, await batchingClient.SaveChangesAsync());
            secondParent.Name = "batch-parent-two-updated";
            thirdParent.Name = "batch-parent-three-updated";
            Assert.Equal(2, await batchingClient.SaveChangesAsync());
            Assert.Equal(
                ["batch-parent-two-updated", "batch-parent-three-updated"],
                await batchingClient.Parents
                    .Where(item => item.Id == 2 || item.Id == 3)
                    .OrderBy(item => item.Id)
                    .Select(item => item.Name)
                    .ToListAsync());
            batchingClient.Parents.RemoveRange(secondParent, thirdParent);
            Assert.Equal(2, await batchingClient.SaveChangesAsync());
        }

        await using (var concurrencyOne = new QuackContext(clientOptions))
        await using (var concurrencyTwo = new QuackContext(clientOptions))
        {
            var first = await concurrencyOne.Items.SingleAsync(item => item.Id == 1);
            var second = await concurrencyTwo.Items.SingleAsync(item => item.Id == 1);
            first.Name = "concurrency-winner";
            second.Name = "concurrency-loser";
            Assert.Equal(1, await concurrencyOne.SaveChangesAsync());
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => concurrencyTwo.SaveChangesAsync());
        }

        Assert.Equal(1, await client.Children.Where(child => child.ParentId == 1 && child.Id == 2).ExecuteDeleteAsync());

        await serverContext.Database.ExecuteSqlRawAsync(
            "CREATE SCHEMA analytics; "
            + "CREATE TABLE analytics.schema_items (id INTEGER PRIMARY KEY, name VARCHAR NOT NULL);");
        await using (var schemaClient = new SchemaQuackContext(
                         new DbContextOptionsBuilder<SchemaQuackContext>()
                             .UseDuckDB(
                                 clientConnectionString,
                                 duckDb => duckDb.UseQuack(
                                     endpoint,
                                     "integration-token",
                                     quack => quack.DisableSsl().ExtensionPath(extensionPath)))
                             .Options))
        {
            Assert.Equal(1, await schemaClient.BulkInsertAsync(
            [
                new SchemaQuackItem { Id = 1, Name = "bulk" }
            ]));
            Assert.Equal(2, await schemaClient.UpsertAsync(
            [
                new SchemaQuackItem { Id = 1, Name = "updated" },
                new SchemaQuackItem { Id = 2, Name = "inserted" }
            ]));
            Assert.Equal(
                ["updated", "inserted"],
                await schemaClient.Items.OrderBy(item => item.Id).Select(item => item.Name).ToListAsync());
        }

        var connection = client.Database.GetDbConnection();
        var stateChanges = new List<(ConnectionState Original, ConnectionState Current)>();
        connection.StateChange += (_, args) => stateChanges.Add((args.OriginalState, args.CurrentState));
        if (connection.State != ConnectionState.Closed)
        {
            await connection.CloseAsync();
        }

        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT 1;";
            var reader = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection);
            Assert.True(await reader.ReadAsync());
            reader.Close();
            await reader.DisposeAsync();
        }

        Assert.Equal(ConnectionState.Closed, connection.State);
        await connection.OpenAsync();
        Assert.Equal(3, await client.Items.CountAsync());

        await using (var completedTransaction = await connection.BeginTransactionAsync())
        await using (var staleCommand = connection.CreateCommand())
        {
            staleCommand.Transaction = completedTransaction;
            await completedTransaction.CommitAsync();
            staleCommand.CommandText = "SELECT 1;";
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => staleCommand.ExecuteScalarAsync());
            Assert.Contains("no longer active", exception.Message);
        }

        await connection.CloseAsync();
        Assert.Contains((ConnectionState.Closed, ConnectionState.Open), stateChanges);
        Assert.Contains((ConnectionState.Open, ConnectionState.Closed), stateChanges);

        var serverDiagnostics = await serverContext.Database.GetQuackDiagnosticsAsync();
        Assert.NotEmpty(serverDiagnostics.Servers);
        var diagnostics = await client.Database.GetQuackDiagnosticsAsync();
        Assert.NotNull(diagnostics.Identity);
        Assert.True(diagnostics.RoundTripLatency >= TimeSpan.Zero);
    }

    [ConditionalFact]
    public void UseQuack_is_opt_in_and_does_not_change_native_connection_path()
    {
        using var native = new QuackContext(FileOptions<QuackContext>());
        using var remote = new QuackContext(
            new DbContextOptionsBuilder<QuackContext>()
                .UseQuack("quack:localhost:9494", "secret-token")
                .Options);

        Assert.IsType<DuckDBConnection>(native.Database.GetDbConnection());
        Assert.IsType<QuackDbConnection>(remote.Database.GetDbConnection());
        Assert.False(native.GetService<IDuckDBEngineCapabilities>().SupportsRemoteCommandExecution);
        Assert.False(native.GetService<IDuckDBEngineCapabilities>().SupportsRemoteBulkInsert);
        var remoteCapabilities = remote.GetService<IDuckDBEngineCapabilities>();
        Assert.True(remoteCapabilities.SupportsRemoteCommandExecution);
        Assert.True(remoteCapabilities.SupportsRemoteBulkInsert);
        Assert.True(remoteCapabilities.SupportsSequences);
        Assert.True(remoteCapabilities.SupportsGeneratedColumns);
        Assert.True(remoteCapabilities.SupportsSqlDefaultExpressions);
        Assert.True(remoteCapabilities.SupportsStoreGeneratedValues);
        Assert.True(remoteCapabilities.SupportsSaveChangesBatching);
        Assert.False(remoteCapabilities.SupportsMultipleStatementsPerCommand);
        Assert.False(remoteCapabilities.SupportsTieredStorage);
        Assert.False(remoteCapabilities.SupportsEfMigrations);
        Assert.True(remoteCapabilities.SupportsSchemaManagement);
        Assert.False(remoteCapabilities.SupportsDatabaseDeletion);

        var nativeUpsertPlan = DuckDBUpsertPlanner.GetOrCreate(native, typeof(QuackItem));
        var remoteUpsertPlan = DuckDBUpsertPlanner.GetOrCreate(remote, typeof(QuackItem));
        Assert.Empty(nativeUpsertPlan.ValueAccessors);
        Assert.NotNull(nativeUpsertPlan.WriteRow);
        Assert.Equal(remoteUpsertPlan.InsertColumns.Length, remoteUpsertPlan.ValueAccessors.Length);
        Assert.Null(remoteUpsertPlan.WriteRow);
    }

    [ConditionalFact]
    public void Quack_secret_is_excluded_from_log_fragment_and_debug_information()
    {
        const string token = "do-not-log-this-token";
        var options = new DbContextOptionsBuilder<QuackContext>()
            .UseQuack("quack:localhost:9494", token)
            .Options;
        var extension = options.FindExtension<DuckDBOptionsExtension>()!;
        var debug = new Dictionary<string, string>();

        extension.Info.PopulateDebugInfo(debug);

        Assert.Contains("Quack", extension.Info.LogFragment);
        Assert.DoesNotContain(token, extension.Info.LogFragment);
        Assert.DoesNotContain(token, string.Join(";", debug.Select(pair => $"{pair.Key}={pair.Value}")));
    }

    [ConditionalFact]
    public void Quack_profile_validates_endpoint_token_and_incompatible_profiles_before_opening()
    {
        var endpointOptions = new DbContextOptionsBuilder<QuackContext>()
            .UseQuack("https://localhost:9494", "valid-token")
            .Options;
        var endpointException = Assert.Throws<InvalidOperationException>(
            () => endpointOptions.FindExtension<DuckDBOptionsExtension>()!.Validate(endpointOptions));
        Assert.Contains("quack:", endpointException.Message);

        Assert.Throws<ArgumentException>(() =>
            new DbContextOptionsBuilder<QuackContext>().UseQuack("quack:localhost", "abc"));

        var combined = new DbContextOptionsBuilder<QuackContext>()
            .UseDuckDB("Data Source=:memory:", duckDb =>
            {
                duckDb.UseDuckLake("lake.ducklake");
                duckDb.UseQuack("quack:localhost", "valid-token");
            })
            .Options;
        var combinedException = Assert.Throws<InvalidOperationException>(
            () => combined.FindExtension<DuckDBOptionsExtension>()!.Validate(combined));
        Assert.Contains("cannot be combined", combinedException.Message);

        using var suppliedConnection = new DuckDBConnection("Data Source=:memory:");
        var supplied = new DbContextOptionsBuilder<QuackContext>()
            .UseDuckDB(
                suppliedConnection,
                contextOwnsConnection: false,
                duckDb => duckDb.UseQuack("quack:localhost", "valid-token"))
            .Options;
        var suppliedException = Assert.Throws<InvalidOperationException>(
            () => supplied.FindExtension<DuckDBOptionsExtension>()!.Validate(supplied));
        Assert.Contains("caller-supplied DbConnection", suppliedException.Message);
    }

    [ConditionalFact]
    public void Quack_parameter_expansion_preserves_quoted_text_and_renders_typed_values()
    {
        using var command = new DuckDBCommand();
        command.Parameters.Add(new DuckDBParameter("name", "O'Brien"));
        command.Parameters.Add(new DuckDBParameter("id", 42));
        command.Parameters.Add(new DuckDBParameter("bytes", new byte[] { 0x01, 0xAF }));
        command.Parameters.Add(new DuckDBParameter("ids", new[] { 1, 2, 3 }));

        var sql = QuackSqlTextBuilder.ExpandParameters(
            "SELECT '$name' AS literal, \"$id\" AS identifier -- $bytes\n"
            + "FROM events WHERE name = $name AND id = $id AND payload = $bytes AND id = ANY($ids)",
            command.Parameters);

        Assert.Contains("'$name' AS literal", sql);
        Assert.Contains("\"$id\" AS identifier", sql);
        Assert.Contains("-- $bytes", sql);
        Assert.Contains("name = 'O''Brien'", sql);
        Assert.Contains("id = 42", sql);
        Assert.Contains("payload = from_hex('01AF')", sql);
        Assert.Contains("ANY([1, 2, 3])", sql);
    }

    [ConditionalFact]
    public void Quack_parameter_expansion_preserves_casts_and_dollar_quoted_text()
    {
        using var command = new DuckDBCommand();
        command.Parameters.Add(new DuckDBParameter("value", "{\"ok\":true}"));
        command.Parameters.Add(new DuckDBParameter("JSON", "must-not-replace-cast"));

        var sql = QuackSqlTextBuilder.ExpandParameters(
            "SELECT $value::JSON, $$ $value::JSON $$, $body$ $value::JSON $body$, "
            + "/* outer /* $value */ $value */ $value",
            command.Parameters);

        Assert.Equal(
            "SELECT '{\"ok\":true}'::JSON, $$ $value::JSON $$, $body$ $value::JSON $body$, "
            + "/* outer /* $value */ $value */ '{\"ok\":true}'",
            sql);

        using var enumCommand = new DuckDBCommand();
        enumCommand.Parameters.Add(new DuckDBParameter("value", TestStatus.Enabled));
        Assert.Equal("SELECT 2", QuackSqlTextBuilder.ExpandParameters("SELECT $value", enumCommand.Parameters));
    }

    [ConditionalFact]
    public void Quack_parameter_expansion_preserves_escape_string_literals()
    {
        using var command = new DuckDBCommand();
        command.Parameters.Add(new DuckDBParameter("value", 42));

        var sql = QuackSqlTextBuilder.ExpandParameters(
            """SELECT E'it\'s $value', $value""",
            command.Parameters);

        Assert.Equal("""SELECT E'it\'s $value', 42""", sql);
    }

    [ConditionalFact]
    public void Quack_temporal_literals_are_invariant()
    {
        var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        customCulture.DateTimeFormat.TimeSeparator = ".";
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = customCulture;

            Assert.Equal(
                "TIMESTAMP '2026-08-15 13:14:15.1234567'",
                QuackSqlTextBuilder.GenerateLiteral(new DateTime(2026, 8, 15, 13, 14, 15, 123).AddTicks(4567)));
            Assert.Equal(
                "TIMESTAMPTZ '2026-08-15 13:14:15.0000000+02:30'",
                QuackSqlTextBuilder.GenerateLiteral(
                    new DateTimeOffset(2026, 8, 15, 13, 14, 15, TimeSpan.FromMinutes(150))));
            Assert.Equal(
                "TIME '13:14:15.0000000'",
                QuackSqlTextBuilder.GenerateLiteral(new TimeOnly(13, 14, 15)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [ConditionalFact]
    public void Quack_command_rejects_non_text_command_types_before_connecting()
    {
        using var context = new QuackContext(
            new DbContextOptionsBuilder<QuackContext>()
                .UseQuack("quack:localhost:1", "valid-token")
                .Options);
        using var command = context.Database.GetDbConnection().CreateCommand();

        var exception = Assert.Throws<NotSupportedException>(() => command.CommandType = CommandType.StoredProcedure);

        Assert.Contains("CommandType.Text", exception.Message);
    }

    [ConditionalFact]
    public void Quack_parameter_expansion_rejects_output_parameters()
    {
        using var command = new DuckDBCommand();
        command.Parameters.Add(new DuckDBParameter("value", 42)
        {
            Direction = ParameterDirection.Output
        });

        var exception = Assert.Throws<NotSupportedException>(
            () => QuackSqlTextBuilder.ExpandParameters("SELECT $value", command.Parameters));

        Assert.Contains("input parameters only", exception.Message);
    }

    [ConditionalFact]
    public void Quack_accepts_save_changes_batching_without_changing_the_connection_lifecycle()
    {
        using var context = new QuackContext(
            new DbContextOptionsBuilder<QuackContext>()
                .UseQuack(
                    "quack:localhost:1",
                    "valid-token",
                    duckDBOptionsAction: duckDb => duckDb.EnableBulkInsertBatching())
                .Options);
        Assert.True(context.GetService<IDuckDBEngineCapabilities>().SupportsSaveChangesBatching);
        _ = context.Model;
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [ConditionalFact]
    public void Explicit_remote_replay_rejects_an_in_process_context()
    {
        using var context = new QuackContext(FileOptions<QuackContext>());
        var plan = new DuckDBCommandPlan("SELECT 1", []);

        var exception = Assert.Throws<InvalidOperationException>(
            () => { _ = context.Database.ReplayQuackCommandAsync(plan); });

        Assert.Contains("UseQuack", exception.Message);
    }

    [ConditionalFact]
    public async Task Quack_database_deletion_fails_before_connecting()
    {
        await using var context = new QuackContext(
            new DbContextOptionsBuilder<QuackContext>()
                .UseQuack("quack:localhost:1", "valid-token")
                .Options);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => context.Database.EnsureDeletedAsync());

        Assert.Contains("owned by the server", exception.Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [ConditionalFact]
    public void Quack_database_deletion_fails_before_connecting_synchronously()
    {
        using var context = new QuackContext(
            new DbContextOptionsBuilder<QuackContext>()
                .UseQuack("quack:localhost:1", "valid-token")
                .Options);

        var exception = Assert.Throws<NotSupportedException>(
            () => context.Database.EnsureDeleted());

        Assert.Contains("owned by the server", exception.Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [ConditionalFact]
    public void Quack_sequence_generated_model_is_accepted_during_model_validation()
    {
        using var context = new GeneratedQuackContext(
            new DbContextOptionsBuilder<GeneratedQuackContext>()
                .UseQuack("quack:localhost:1", "valid-token")
                .Options);

        Assert.NotNull(context.Model.FindEntityType(typeof(GeneratedQuackItem)));
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private sealed class QuackContext(DbContextOptions<QuackContext> options) : DbContext(options)
    {
        public DbSet<QuackItem> Items => Set<QuackItem>();
        public DbSet<GeneratedQuackItem> GeneratedItems => Set<GeneratedQuackItem>();
        public DbSet<SchemaGeneratedQuackItem> SchemaGeneratedItems => Set<SchemaGeneratedQuackItem>();
        public DbSet<QuackParent> Parents => Set<QuackParent>();
        public DbSet<QuackChild> Children => Set<QuackChild>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<QuackItem>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedNever();
                entity.Property(item => item.Name).IsRequired().IsConcurrencyToken();
            });

            modelBuilder.Entity<GeneratedQuackItem>(entity =>
            {
                entity.ToTable("generated_items");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("id").UseAutoIncrement();
                entity.Property(item => item.Name).HasColumnName("name").IsRequired();
                entity.Property(item => item.LiteralDefault)
                    .HasColumnName("literal_default")
                    .HasDefaultValue("literal-default");
                entity.Property(item => item.CreatedAt)
                    .HasColumnName("created_at")
                    .HasDefaultValueSql("current_timestamp");
                entity.Property(item => item.CorrelationId)
                    .HasColumnName("correlation_id")
                    .HasDefaultValueSql("uuid()");
                entity.Property(item => item.UpperName)
                    .HasColumnName("upper_name")
                    .HasComputedColumnSql("upper(\"name\")");
            });

            modelBuilder.Entity<SchemaGeneratedQuackItem>(entity =>
            {
                entity.ToTable("generated_schema_items", "generated_schema");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).HasColumnName("id").UseAutoIncrement();
                entity.Property(item => item.Name).HasColumnName("name").IsRequired();
                entity.Property(item => item.CreatedAt)
                    .HasColumnName("created_at")
                    .HasDefaultValueSql("current_timestamp");
            });

            modelBuilder.Entity<QuackParent>(entity =>
            {
                entity.HasKey(value => value.Id);
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.Property(value => value.Name).IsRequired();
                entity.HasMany(value => value.Children)
                    .WithOne()
                    .HasForeignKey(value => value.ParentId);
            });

            modelBuilder.Entity<QuackChild>(entity =>
            {
                entity.HasKey(value => value.Id);
                entity.Property(value => value.Id).ValueGeneratedNever();
                entity.Property(value => value.Value).IsRequired();
            });
        }
    }

    private sealed class QuackItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    private sealed class GeneratedQuackContext(DbContextOptions<GeneratedQuackContext> options) : DbContext(options)
    {
        public DbSet<GeneratedQuackItem> Items => Set<GeneratedQuackItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GeneratedQuackItem>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).UseAutoIncrement();
            });
        }
    }

    private sealed class GeneratedQuackItem
    {
        public long Id { get; set; }
        public string Name { get; set; } = null!;
        public string LiteralDefault { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public Guid CorrelationId { get; set; }
        public string UpperName { get; set; } = null!;
    }

    private sealed class QuackParent
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public List<QuackChild> Children { get; set; } = [];
    }

    private sealed class SchemaGeneratedQuackItem
    {
        public long Id { get; set; }
        public string Name { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
    }

    private sealed class QuackChild
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Value { get; set; } = null!;
    }

    private sealed class SchemaQuackContext(DbContextOptions<SchemaQuackContext> options) : DbContext(options)
    {
        public DbSet<SchemaQuackItem> Items => Set<SchemaQuackItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SchemaQuackItem>(entity =>
            {
                entity.ToTable("schema_items", "analytics");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedNever();
                entity.Property(item => item.Name).IsRequired();
            });
        }
    }

    private sealed class SchemaQuackItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    private enum TestStatus
    {
        Enabled = 2
    }

}

public sealed class QuackIntegrationFactAttribute : FactAttribute
{
    public QuackIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DUCKDB_QUACK_EXTENSION_PATH") is not { Length: > 0 } path
            || !File.Exists(path))
        {
            Skip = "Set DUCKDB_QUACK_EXTENSION_PATH to a Quack extension built for the pinned DuckDB runtime.";
        }
    }
}