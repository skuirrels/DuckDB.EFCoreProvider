using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Tests for <c>UseEncryptedDatabase</c>: DuckDB accepts an encryption key only as an <c>ATTACH</c>
///     parameter, so the provider hosts the encrypted file on a shared in-memory database and attaches it as the
///     context's default catalog.
/// </summary>
public sealed class EncryptedDatabaseTests : IDisposable
{
    // One internal service provider for the whole class. EF caps how many it creates for a process, and a
    // profile that needs its own provider shape spends one of those slots for every test class that uses it.
    private static readonly IServiceProvider ServiceProvider = new ServiceCollection()
        .AddEntityFrameworkDuckDB()
        .BuildServiceProvider();

    private const string Key = "correct horse battery staple";
    private const string Secret = "SENTINEL-123-45-6789";

    // Short strings occur by chance in ciphertext, so the plaintext assertions use long distinctive values.
    private const string PersonName = "Ada-Lovelace-SENTINEL-NAME";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"ddbenc_{Guid.NewGuid():N}");

    private string DbPath => Path.Combine(_directory, "secure.duckdb");

    public EncryptedDatabaseTests()
        => Directory.CreateDirectory(_directory);

    private DbContextOptions<SecretContext> Options(
        Action<DuckDBEncryptedDatabaseOptionsBuilder>? encrypted = null,
        string? path = null,
        Func<string>? keyProvider = null)
        => new DbContextOptionsBuilder<SecretContext>()
            .UseInternalServiceProvider(ServiceProvider)
            .UseEncryptedDuckDB(path ?? DbPath, keyProvider ?? (() => Key), encrypted)
            .Options;

    [ConditionalFact]
    public void Entities_round_trip_and_the_file_holds_no_plaintext()
    {
        using (var context = new SecretContext(Options()))
        {
            context.Database.EnsureCreated();
            context.People.Add(new Person { Id = 1, Name = PersonName, Ssn = Secret });
            context.SaveChanges();
        }

        using (var context = new SecretContext(Options()))
        {
            Assert.Equal(PersonName, context.People.Single().Name);
        }

        var contents = Encoding.ASCII.GetString(File.ReadAllBytes(DbPath));
        Assert.DoesNotContain(Secret, contents, StringComparison.Ordinal);
        Assert.DoesNotContain(PersonName, contents, StringComparison.Ordinal);
        Assert.DoesNotContain("People", contents, StringComparison.Ordinal);
    }

    [ConditionalFact]
    public async Task The_async_connection_path_attaches_creates_and_deletes_the_encrypted_database()
    {
        await using var context = new SecretContext(Options(encrypted => encrypted.CatalogName("async_probe")));

        Assert.True(await context.Database.EnsureCreatedAsync());
        context.People.Add(new Person { Id = 1, Name = PersonName, Ssn = Secret });
        await context.SaveChangesAsync();

        Assert.Equal(PersonName, (await context.People.SingleAsync()).Name);
        Assert.Equal("async_probe", CurrentCatalog(context));
        Assert.True(File.Exists(DbPath));

        Assert.True(await context.Database.EnsureDeletedAsync());
        Assert.False(File.Exists(DbPath));
    }

    [ConditionalFact]
    public void A_caller_opened_connection_still_attaches_the_encrypted_database()
    {
        using var context = new SecretContext(Options(encrypted => encrypted.CatalogName("caller_opened")));

        // Opening the underlying connection first makes EF's Open() short-circuit the provider's own
        // OpenDbConnection. Without initialization for that case the context would silently create its tables
        // in the unencrypted in-memory host and never write the encrypted file at all.
        context.Database.GetDbConnection().Open();
        context.Database.EnsureCreated();
        context.People.Add(new Person { Id = 1, Name = PersonName, Ssn = Secret });
        context.SaveChanges();

        Assert.Equal("caller_opened", CurrentCatalog(context));
        Assert.True(File.Exists(DbPath));
        Assert.Equal(
            1L,
            Scalar<long>(
                context,
                "SELECT count(*) FROM duckdb_tables() WHERE database_name = 'caller_opened' AND table_name = 'People'"));
        Assert.Equal(
            0L,
            Scalar<long>(context, "SELECT count(*) FROM duckdb_tables() WHERE database_name = 'memory'"));
    }

    [ConditionalFact]
    public async Task A_caller_opened_connection_still_attaches_the_encrypted_database_asynchronously()
    {
        await using var context = new SecretContext(Options(encrypted => encrypted.CatalogName("caller_opened_async")));

        await context.Database.GetDbConnection().OpenAsync();
        await context.Database.EnsureCreatedAsync();
        context.People.Add(new Person { Id = 1, Name = PersonName, Ssn = Secret });
        await context.SaveChangesAsync();

        Assert.Equal("caller_opened_async", CurrentCatalog(context));
        Assert.True(File.Exists(DbPath));
    }

    [ConditionalFact]
    public void Initialization_runs_once_for_a_caller_opened_connection()
    {
        var initializations = 0;
        var options = new DbContextOptionsBuilder<SecretContext>()
            .UseInternalServiceProvider(ServiceProvider)
            .UseEncryptedDuckDB(
                DbPath,
                () => Key,
                encrypted => encrypted.CatalogName("initialized_once"),
                duckdb => duckdb.ConfigureConnection(_ => initializations++))
            .Options;

        using var context = new SecretContext(options);
        context.Database.GetDbConnection().Open();
        context.Database.OpenConnection();
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        // The caller's initializer must not be replayed for each operation on the connection it opened.
        Assert.Equal(1, initializations);
    }

    [ConditionalFact]
    public void Concurrent_first_opens_all_attach_the_encrypted_database()
    {
        const int threadCount = 8;
        var timeout = TimeSpan.FromSeconds(30);
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var catalogs = new System.Collections.Concurrent.ConcurrentBag<string>();

        // Every participant blocks, so these are dedicated threads rather than pooled ones: starving the
        // thread pool would stall the rest of the test run.
        using var opening = new Barrier(threadCount);
        using var opened = new Barrier(threadCount);

        var workers = Enumerable.Range(0, threadCount)
            .Select(_ => new Thread(() =>
            {
                SecretContext? context = null;
                try
                {
                    context = new SecretContext(Options(encrypted => encrypted.CatalogName("concurrent")));

                    // Line every thread up on the check-then-attach window a shared host instance exposes.
                    opening.SignalAndWait(timeout);
                    context.Database.OpenConnection();
                    catalogs.Add(CurrentCatalog(context));
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                finally
                {
                    // Hold every connection open until all of them are, so one attachment serves them all.
                    opened.SignalAndWait(timeout);
                    context?.Dispose();
                }
            }) { IsBackground = true })
            .ToArray();

        foreach (var worker in workers)
        {
            worker.Start();
        }

        foreach (var worker in workers)
        {
            Assert.True(worker.Join(timeout), "A concurrent attachment did not complete.");
        }

        Assert.Empty(failures.Select(failure => failure.Message));
        Assert.Equal(threadCount, catalogs.Count);
        Assert.All(catalogs, catalog => Assert.Equal("concurrent", catalog));
    }

    [ConditionalFact]
    public void EnsureDeleted_under_an_open_connection_reattaches_before_recreating()
    {
        using var context = new SecretContext(Options(encrypted => encrypted.CatalogName("delete_reattach")));
        context.Database.EnsureCreated();

        // The outer open scope keeps the physical connection open across the delete, so the StateChange reset
        // never fires and only an explicit invalidation makes the next open re-attach.
        context.Database.OpenConnection();
        Assert.True(context.Database.EnsureDeleted());
        Assert.False(File.Exists(DbPath));

        Assert.True(context.Database.EnsureCreated());
        context.People.Add(new Person { Id = 1, Name = PersonName, Ssn = Secret });
        context.SaveChanges();

        Assert.Equal("delete_reattach", CurrentCatalog(context));
        Assert.True(File.Exists(DbPath));
        Assert.Equal(
            0L,
            Scalar<long>(context, "SELECT count(*) FROM duckdb_tables() WHERE database_name = 'memory'"));
    }

    [ConditionalFact]
    public void A_wrong_key_cannot_reuse_an_existing_attachment()
    {
        using var holder = new SecretContext(Options(encrypted => encrypted.CatalogName("key_guard")));
        holder.Database.EnsureCreated();
        holder.Database.OpenConnection();

        // Same alias and path: the intruder's ATTACH would be a no-op, so only the fingerprint check stands
        // between a wrong key and the already-decrypted data.
        using (var sameKey = new SecretContext(Options(encrypted => encrypted.CatalogName("key_guard"))))
        {
            Assert.Equal(0, sameKey.People.Count());
        }

        using var wrongKey = new SecretContext(Options(
            encrypted => encrypted.CatalogName("key_guard"),
            keyProvider: () => "not the key"));

        var exception = Assert.Throws<InvalidOperationException>(() => wrongKey.Database.OpenConnection());
        Assert.Contains("different encryption key", exception.Message, StringComparison.Ordinal);
    }

    [ConditionalFact]
    public void A_tiered_store_over_a_caller_opened_connection_uses_the_encrypted_catalog()
    {
        var archivePath = Path.Combine(_directory, "archive");
        Directory.CreateDirectory(archivePath);
        var options = new DbContextOptionsBuilder<TieredSecretContext>()
            .UseInternalServiceProvider(ServiceProvider)
            .UseEncryptedDuckDB(DbPath, () => Key, encrypted => encrypted.CatalogName("tiered_alias"))
            .Options;

        using (var seed = new TieredSecretContext(options, archivePath))
        {
            seed.Database.EnsureCreated();
        }

        using var context = new TieredSecretContext(options, archivePath);
        context.Database.GetDbConnection().Open();
        context.Database.EnsureTieredStoresCreated();

        Assert.Equal("tiered_alias", CurrentCatalog(context));
        Assert.Equal(
            "tiered_alias",
            Scalar<string>(
                context,
                "SELECT database_name FROM duckdb_tables() WHERE table_name = '__duckdb_tier_control'"));
    }

    [ConditionalFact]
    public void A_symlinked_database_file_matches_its_attachment()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // creating file symlinks on Windows requires elevation
        }

        var realPath = Path.Combine(_directory, "real.duckdb");
        using (var seed = new SecretContext(Options(encrypted => encrypted.CatalogName("sym_seed"), realPath)))
        {
            seed.Database.EnsureCreated();
        }

        var linkPath = Path.Combine(_directory, "current.duckdb");
        File.CreateSymbolicLink(linkPath, realPath);

        // DuckDB reports the resolved target path, so the post-attach verification must resolve the link in
        // the file component too — not only in the directories above it.
        using var first = new SecretContext(Options(encrypted => encrypted.CatalogName("sym_alias"), linkPath));
        first.Database.OpenConnection();
        using var second = new SecretContext(Options(encrypted => encrypted.CatalogName("sym_alias"), linkPath));
        second.Database.OpenConnection();

        Assert.Equal("sym_alias", CurrentCatalog(second));
    }

    [ConditionalFact]
    public void A_plain_in_memory_data_source_applied_after_configuration_is_rejected()
    {
        var builder = new DbContextOptionsBuilder<SecretContext>().UseInternalServiceProvider(ServiceProvider);
        builder.UseDuckDB(
            DuckDBConnectionStringBuilder.InMemorySharedConnectionString,
            duckdb => duckdb.UseEncryptedDatabase(DbPath, () => Key, encrypted => encrypted.CatalogName("late_plain")));
        builder.UseDuckDB("Data Source=:memory:");

        // An unshared in-memory host gives every connection its own instance, each attaching the encrypted
        // file independently — two writable attachments of one file.
        var exception = Assert.Throws<InvalidOperationException>(() => new SecretContext(builder.Options));
        Assert.Contains("shared in-memory host", exception.Message, StringComparison.Ordinal);
    }

    [ConditionalFact]
    public void The_file_cannot_be_opened_without_the_key()
    {
        using (var context = new SecretContext(Options()))
        {
            context.Database.EnsureCreated();
        }

        using var connection = new DuckDBConnection($"Data Source={DbPath}");

        Assert.Throws<DuckDBException>(() => connection.Open());
    }

    [ConditionalFact]
    public void A_wrong_key_fails_to_attach()
    {
        using (var context = new SecretContext(Options()))
        {
            context.Database.EnsureCreated();
        }

        // A separate alias so the attachment is retried instead of reusing the one the host instance holds.
        using var context2 = new SecretContext(Options(
            encrypted => encrypted.CatalogName("wrong_key_probe"),
            keyProvider: () => "not the key"));

        var exception = Assert.Throws<DuckDBException>(() => context2.Database.OpenConnection());
        Assert.Contains("encryption key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [ConditionalFact]
    public void Tables_and_migration_history_are_created_inside_the_encrypted_catalog()
    {
        using var context = new SecretContext(Options());
        context.Database.EnsureCreated();
        context.GetService<Migrations.IHistoryRepository>().CreateIfNotExists();

        Assert.Equal("secure", CurrentCatalog(context));
        Assert.True(Scalar<bool>(context, "SELECT encrypted FROM duckdb_databases() WHERE database_name = 'secure'"));
        Assert.Equal(
            "GCM",
            Scalar<string>(context, "SELECT cipher FROM duckdb_databases() WHERE database_name = 'secure'"));
        Assert.Equal(
            2L,
            Scalar<long>(
                context,
                "SELECT count(*) FROM duckdb_tables() WHERE database_name = 'secure' "
                + "AND table_name IN ('People', '__EFMigrationsHistory')"));
    }

    [ConditionalFact]
    public void The_migrations_history_and_lock_tables_are_scoped_to_the_encrypted_catalog()
    {
        using var context = new SecretContext(Options(encrypted => encrypted.CatalogName("history_probe")));
        context.Database.EnsureCreated();

        var repository = context.GetService<Migrations.IHistoryRepository>();
        Assert.False(repository.Exists());

        repository.CreateIfNotExists();
        Assert.True(repository.Exists());
        Assert.Empty(repository.GetAppliedMigrations());
        repository.AcquireDatabaseLock().Dispose();

        Assert.Equal(
            2L,
            Scalar<long>(
                context,
                "SELECT count(*) FROM duckdb_tables() WHERE database_name = 'history_probe' "
                + "AND table_name IN ('__EFMigrationsHistory', '__EFMigrationsLock')"));
    }

    [ConditionalFact]
    public void The_catalog_alias_defaults_to_the_file_name_and_can_be_overridden()
    {
        using (var context = new SecretContext(Options()))
        {
            context.Database.EnsureCreated();
            Assert.Equal("secure", CurrentCatalog(context));
        }

        using var renamed = new SecretContext(Options(encrypted => encrypted.CatalogName("vault")));
        renamed.Database.EnsureCreated();

        Assert.Equal("vault", CurrentCatalog(renamed));
    }

    [ConditionalFact]
    public void Temporary_files_are_encrypted_by_default_and_the_setting_is_opt_out()
    {
        using var context = new SecretContext(Options());
        context.Database.OpenConnection();

        Assert.True(Scalar<bool>(context, "SELECT current_setting('temp_file_encryption')"));

        // temp_file_encryption is a global DuckDB setting, so opting out is asserted on the configuration
        // rather than on the shared instance another context may already have switched on.
        var options = Options(encrypted => encrypted.EncryptTemporaryFiles(false));
        var extension = options.FindExtension<DuckDBOptionsExtension>()!;

        Assert.False(extension.EncryptedDatabase!.EncryptTemporaryFiles);
    }

    [ConditionalFact]
    public void A_read_only_attachment_rejects_writes()
    {
        using (var context = new SecretContext(Options(encrypted => encrypted.CatalogName("read_only_source"))))
        {
            context.Database.EnsureCreated();
        }

        using var readOnly = new SecretContext(Options(encrypted => encrypted
            .CatalogName("read_only_target")
            .ReadOnly()));
        readOnly.People.Add(new Person { Id = 2, Name = "Grace", Ssn = Secret });

        var exception = Assert.Throws<DbUpdateException>(() => readOnly.SaveChanges());
        Assert.Contains("read-only mode", exception.InnerException!.Message, StringComparison.Ordinal);
    }

    [ConditionalFact]
    public void A_read_only_clone_connection_is_refused_rather_than_failing_at_open()
    {
        using var context = new SecretContext(Options(encrypted => encrypted.CatalogName("clone_probe")));
        context.Database.EnsureCreated();
        context.Database.OpenConnection();

        // The clone shares the host instance, so it can neither re-attach the database read-only while the
        // writable attachment is live nor constrain the writer. Refusing at the call site keeps that from
        // surfacing as an attachment failure deep in the next Open().
        var connection = (IDuckDBRelationalConnection)context.GetService<IRelationalConnection>();
        var exception = Assert.Throws<NotSupportedException>(connection.CreateReadOnlyConnection);

        Assert.Contains("independently enforced read-only connection", exception.Message, StringComparison.Ordinal);
        Assert.Contains("encrypted.ReadOnly()", exception.Message, StringComparison.Ordinal);
    }

    [ConditionalFact]
    public void An_echoed_key_literal_is_redacted_and_the_rest_of_the_message_is_kept()
    {
        const string key = "s3cr3t-key-material";
        var failure = new InvalidOperationException(
            $"Parser Error: syntax error at ATTACH '/var/lib/app/secure.duckdb' AS \"secure\" "
            + $"(ENCRYPTION_KEY {DuckDBEncryptedAttachCommandBuilder.KeyLiteral(key)})");

        var sanitized = DuckDBRelationalConnection.SanitizeEncryptedDatabaseFailure(failure, key);

        Assert.DoesNotContain(key, sanitized.Message, StringComparison.Ordinal);
        Assert.Contains("ENCRYPTION_KEY '***'", sanitized.Message, StringComparison.Ordinal);
        Assert.Contains("/var/lib/app/secure.duckdb", sanitized.Message, StringComparison.Ordinal);
        Assert.Null(sanitized.InnerException);
    }

    [ConditionalFact]
    public void A_key_that_only_occurs_incidentally_suppresses_the_message_instead_of_corrupting_it()
    {
        // "duckdb" is a substring of the path here. Replacing every occurrence of the key's characters would
        // rewrite the path to "/var/lib/app/secure.***" and destroy the only useful part of the message.
        const string key = "duckdb";
        var failure = new InvalidOperationException(
            "Binder Error: Unique file handle conflict: the database file \"/var/lib/app/secure.duckdb\" "
            + "is already attached by database \"secure\"");

        var sanitized = DuckDBRelationalConnection.SanitizeEncryptedDatabaseFailure(failure, key);

        Assert.DoesNotContain(key, sanitized.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secure.***", sanitized.Message, StringComparison.Ordinal);
        Assert.Contains("suppressed", sanitized.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), sanitized.Message, StringComparison.Ordinal);
    }

    [ConditionalFact]
    public void A_failure_without_the_key_is_propagated_unchanged()
    {
        var failure = new InvalidOperationException("Invalid Input Error: Wrong encryption key used to open the database file");

        Assert.Same(
            failure,
            DuckDBRelationalConnection.SanitizeEncryptedDatabaseFailure(failure, "s3cr3t-key-material"));
    }

    [ConditionalFact]
    public void The_key_reaches_the_attachment_as_one_escaped_literal()
    {
        Assert.Equal("'s3cr3t-key-material'", DuckDBEncryptedAttachCommandBuilder.KeyLiteral("s3cr3t-key-material"));
        Assert.Equal("'it''s'", DuckDBEncryptedAttachCommandBuilder.KeyLiteral("it's"));
        Assert.Contains(
            "ATTACH IF NOT EXISTS",
            DuckDBEncryptedAttachCommandBuilder.BuildAttachment(
                new DuckDBEncryptedDatabaseOptions { Path = "/tmp/x.duckdb", KeyProvider = () => "k" },
                "k"),
            StringComparison.Ordinal);
    }

    [ConditionalFact]
    public void An_alias_already_attached_to_another_file_is_rejected()
    {
        // The shared in-memory host instance lives as long as a connection is open, so the first context holds
        // one for the lifetime of the test: that is the case where two contexts really do share an alias.
        using var context = new SecretContext(Options(encrypted => encrypted.CatalogName("shared_alias")));
        context.Database.EnsureCreated();
        context.Database.OpenConnection();

        var otherPath = Path.Combine(_directory, "other.duckdb");
        using var conflicting = new SecretContext(Options(
            encrypted => encrypted.CatalogName("shared_alias"),
            otherPath));

        var exception = Assert.Throws<InvalidOperationException>(() => conflicting.Database.OpenConnection());
        Assert.Contains("already attached to a different database file", exception.Message, StringComparison.Ordinal);
    }

    [ConditionalFact]
    public void An_unencrypted_database_under_the_same_alias_is_rejected()
    {
        var plainPath = Path.Combine(_directory, "plain.duckdb");
        using var host = new DuckDBConnection(DuckDBConnectionStringBuilder.InMemorySharedConnectionString);
        host.Open();
        using (var command = host.CreateCommand())
        {
            command.CommandText = $"ATTACH '{plainPath}' AS plain_alias;";
            command.ExecuteNonQuery();
        }

        using var context = new SecretContext(Options(
            encrypted => encrypted.CatalogName("plain_alias"),
            plainPath));

        var exception = Assert.Throws<InvalidOperationException>(() => context.Database.OpenConnection());
        Assert.Contains("is not encrypted", exception.Message, StringComparison.Ordinal);
    }

    [ConditionalFact]
    public void EnsureDeleted_detaches_and_removes_the_encrypted_file()
    {
        using var context = new SecretContext(Options(encrypted => encrypted.CatalogName("deletable")));
        Assert.True(context.Database.EnsureCreated());
        Assert.True(File.Exists(DbPath));

        Assert.True(context.Database.EnsureDeleted());

        Assert.False(File.Exists(DbPath));
        Assert.False(File.Exists(DbPath + ".wal"));
    }

    [ConditionalFact]
    public void A_file_data_source_is_rejected_because_the_host_database_is_in_memory()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new DbContextOptionsBuilder<SecretContext>()
                .UseDuckDB($"Data Source={DbPath}", duckdb => duckdb.UseEncryptedDatabase(DbPath, () => Key)));

        Assert.Contains("cannot be combined with the file data source", exception.Message, StringComparison.Ordinal);
    }

    [ConditionalFact]
    public void Combining_the_encrypted_database_with_DuckLake_is_rejected()
    {
        var options = new DbContextOptionsBuilder<SecretContext>()
            .UseInternalServiceProvider(ServiceProvider)
            .UseDuckDB(
                DuckDBConnectionStringBuilder.InMemorySharedConnectionString,
                duckdb => duckdb
                    .UseEncryptedDatabase(DbPath, () => Key)
                    .UseDuckLake(Path.Combine(_directory, "lake.duckdb")))
            .Options;

        var exception = Assert.Throws<InvalidOperationException>(() => new SecretContext(options));
        Assert.Contains("UseDuckLake and UseEncryptedDatabase cannot be combined", exception.Message, StringComparison.Ordinal);
    }

    [ConditionalFact]
    public void The_key_is_resolved_per_attachment_and_never_stored_in_the_options()
    {
        var resolutions = 0;
        var options = Options(keyProvider: () =>
        {
            resolutions++;
            return Key;
        });

        using (var context = new SecretContext(options))
        {
            context.Database.EnsureCreated();
        }

        // The host instance is released when the last connection closes, so each attachment resolves the key
        // again rather than the provider caching it.
        Assert.True(resolutions > 0);

        var extension = options.FindExtension<DuckDBOptionsExtension>()!;
        Assert.Contains("EncryptedDatabase", extension.Info.LogFragment, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, extension.Info.LogFragment, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, options.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [ConditionalFact]
    public void An_empty_key_fails_the_attachment_instead_of_opening_the_database()
    {
        using var context = new SecretContext(Options(
            encrypted => encrypted.CatalogName("empty_key_probe"),
            keyProvider: () => string.Empty));

        var exception = Assert.Throws<InvalidOperationException>(() => context.Database.OpenConnection());
        Assert.Contains("returned no key", exception.Message, StringComparison.Ordinal);
    }

    [ConditionalFact]
    public void Built_in_catalog_names_are_rejected()
        => Assert.Throws<ArgumentException>(
            () => Options(encrypted => encrypted.CatalogName("temp")));

    [ConditionalFact]
    public void The_catalog_alias_is_derived_from_the_database_file_name()
    {
        Assert.Equal("secure", DuckDBEncryptedDatabaseOptions.DeriveCatalogName("/var/lib/app/secure.duckdb"));
        Assert.Equal("my_app", DuckDBEncryptedDatabaseOptions.DeriveCatalogName("my-app.db"));
        Assert.Equal("_2024_data", DuckDBEncryptedDatabaseOptions.DeriveCatalogName("2024 data.duckdb"));
        Assert.Equal("temp_db", DuckDBEncryptedDatabaseOptions.DeriveCatalogName("temp.duckdb"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string CurrentCatalog(DbContext context)
        => Scalar<string>(context, "SELECT current_catalog()");

    private static T Scalar<T>(DbContext context, string sql)
    {
        context.Database.OpenConnection();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return (T)command.ExecuteScalar()!;
    }

    private sealed class Person
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Ssn { get; set; } = string.Empty;
    }

    private sealed class SecretContext(DbContextOptions<SecretContext> options) : DbContext(options)
    {
        public DbSet<Person> People => Set<Person>();
    }

    private sealed class TieredSecretContext(DbContextOptions<TieredSecretContext> options, string archivePath)
        : DbContext(options)
    {
        public DbSet<Reading> Readings => Set<Reading>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ToTieredStore<Reading>(reading => reading.EffectiveAt, archivePath)
                .WithTieredView("readings_all");
    }

    private sealed class Reading
    {
        public int Id { get; set; }
        public DateTime EffectiveAt { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
