using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Infrastructure;
using DuckDB.EFCoreProvider.Infrastructure.Internal;
using DuckDB.EFCoreProvider.Storage.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     Covers <c>StorageCompatibilityVersion(...)</c> and the DuckLake <c>DuckLakeVersion(...)</c> option: validation
///     at options time, rendering through the attach command builders, and the format written to new files.
/// </summary>
public sealed class StorageVersionOptionsTests : DuckDBTestBase
{
    private const string PinnedVersion = "v1.5.0";

    [Theory]
    [InlineData("v1.5.0", "v1.5.0")]
    [InlineData("V1.5.0", "v1.5.0")]
    [InlineData("v0.10.2", "v0.10.2")]
    [InlineData("latest", "latest")]
    [InlineData("LATEST", "latest")]
    public void Storage_version_accepts_release_versions_and_latest_in_canonical_form(string version, string expected)
    {
        var options = NewOptions()
            .UseDuckDB($"DataSource={DbPath}", duckdb => duckdb.StorageCompatibilityVersion(version))
            .Options;

        Assert.Equal(expected, options.FindExtension<DuckDBOptionsExtension>()!.StorageCompatibilityVersion);
    }

    [Theory]
    [InlineData("1.5.0")]
    [InlineData("v1.5")]
    [InlineData("v1.5.0'; DROP TABLE x; --")]
    [InlineData("newest")]
    public void Storage_version_rejects_malformed_versions_when_configured(string version)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new DbContextOptionsBuilder().UseDuckDB(
                $"DataSource={DbPath}",
                duckdb => duckdb.StorageCompatibilityVersion(version)));

        Assert.Equal("version", exception.ParamName);
    }

    [ConditionalFact]
    public void Storage_version_cannot_be_combined_with_Quack()
    {
        var options = NewOptions()
            .UseDuckDB(duckdb => duckdb
                .UseQuack("quack:localhost", "token")
                .StorageCompatibilityVersion(PinnedVersion))
            .Options;

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(options));

        Assert.Contains("UseQuack", exception.Message);
    }

    [ConditionalFact]
    public void Storage_version_cannot_be_combined_with_DuckLake()
    {
        var options = NewOptions()
            .UseDuckLake("catalog.ducklake", duckDBOptionsAction: duckdb => duckdb.StorageCompatibilityVersion(PinnedVersion))
            .Options;

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(options));

        Assert.Contains("DuckLakeVersion", exception.Message);
    }

    [ConditionalFact]
    public void Storage_version_must_agree_with_the_connection_string()
    {
        var options = NewOptions()
            .UseDuckDB(
                $"DataSource={DbPath};storage_compatibility_version=v1.4.0",
                duckdb => duckdb.StorageCompatibilityVersion(PinnedVersion))
            .Options;

        var validation = Assert.Throws<InvalidOperationException>(() => Validate(options));
        using var context = new VersionContext(options);
        var connection = Assert.Throws<InvalidOperationException>(() => context.Database.GetDbConnection());

        Assert.Contains("conflicts with", validation.Message);
        Assert.Contains("conflicts with", connection.Message);
    }

    [ConditionalFact]
    public void Storage_version_in_the_connection_string_that_agrees_is_accepted()
    {
        var options = NewOptions()
            .UseDuckDB(
                $"DataSource={DbPath};storage_compatibility_version={PinnedVersion}",
                duckdb => duckdb.StorageCompatibilityVersion(PinnedVersion))
            .Options;

        Validate(options);
        using var context = new VersionContext(options);
        context.Database.EnsureCreated();

        Assert.Contains($"storage_version={PinnedVersion}", StorageTags(context));
    }

    [ConditionalFact]
    public void Storage_version_requires_a_caller_supplied_connection_to_carry_it()
    {
        using var connection = new DuckDBConnection($"DataSource={DbPath}");
        var options = NewOptions()
            .UseDuckDB(connection, duckdb => duckdb.StorageCompatibilityVersion(PinnedVersion))
            .Options;

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(options));

        Assert.Contains("caller-supplied DbConnection", exception.Message);
    }

    [ConditionalFact]
    public void Native_database_file_is_created_in_the_pinned_storage_format()
    {
        using var context = new VersionContext(NewOptions()
            .UseDuckDB($"DataSource={DbPath}", duckdb => duckdb.StorageCompatibilityVersion(PinnedVersion))
            .Options);

        context.Database.EnsureCreated();
        context.Items.Add(new VersionItem { Id = 1 });
        context.SaveChanges();

        Assert.Contains($"storage_version={PinnedVersion}", StorageTags(context));
        Assert.Equal(PinnedVersion, StorageCompatibilitySetting(context));
        Assert.Contains(
            $"{DuckDBStorageVersions.StorageCompatibilityVersionKey}={PinnedVersion}",
            context.Database.GetDbConnection().ConnectionString,
            StringComparison.OrdinalIgnoreCase);
    }

    [ConditionalFact]
    public void Storage_version_fails_when_the_file_is_already_open_with_another_version()
    {
        // DuckDB.NET shares one database instance per data source and reads startup configuration only when it
        // creates the instance, so the provider must detect that the setting was not applied.
        using var existing = new DuckDBConnection($"DataSource={DbPath}");
        existing.Open();
        using var context = new VersionContext(NewOptions()
            .UseDuckDB($"DataSource={DbPath}", duckdb => duckdb.StorageCompatibilityVersion(PinnedVersion))
            .Options);

        var exception = Assert.Throws<InvalidOperationException>(() => context.Database.OpenConnection());

        Assert.Contains("already opened in this process", exception.Message);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [ConditionalFact]
    public void Storage_version_is_carried_to_read_only_connections()
    {
        using (var writer = new VersionContext(NewOptions()
                   .UseDuckDB($"DataSource={DbPath}", duckdb => duckdb.StorageCompatibilityVersion(PinnedVersion))
                   .Options))
        {
            writer.Database.EnsureCreated();
            var readOnly = writer.GetService<IDuckDBRelationalConnection>().CreateReadOnlyConnection();
            using (readOnly)
            {
                Assert.Contains(
                    $"{DuckDBStorageVersions.StorageCompatibilityVersionKey}={PinnedVersion}",
                    readOnly.DbConnection.ConnectionString,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [ConditionalTheory]
    [InlineData("v1.4.0")]
    [InlineData("v1.0.0")]
    public void Encrypted_database_rejects_storage_versions_that_lower_the_encryption_scheme(string version)
    {
        var options = NewOptions()
            .UseEncryptedDuckDB(
                Path.Combine(Path.GetTempPath(), "never-created.duckdb"),
                () => "k",
                duckDBOptionsAction: duckdb => duckdb.StorageCompatibilityVersion(version))
            .Options;

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(options));

        Assert.Contains("legacy encryption scheme", exception.Message);
    }

    [ConditionalTheory]
    [InlineData("v1.5.0")]
    [InlineData("latest")]
    public void Encrypted_database_accepts_storage_versions_that_keep_the_encryption_scheme(string version)
        => Validate(NewOptions()
            .UseEncryptedDuckDB(
                Path.Combine(Path.GetTempPath(), "never-created.duckdb"),
                () => "k",
                duckDBOptionsAction: duckdb => duckdb.StorageCompatibilityVersion(version))
            .Options);

    [ConditionalFact]
    public void Encrypted_attachment_renders_the_storage_version_before_access_mode()
    {
        var options = new DuckDBEncryptedDatabaseOptions
        {
            Path = "/tmp/secure.duckdb",
            KeyProvider = () => "k",
            CatalogName = "secure",
            IsReadOnly = true
        };

        Assert.Equal(
            "ATTACH IF NOT EXISTS '/tmp/secure.duckdb' AS \"secure\" (ENCRYPTION_KEY 'k', STORAGE_VERSION 'v1.5.0', READ_ONLY);",
            DuckDBEncryptedAttachCommandBuilder.BuildAttachment(options, "k", PinnedVersion));
        Assert.Equal(
            "ATTACH IF NOT EXISTS '/tmp/secure.duckdb' AS \"secure\" (ENCRYPTION_KEY 'k', READ_ONLY);",
            DuckDBEncryptedAttachCommandBuilder.BuildAttachment(options, "k"));
    }

    [ConditionalFact]
    public void Encrypted_database_file_is_created_in_the_pinned_storage_format()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ddbenc_storage_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "pinned.duckdb");
            using var context = new VersionContext(NewOptions()
                .UseEncryptedDuckDB(
                    path,
                    () => "correct horse battery staple",
                    encrypted => encrypted.CatalogName("pinned_storage"),
                    // v1.5.0 is the oldest format that keeps the current encryption scheme, and the default for
                    // new encrypted files, so the file stays readable by DuckDB 1.5 on every engine.
                    duckdb => duckdb.StorageCompatibilityVersion(PinnedVersion))
                .Options);

            context.Database.EnsureCreated();

            Assert.Contains($"storage_version={PinnedVersion}", StorageTags(context, "pinned_storage"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.1-dev1")]
    [InlineData("1.1-DEV1")]
    [InlineData("2.0")]
    public void DuckLake_version_accepts_writable_specifications(string version)
    {
        var profile = DuckLakeProfile(duckLake => duckLake.DuckLakeVersion(version));
        var expected = version.ToLowerInvariant();

        Assert.Equal(expected, profile.DuckLakeVersion);
        Assert.Contains($"DUCKLAKE_VERSION '{expected}'", DuckLakeAttachCommandBuilder.BuildAttachment(profile));
    }

    [Theory]
    [InlineData("0.4")]
    [InlineData("1")]
    [InlineData("v1.0")]
    [InlineData("1.0'; DROP TABLE x; --")]
    public void DuckLake_version_rejects_unwritable_or_malformed_specifications(string version)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => DuckLakeProfile(duckLake => duckLake.DuckLakeVersion(version)));

        Assert.Equal("version", exception.ParamName);
    }

    [Fact]
    public void DuckLake_version_is_omitted_unless_configured()
        => Assert.DoesNotContain(
            "DUCKLAKE_VERSION",
            DuckLakeAttachCommandBuilder.BuildAttachment(DuckLakeProfile(_ => { })));

    [Fact]
    public void DuckLake_version_applies_to_writable_additional_catalogs_only()
    {
        var profile = DuckLakeProfile(duckLake => duckLake
            .AlsoAttach("before_writable", "before.ducklake", readOnly: false)
            .DuckLakeVersion("1.0")
            .AlsoAttach("after_writable", "after.ducklake", readOnly: false)
            .AlsoAttach("reference", "reference.ducklake"));

        var command = DuckLakeAttachCommandBuilder.Build(profile);

        Assert.Equal(["1.0", "1.0", null], profile.AdditionalCatalogs.Select(catalog => catalog.DuckLakeVersion));
        Assert.Contains("AS \"before_writable\" (DUCKLAKE_VERSION '1.0')", command);
        Assert.Contains("AS \"after_writable\" (DUCKLAKE_VERSION '1.0')", command);
        Assert.Contains("AS \"reference\" (CREATE_IF_NOT_EXISTS false, READ_ONLY)", command);
    }

    [Fact]
    public void Read_only_and_historical_DuckLake_profiles_drop_the_version()
    {
        Assert.Null(DuckLakeProfile(duckLake => duckLake.DuckLakeVersion("1.0").ReadOnly()).DuckLakeVersion);
        Assert.Null(DuckLakeProfile(duckLake => duckLake.DuckLakeVersion("1.0").AsOfSnapshot(1)).DuckLakeVersion);
        Assert.Null(DuckLakeProfile(duckLake => duckLake.DuckLakeVersion("1.0").AsOfTimestamp(DateTimeOffset.UnixEpoch)).DuckLakeVersion);
        Assert.Null(DuckLakeProfile(duckLake => duckLake.DuckLakeVersion("1.0")).AsReadOnly().DuckLakeVersion);
    }

    [ConditionalFact]
    public void Read_only_DuckLake_profile_with_a_version_is_rejected_at_validation()
    {
        var extension = NewOptions()
            .UseDuckLake("catalog.ducklake")
            .Options
            .FindExtension<DuckDBOptionsExtension>()!;
        var options = NewOptions()
            .UseDuckLake("catalog.ducklake")
            .Options
            .WithExtension(extension.WithDuckLakeOptions(extension.DuckLakeOptions! with
            {
                IsReadOnly = true,
                CreateIfNotExists = false,
                DuckLakeVersion = "1.0"
            }));

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(options));

        Assert.Contains("read-only DuckLake profile cannot select DuckLake specification", exception.Message);
    }

    private static DuckLakeOptions DuckLakeProfile(Action<DuckLakeDbContextOptionsBuilder> configure)
        => new DbContextOptionsBuilder()
            .UseDuckLake("catalog.ducklake", configure)
            .Options
            .FindExtension<DuckDBOptionsExtension>()!
            .DuckLakeOptions!;

    private static void Validate(DbContextOptions options)
        => options.FindExtension<DuckDBOptionsExtension>()!.Validate(options);

    private static DbContextOptionsBuilder<VersionContext> NewOptions()
        => new DbContextOptionsBuilder<VersionContext>()
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

    private static string StorageTags(DbContext context, string? databaseName = null)
    {
        databaseName ??= context.Database.SqlQuery<string>($"SELECT current_database() AS \"Value\"").AsEnumerable().Single();
        return context.Database
            .SqlQuery<string>(
                $"SELECT CAST(tags AS VARCHAR) AS \"Value\" FROM duckdb_databases() WHERE database_name = {databaseName}")
            .AsEnumerable()
            .Single();
    }

    private static string StorageCompatibilitySetting(DbContext context)
        => context.Database
            .SqlQueryRaw<string>("SELECT current_setting('storage_compatibility_version') AS \"Value\"")
            .AsEnumerable()
            .Single();

    private sealed class VersionContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<VersionItem> Items => Set<VersionItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<VersionItem>().Property(item => item.Id).ValueGeneratedNever();
    }

    private sealed class VersionItem
    {
        public int Id { get; set; }
    }
}
