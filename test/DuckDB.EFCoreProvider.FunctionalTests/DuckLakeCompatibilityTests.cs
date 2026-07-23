using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using System.Data;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class DuckLakeCompatibilityTests
{
    [Fact]
    public void EF_profile_can_create_query_insert_update_delete_and_detect_concurrency()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var concurrencyInterceptor = new RecordingConcurrencyInterceptor();
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake
                    .CatalogName("analytics")
                    .DataPath(dataPath))
                .AddInterceptors(concurrencyInterceptor)
                .Options;

            var id = Guid.NewGuid();
            using (var context = new DuckLakeContext(options))
            {
                Assert.Equal(
                    "CK_items_quantity_nonnegative",
                    Assert.Single(
                        context.GetService<IDesignTimeModel>().Model
                            .FindEntityType(typeof(DuckLakeItem))!
                            .GetCheckConstraints()).Name);
                Assert.True(context.Database.EnsureCreated());
                context.Database.OpenConnection();
                try
                {
                    Assert.Equal(
                        0L,
                        ExecuteScalarInt64(
                            (DuckDBConnection)context.Database.GetDbConnection(),
                            "SELECT count(*) FROM duckdb_constraints() "
                            + "WHERE database_name = 'analytics' AND table_name = 'items' AND constraint_type != 'NOT NULL';"));
                    Assert.Equal(
                        0L,
                        ExecuteScalarInt64(
                            (DuckDBConnection)context.Database.GetDbConnection(),
                            "SELECT count(*) FROM duckdb_indexes() WHERE database_name = 'analytics' AND table_name = 'items';"));
                }
                finally
                {
                    context.Database.CloseConnection();
                }

                context.Items.Add(new DuckLakeItem { Id = id, Name = "created", Quantity = 1 });
                Assert.Equal(1, context.SaveChanges());
            }

            using (var context = new DuckLakeContext(options))
            {
                Assert.False(context.Database.EnsureCreated());
            }

            using (var context = new DuckLakeContext(options))
            {
                var item = Assert.Single(context.Items);
                Assert.Equal("created", item.Name);
                item.Name = "updated";
                item.Quantity = 2;
                Assert.Equal(1, context.SaveChanges());
            }

            using var staleContext = new DuckLakeContext(options);
            var staleItem = Assert.Single(staleContext.Items);
            using (var deletingContext = new DuckLakeContext(options))
            {
                deletingContext.Remove(Assert.Single(deletingContext.Items));
                Assert.Equal(1, deletingContext.SaveChanges());
            }

            staleItem.Name = "stale update";
            Assert.Throws<DbUpdateConcurrencyException>(() => staleContext.SaveChanges());
            Assert.Equal(1, concurrencyInterceptor.SyncInvocations);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Connectivity_probes_do_not_create_a_missing_catalog()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .Options;

            Assert.False(File.Exists(metadataPath));
            using (var context = new DuckLakeContext(options))
            {
                Assert.False(context.Database.CanConnect());
            }

            Assert.False(File.Exists(metadataPath));
            await using (var context = new DuckLakeContext(options))
            {
                Assert.False(await context.Database.CanConnectAsync());
            }

            Assert.False(File.Exists(metadataPath));
            await using (var context = new DuckLakeContext(options))
            {
                Assert.True(await context.Database.EnsureCreatedAsync());
            }

            Assert.True(File.Exists(metadataPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Already_open_external_connection_is_initialized_reinitialized_and_reused()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            using var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();
            var initializationCount = 0;
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckDB(
                    connection,
                    duckDB =>
                    {
                        duckDB.ConfigureConnection(_ => Interlocked.Increment(ref initializationCount));
                        duckDB.UseDuckLake(
                            metadataPath,
                            duckLake => duckLake.CatalogName("analytics").DataPath(dataPath));
                    })
                .Options;

            using (var context = new DuckLakeContext(options))
            {
                Assert.True(context.Database.EnsureCreated());
                context.Items.Add(new DuckLakeItem
                {
                    Id = Guid.NewGuid(),
                    Name = "external",
                    Quantity = 1
                });
                context.SaveChanges();
                Assert.Equal(1, initializationCount);

                // Closing and reopening the raw connection drops its attachments. The next EF command must
                // notice the new open lifetime and initialize DuckLake again before issuing the query.
                connection.Close();
                connection.Open();
                Assert.Equal("external", Assert.Single(context.Items.AsNoTracking()).Name);
                Assert.Equal(2, initializationCount);
            }

            // A new context over the same still-open connection must select the catalog that the previous
            // context already attached rather than issuing a conflicting second ATTACH.
            using (var context = new DuckLakeContext(options))
            {
                Assert.False(context.Database.EnsureCreated());
                Assert.Equal("external", Assert.Single(context.Items.AsNoTracking()).Name);
                Assert.Equal(3, initializationCount);
            }

            Assert.Equal(ConnectionState.Open, connection.State);
            connection.Close();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Provider_owned_connection_opened_directly_after_validation_is_initialized_async()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var initializationCount = 0;
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(
                    metadataPath,
                    duckLake => duckLake.DataPath(dataPath),
                    duckDB => duckDB.ConfigureConnection(_ => Interlocked.Increment(ref initializationCount)))
                .Options;
            await using var context = new DuckLakeContext(options);

            _ = context.Model;
            var connection = (DuckDBConnection)context.Database.GetDbConnection();
            await connection.OpenAsync();
            Assert.False(File.Exists(metadataPath));

            Assert.True(await context.Database.EnsureCreatedAsync());
            context.Items.Add(new DuckLakeItem
            {
                Id = Guid.NewGuid(),
                Name = "direct async open",
                Quantity = 1
            });
            Assert.Equal(1, await context.SaveChangesAsync());
            Assert.Equal("direct async open", Assert.Single(await context.Items.AsNoTracking().ToListAsync()).Name);
            Assert.Equal(1, initializationCount);
            Assert.True(File.Exists(metadataPath));

            await connection.CloseAsync();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Existing_non_DuckLake_alias_is_rejected_before_EF_uses_the_host_database()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            using var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();
            ExecuteNonQuery(
                connection,
                $"ATTACH '{EscapeSqlLiteral(Path.Combine(root, "native.duckdb"))}' AS analytics;");
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckDB(
                    connection,
                    duckDB => duckDB.UseDuckLake(
                        metadataPath,
                        duckLake => duckLake.CatalogName("analytics").DataPath(dataPath)))
                .Options;

            using var context = new DuckLakeContext(options);
            var exception = Assert.Throws<InvalidOperationException>(() => context.Database.OpenConnection());

            Assert.Contains("already attached as type", exception.Message);
            Assert.Equal("memory", ExecuteScalarString(connection, "SELECT current_database();"));
            Assert.False(File.Exists(metadataPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticMigration_profile_opens_before_EnsureCreated_checks_for_tables(bool async)
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var initialOptions = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .Options;
            using (var context = new DuckLakeContext(initialOptions))
            {
                context.Database.EnsureCreated();
            }

            DuckDBConnection? primaryConnection = null;
            var migrationProfileOpened = false;
            var readOnlyProbeOpenedFirst = false;
            var migrationOptions = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(
                    metadataPath,
                    duckLake => duckLake
                        .DataPath(dataPath)
                        .CreateIfNotExists(false)
                        .AutomaticMigration(),
                    duckDB => duckDB.ConfigureConnection(connection =>
                    {
                        if (ReferenceEquals(connection, primaryConnection))
                        {
                            Volatile.Write(ref migrationProfileOpened, true);
                            return;
                        }

                        if (!Volatile.Read(ref migrationProfileOpened))
                        {
                            Volatile.Write(ref readOnlyProbeOpenedFirst, true);
                            using var command = connection.CreateCommand();
                            command.CommandText = "SELECT * FROM __probe_before_automatic_migration__;";
                            command.ExecuteNonQuery();
                        }
                    }))
                .Options;

            await using var migrationContext = new DuckLakeContext(migrationOptions);
            primaryConnection = (DuckDBConnection)migrationContext.Database.GetDbConnection();
            var created = async
                ? await migrationContext.Database.EnsureCreatedAsync()
                : migrationContext.Database.EnsureCreated();

            Assert.False(created);
            Assert.True(migrationProfileOpened);
            Assert.False(readOnlyProbeOpenedFirst);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Data_path_override_is_connection_scoped_and_does_not_change_catalog_metadata()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);
        var overrideDataPath = Path.Combine(root, "override-data");
        Directory.CreateDirectory(overrideDataPath);

        try
        {
            var initialOptions = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .Options;
            using (var context = new DuckLakeContext(initialOptions))
            {
                context.Database.EnsureCreated();
            }

            var persistedDataPath = ReadPersistedDataPath(metadataPath);
            var overrideOptions = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(
                    metadataPath,
                    duckLake => duckLake
                        .DataPath(overrideDataPath, overrideForCurrentConnection: true)
                        .CreateIfNotExists(false))
                .Options;
            using (var context = new DuckLakeContext(overrideOptions))
            {
                context.Database.OpenConnection();
                Assert.Equal("ducklake", ExecuteScalarString(
                    (DuckDBConnection)context.Database.GetDbConnection(),
                    "SELECT current_database();"));
                context.Database.CloseConnection();
            }

            Assert.Equal(persistedDataPath, ReadPersistedDataPath(metadataPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Attach_command_contains_automatic_migration_and_connection_scoped_data_path_override()
    {
        var command = DuckLakeAttachCommandBuilder.Build(new DuckLakeOptions
        {
            MetadataSource = "catalog.ducklake",
            DataPath = "override-data",
            OverrideDataPath = true,
            AutomaticMigration = true
        });

        Assert.Contains("DATA_PATH 'override-data'", command);
        Assert.Contains("OVERRIDE_DATA_PATH true", command);
        Assert.Contains("AUTOMATIC_MIGRATION", command);
        Assert.EndsWith("USE \"ducklake\";", command);
    }

    [Fact]
    public async Task Async_concurrency_uses_EF_interceptor_pipeline_and_honors_suppression()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var concurrencyInterceptor = new SuppressingConcurrencyInterceptor();
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .AddInterceptors(concurrencyInterceptor)
                .Options;

            await using (var context = new DuckLakeContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                context.Items.Add(new DuckLakeItem { Id = Guid.NewGuid(), Name = "created", Quantity = 1 });
                await context.SaveChangesAsync();
            }

            await using var staleContext = new DuckLakeContext(options);
            var staleItem = Assert.Single(await staleContext.Items.ToListAsync());
            await using (var deletingContext = new DuckLakeContext(options))
            {
                deletingContext.Remove(Assert.Single(await deletingContext.Items.ToListAsync()));
                await deletingContext.SaveChangesAsync();
            }

            staleItem.Name = "suppressed stale update";
            Assert.Equal(1, await staleContext.SaveChangesAsync());
            Assert.Equal(1, concurrencyInterceptor.AsyncInvocations);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Named_secret_profile_runs_secret_initialization_before_attach_and_supports_read_only_reopen()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var writeOptions = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(
                    duckLake => duckLake.UseNamedSecret("application_lake"),
                    duckDB => duckDB.ConfigureConnection(connection =>
                    {
                        using var command = connection.CreateCommand();
                        command.CommandText =
                            $"CREATE OR REPLACE SECRET application_lake (TYPE ducklake, "
                            + $"METADATA_PATH '{EscapeSqlLiteral(metadataPath)}', DATA_PATH '{EscapeSqlLiteral(dataPath)}');";
                        command.ExecuteNonQuery();
                    }))
                .Options;

            using (var context = new DuckLakeContext(writeOptions))
            {
                context.Database.EnsureCreated();
                context.Items.Add(new DuckLakeItem { Id = Guid.NewGuid(), Name = "secret", Quantity = 1 });
                context.SaveChanges();
            }

            var readOptions = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.ReadOnly())
                .Options;
            using var readContext = new DuckLakeContext(readOptions);
            Assert.Equal("secret", Assert.Single(readContext.Items.AsNoTracking()).Name);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Update_concurrency_failure_rolls_back_duplicate_logical_key_matches()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .Options;
            var id = Guid.NewGuid();

            using (var context = new DuckLakeContext(options))
            {
                context.Database.EnsureCreated();
                context.BulkInsert(
                [
                    new DuckLakeItem { Id = id, Name = "first", Quantity = 1 },
                    new DuckLakeItem { Id = id, Name = "second", Quantity = 2 }
                ]);
            }

            using (var context = new DuckLakeContext(options))
            {
                context.Update(new DuckLakeItem { Id = id, Name = "mutated", Quantity = 3 });
                Assert.Throws<DbUpdateConcurrencyException>(() => context.SaveChanges());
            }

            using var verificationContext = new DuckLakeContext(options);
            Assert.Equal(
                ["first", "second"],
                verificationContext.Items.AsNoTracking()
                    .Where(item => item.Id == id)
                    .OrderBy(item => item.Name)
                    .Select(item => item.Name)
                    .ToArray());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Async_delete_concurrency_failure_rolls_back_duplicate_logical_key_matches()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .Options;
            var id = Guid.NewGuid();

            using (var context = new DuckLakeContext(options))
            {
                context.Database.EnsureCreated();
                context.BulkInsert(
                [
                    new DuckLakeItem { Id = id, Name = "first", Quantity = 1 },
                    new DuckLakeItem { Id = id, Name = "second", Quantity = 2 }
                ]);
            }

            await using (var context = new DuckLakeContext(options))
            {
                context.Remove(new DuckLakeItem { Id = id, Name = "detached", Quantity = 0 });
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
            }

            await using var verificationContext = new DuckLakeContext(options);
            Assert.Equal(2, await verificationContext.Items.AsNoTracking().CountAsync(item => item.Id == id));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Destructive_delete_and_migrations_fail_explicitly_for_DuckLake()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .Options;
            using var context = new DuckLakeContext(options);
            context.Database.EnsureCreated();

            var deleteException = Assert.Throws<NotSupportedException>(() => context.Database.EnsureDeleted());
            Assert.Contains("intentionally disabled", deleteException.Message);
            var asyncDeleteException = await Assert.ThrowsAsync<NotSupportedException>(
                () => context.Database.EnsureDeletedAsync());
            Assert.Contains("intentionally disabled", asyncDeleteException.Message);

            var migrationException = Assert.Throws<NotSupportedException>(() => context.Database.Migrate());
            Assert.Contains("migrations are not supported", migrationException.Message);
            var asyncMigrationException = await Assert.ThrowsAsync<NotSupportedException>(
                () => context.Database.MigrateAsync());
            Assert.Contains("migrations are not supported", asyncMigrationException.Message);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Numeric_keys_are_client_assigned_and_auto_increment_is_rejected()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<NumericKeyContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .Options;
            using (var context = new NumericKeyContext(options, useAutoIncrement: false))
            {
                context.Database.EnsureCreated();
                context.Items.Add(new NumericKeyItem { Id = 42, Name = "assigned" });
                context.SaveChanges();
                Assert.Equal(42, Assert.Single(context.Items.AsNoTracking()).Id);
            }

            var generatedOptions = new DbContextOptionsBuilder<ClientGeneratedNumericKeyContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .Options;
            using (var context = new ClientGeneratedNumericKeyContext(generatedOptions))
            {
                var generated = new NumericKeyItem { Name = "generated" };
                context.Items.Add(generated);
                context.SaveChanges();
                Assert.True(generated.Id >= TestIntegerValueGenerator.FirstValue);
            }

            var invalidOptions = new DbContextOptionsBuilder<AutoIncrementContext>()
                .UseDuckLake(metadataPath)
                .Options;
            using var invalidContext = new AutoIncrementContext(invalidOptions);
            var exception = Assert.Throws<InvalidOperationException>(() => _ = invalidContext.Model);
            Assert.Contains("does not support auto-increment", exception.Message);
            Assert.DoesNotContain("DuckLake", exception.Message);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Value_generated_on_update_is_rejected_during_model_validation()
    {
        var root = CreateDirectories(out var metadataPath, out _);

        try
        {
            var options = new DbContextOptionsBuilder<OnUpdateGeneratedContext>()
                .UseDuckLake(metadataPath)
                .Options;
            using var context = new OnUpdateGeneratedContext(options);

            var exception = Assert.Throws<InvalidOperationException>(() => _ = context.Model);
            Assert.Contains("cannot read store-generated values", exception.Message);
            Assert.Contains("OnUpdateGeneratedItem.Revision", exception.Message);
            Assert.DoesNotContain("DuckLake", exception.Message);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void SaveChanges_batching_and_unsafe_profile_values_fail_fast()
    {
        Assert.Throws<ArgumentException>(() =>
            new DbContextOptionsBuilder().UseDuckLake("catalog.ducklake", lake => lake.CatalogName("bad-name")));
        var remoteMetadataException = Assert.Throws<ArgumentException>(() =>
            new DbContextOptionsBuilder().UseDuckLake("postgres:host=metadata;password=secret"));
        Assert.Contains("UseNamedSecret", remoteMetadataException.Message);

        var root = CreateDirectories(out var metadataPath, out var dataPath);
        try
        {
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(
                    metadataPath,
                    duckLake => duckLake.DataPath(dataPath),
                    duckDB => duckDB.EnableBulkInsertBatching())
                .Options;
            using var context = new DuckLakeContext(options);
            context.Database.EnsureCreated();
            context.Items.Add(new DuckLakeItem { Id = Guid.NewGuid(), Name = "batched", Quantity = 1 });

            var exception = Assert.Throws<NotSupportedException>(() => context.SaveChanges());
            Assert.Contains("SaveChanges batching is not supported", exception.Message);
            Assert.DoesNotContain("DuckLake", exception.Message);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Shared_compiled_model_keeps_native_and_DuckLake_upsert_plans_isolated()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var modelOptions = new DbContextOptionsBuilder<SharedModelContext>()
                .UseDuckDB("Data Source=:memory:")
                .Options;
            using var modelContext = new SharedModelContext(modelOptions);
            var sharedModel = modelContext.Model;

            var nativeOptions = new DbContextOptionsBuilder<SharedModelContext>()
                .UseDuckDB($"Data Source={Path.Combine(root, "native.duckdb")}")
                .UseModel(sharedModel)
                .Options;
            using (var nativeContext = new SharedModelContext(nativeOptions))
            {
                nativeContext.Database.EnsureCreated();
                Assert.Equal(1, nativeContext.Upsert([new SharedUpsertItem { Id = 1, Name = "native" }]));
            }

            var duckLakeOptions = new DbContextOptionsBuilder<SharedModelContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .UseModel(sharedModel)
                .Options;
            using var duckLakeContext = new SharedModelContext(duckLakeOptions);
            duckLakeContext.Database.EnsureCreated();
            Assert.Equal(1, duckLakeContext.Upsert([new SharedUpsertItem { Id = 1, Name = "ducklake" }]));
            Assert.Equal("ducklake", Assert.Single(duckLakeContext.Items.AsNoTracking()).Name);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void EF_profile_supports_appender_bulk_insert_and_merge_upsert()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .Options;
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            var thirdId = Guid.NewGuid();

            using var context = new DuckLakeContext(options);
            context.Database.EnsureCreated();

            Assert.Equal(2, context.BulkInsert(
            [
                new DuckLakeItem { Id = firstId, Name = "first", Quantity = 1 },
                new DuckLakeItem { Id = secondId, Name = "second", Quantity = 2 }
            ]));

            Assert.Equal(2, context.Upsert(
            [
                new DuckLakeItem { Id = firstId, Name = "merged", Quantity = 10 },
                new DuckLakeItem { Id = thirdId, Name = "third", Quantity = 3 }
            ]));

            var rows = context.Items.OrderBy(item => item.Name).ToList();
            Assert.Equal(3, rows.Count);
            Assert.Equal(10, rows.Single(item => item.Id == firstId).Quantity);
            Assert.Equal("third", rows.Single(item => item.Id == thirdId).Name);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Async_profile_path_supports_create_tracked_write_bulk_insert_upsert_and_query()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .Options;
            var id = Guid.NewGuid();
            await using var context = new DuckLakeContext(options);

            Assert.True(await context.Database.EnsureCreatedAsync());
            context.Items.Add(new DuckLakeItem { Id = id, Name = "tracked", Quantity = 1 });
            Assert.Equal(1, await context.SaveChangesAsync());
            Assert.Equal(1, await context.BulkInsertAsync(
            [
                new DuckLakeItem { Id = Guid.NewGuid(), Name = "bulk", Quantity = 2 }
            ]));
            Assert.Equal(1, await context.UpsertAsync(
            [
                new DuckLakeItem { Id = id, Name = "merged", Quantity = 3 }
            ]));

            var items = await context.Items.AsNoTracking().OrderBy(item => item.Name).ToListAsync();
            Assert.Equal(2, items.Count);
            Assert.Equal(3, items.Single(item => item.Id == id).Quantity);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Multi_command_SaveChanges_rolls_back_as_one_transaction()
    {
        var root = CreateDirectories(out var metadataPath, out var dataPath);

        try
        {
            var options = new DbContextOptionsBuilder<DuckLakeContext>()
                .UseDuckLake(metadataPath, duckLake => duckLake.DataPath(dataPath))
                .Options;
            using (var context = new DuckLakeContext(options))
            {
                context.Database.EnsureCreated();
                context.Items.AddRange(
                    new DuckLakeItem { Id = Guid.NewGuid(), Name = "valid", Quantity = 1 },
                    new DuckLakeItem { Id = Guid.NewGuid(), Name = null!, Quantity = 2 });
                Assert.Throws<DbUpdateException>(() => context.SaveChanges());
            }

            using var verificationContext = new DuckLakeContext(options);
            Assert.Empty(verificationContext.Items.AsNoTracking());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DuckDB_NET_can_read_write_merge_and_append_to_local_DuckLake()
    {
        // The apostrophe exercises DuckLake SQL-literal escaping for both metadata and data paths.
        var root = Path.Combine(Path.GetTempPath(), $"ducklake-provider-'quoted-{Guid.NewGuid():N}");
        var metadataDirectory = Path.Combine(root, "metadata");
        var dataDirectory = Path.Combine(root, "data");
        Directory.CreateDirectory(metadataDirectory);
        Directory.CreateDirectory(dataDirectory);

        try
        {
            var metadataPath = EscapeSqlLiteral(Path.Combine(metadataDirectory, "catalog.ducklake"));
            var dataPath = EscapeSqlLiteral(dataDirectory);

            using var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();

            ExecuteNonQuery(connection, "INSTALL ducklake; LOAD ducklake;");
            ExecuteNonQuery(
                connection,
                $"ATTACH 'ducklake:{metadataPath}' AS analytics (DATA_PATH '{dataPath}'); USE analytics;");
            ExecuteNonQuery(connection, "CREATE TABLE items (id UUID NOT NULL, name VARCHAR NOT NULL, quantity INTEGER NOT NULL);");

            var firstId = Guid.NewGuid();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "INSERT INTO items VALUES ($id, $name, $quantity);";
                command.Parameters.Add(new DuckDBParameter("id", firstId));
                command.Parameters.Add(new DuckDBParameter("name", "first"));
                command.Parameters.Add(new DuckDBParameter("quantity", 1));
                Assert.Equal(1, command.ExecuteNonQuery());
            }

            using (var transaction = connection.BeginTransaction(IsolationLevel.Snapshot))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE items SET quantity = 2 WHERE id = $id;";
                command.Parameters.Add(new DuckDBParameter("id", firstId));
                Assert.Equal(1, command.ExecuteNonQuery());
                transaction.Commit();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                                      MERGE INTO items
                                      USING (SELECT $id::UUID AS id, $name::VARCHAR AS name, $quantity::INTEGER AS quantity) AS incoming
                                      ON items.id = incoming.id
                                      WHEN MATCHED THEN UPDATE SET name = incoming.name, quantity = incoming.quantity
                                      WHEN NOT MATCHED THEN INSERT;
                                      """;
                command.Parameters.Add(new DuckDBParameter("id", firstId));
                command.Parameters.Add(new DuckDBParameter("name", "merged"));
                command.Parameters.Add(new DuckDBParameter("quantity", 3));
                command.ExecuteNonQuery();
            }

            var appendedId = Guid.NewGuid();
            using (var appender = connection.CreateAppender("main", "items"))
            {
                var row = appender.CreateRow();
                row.AppendValue(appendedId);
                row.AppendValue("appended");
                row.AppendValue(4);
                row.EndRow();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT count(*), CAST(sum(quantity) AS BIGINT) FROM items;";
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(7L, reader.GetInt64(1));
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void ExecuteNonQuery(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long ExecuteScalarInt64(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string ExecuteScalarString(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    private static string ReadPersistedDataPath(string metadataPath)
    {
        using var connection = new DuckDBConnection($"Data Source={metadataPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM ducklake_metadata WHERE key = 'data_path';";
        return Convert.ToString(command.ExecuteScalar())!;
    }

    private static string EscapeSqlLiteral(string value)
        => value.Replace("'", "''");

    private static string CreateDirectories(out string metadataPath, out string dataPath)
    {
        // Provider-owned ATTACH must safely quote developer-supplied paths.
        var root = Path.Combine(Path.GetTempPath(), $"ducklake-provider-'quoted-{Guid.NewGuid():N}");
        var metadataDirectory = Path.Combine(root, "metadata");
        dataPath = Path.Combine(root, "data");
        Directory.CreateDirectory(metadataDirectory);
        Directory.CreateDirectory(dataPath);
        metadataPath = Path.Combine(metadataDirectory, "catalog.ducklake");
        return root;
    }

    private sealed class DuckLakeContext(DbContextOptions<DuckLakeContext> options) : DbContext(options)
    {
        public DbSet<DuckLakeItem> Items => Set<DuckLakeItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DuckLakeItem>(entity =>
            {
                entity.ToTable(
                    "items",
                    table => table.HasCheckConstraint("CK_items_quantity_nonnegative", "\"Quantity\" >= 0"));
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Name).IsRequired();
            });
        }
    }

    private sealed class DuckLakeItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public int Quantity { get; set; }
    }

    private sealed class SharedModelContext(DbContextOptions<SharedModelContext> options) : DbContext(options)
    {
        public DbSet<SharedUpsertItem> Items => Set<SharedUpsertItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SharedUpsertItem>(entity =>
            {
                entity.ToTable("shared_upsert_items");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Id).ValueGeneratedNever();
                entity.Property(item => item.Name).IsRequired();
            });
        }
    }

    private sealed class SharedUpsertItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    private sealed class NumericKeyContext(
        DbContextOptions<NumericKeyContext> options,
        bool useAutoIncrement) : DbContext(options)
    {
        public DbSet<NumericKeyItem> Items => Set<NumericKeyItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var property = modelBuilder.Entity<NumericKeyItem>().Property(item => item.Id);
            if (useAutoIncrement)
            {
                property.UseAutoIncrement();
            }
        }
    }

    private sealed class AutoIncrementContext(DbContextOptions<AutoIncrementContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<NumericKeyItem>().Property(item => item.Id).UseAutoIncrement();
    }

    private sealed class ClientGeneratedNumericKeyContext(
        DbContextOptions<ClientGeneratedNumericKeyContext> options) : DbContext(options)
    {
        public DbSet<NumericKeyItem> Items => Set<NumericKeyItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<NumericKeyItem>()
                .Property(item => item.Id)
                .HasValueGenerator<TestIntegerValueGenerator>();
    }

    private sealed class OnUpdateGeneratedContext(
        DbContextOptions<OnUpdateGeneratedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OnUpdateGeneratedItem>().Property(item => item.Id).ValueGeneratedNever();
            modelBuilder.Entity<OnUpdateGeneratedItem>().Property(item => item.Revision).ValueGeneratedOnUpdate();
        }
    }

    private sealed class OnUpdateGeneratedItem
    {
        public int Id { get; set; }
        public int Revision { get; set; }
    }

    private sealed class TestIntegerValueGenerator : ValueGenerator<int>
    {
        public const int FirstValue = 1_000;
        private int _current = FirstValue - 1;

        public override bool GeneratesTemporaryValues => false;

        public override int Next(EntityEntry entry)
            => Interlocked.Increment(ref _current);
    }

    private sealed class NumericKeyItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    private sealed class RecordingConcurrencyInterceptor : SaveChangesInterceptor
    {
        public int SyncInvocations { get; private set; }

        public override InterceptionResult ThrowingConcurrencyException(
            ConcurrencyExceptionEventData eventData,
            InterceptionResult result)
        {
            SyncInvocations++;
            return result;
        }
    }

    private sealed class SuppressingConcurrencyInterceptor : SaveChangesInterceptor
    {
        public int AsyncInvocations { get; private set; }

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            AsyncInvocations++;
            return ValueTask.FromResult(InterceptionResult.Suppress());
        }
    }
}
