using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using System.Globalization;
using Xunit;

namespace Microsoft.EntityFrameworkCore;

public sealed class DuckLakeExternalIntegrationTests
{
    private const string CatalogName = "external_lake";
    private const string ProfileSecretName = "ducklake_external_profile";

    [DuckLakeExternalFact]
    public async Task PostgreSQL_metadata_and_MinIO_data_round_trip_through_named_secret_profile()
    {
        var configuration = ExternalConfiguration.Load();
        var options = CreateOptions(configuration, readOnly: false);
        var id = Guid.NewGuid();

        await using (var context = new ExternalDuckLakeContext(options))
        {
            Assert.True(await context.Database.EnsureCreatedAsync());
            context.Items.Add(new ExternalDuckLakeItem { Id = id, Name = "external", Quantity = 7 });
            Assert.Equal(1, await context.SaveChangesAsync());

            await context.Database.ExecuteSqlRawAsync($"CALL ducklake_flush_inlined_data('{CatalogName}');");
            await context.Database.OpenConnectionAsync();
            try
            {
                var connection = (DuckDBConnection)context.Database.GetDbConnection();
                Assert.Equal(1, ExecuteScalarInt64(
                    connection,
                    $"""
                    SELECT count(*)
                    FROM duckdb_databases()
                    WHERE database_name = '{CatalogName}' AND type = 'ducklake';
                    """));
                // DuckLake's internal metadata connection is not part of DuckDB's public catalog.
                // The read-only reopen below verifies PostgreSQL-backed metadata persistence through supported behavior.
                Assert.True(ExecuteScalarInt64(
                    connection,
                    $"SELECT count(*) FROM glob('{EscapeSqlLiteral(configuration.DataPath)}/**/*.parquet');") > 0);
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }
        }

        var readOnlyOptions = CreateOptions(configuration, readOnly: true);
        await using (var context = new ExternalDuckLakeContext(readOnlyOptions))
        {
            Assert.True(await context.Database.CanConnectAsync());
            var item = Assert.Single(await context.Items.AsNoTracking().ToListAsync());
            Assert.Equal(id, item.Id);
            Assert.Equal("external", item.Name);
            Assert.Equal(7, item.Quantity);
        }
    }

    private static DbContextOptions<ExternalDuckLakeContext> CreateOptions(
        ExternalConfiguration configuration,
        bool readOnly)
        => new DbContextOptionsBuilder<ExternalDuckLakeContext>()
            .UseDuckLake(
                duckLake =>
                {
                    duckLake
                        .UseNamedSecret(ProfileSecretName)
                        .CatalogName(CatalogName)
                        .CreateIfNotExists(!readOnly);
                    if (readOnly)
                    {
                        duckLake.ReadOnly();
                    }
                },
                duckDB => duckDB
                    .LoadExtension("httpfs")
                    .LoadExtension("postgres")
                    .ConfigureConnection(connection => InitializeSecrets(connection, configuration)))
            .Options;

    private static void InitializeSecrets(DuckDBConnection connection, ExternalConfiguration configuration)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $$"""
            SET httpfs_client_implementation = 'httplib';
            CREATE OR REPLACE SECRET ducklake_external_s3 (
                TYPE s3,
                PROVIDER config,
                KEY_ID '{{EscapeSqlLiteral(configuration.S3KeyId)}}',
                SECRET '{{EscapeSqlLiteral(configuration.S3Secret)}}',
                REGION '{{EscapeSqlLiteral(configuration.S3Region)}}',
                ENDPOINT '{{EscapeSqlLiteral(configuration.S3Endpoint)}}',
                URL_STYLE 'path',
                USE_SSL false
            );
            CREATE OR REPLACE SECRET ducklake_external_postgres (
                TYPE postgres,
                HOST '{{EscapeSqlLiteral(configuration.PostgresHost)}}',
                PORT {{configuration.PostgresPort.ToString(CultureInfo.InvariantCulture)}},
                DATABASE '{{EscapeSqlLiteral(configuration.PostgresDatabase)}}',
                USER '{{EscapeSqlLiteral(configuration.PostgresUser)}}',
                PASSWORD '{{EscapeSqlLiteral(configuration.PostgresPassword)}}'
            );
            CREATE OR REPLACE SECRET {{ProfileSecretName}} (
                TYPE ducklake,
                METADATA_PATH '',
                DATA_PATH '{{EscapeSqlLiteral(configuration.DataPath)}}',
                METADATA_PARAMETERS MAP {'TYPE': 'postgres', 'SECRET': 'ducklake_external_postgres'}
            );
            """;
        command.ExecuteNonQuery();
    }

    private static long ExecuteScalarInt64(DuckDBConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string EscapeSqlLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record ExternalConfiguration(
        string PostgresHost,
        int PostgresPort,
        string PostgresDatabase,
        string PostgresUser,
        string PostgresPassword,
        string S3Endpoint,
        string S3KeyId,
        string S3Secret,
        string S3Region,
        string DataPath)
    {
        public static ExternalConfiguration Load()
        {
            var bucket = GetRequiredEnvironmentVariable("DUCKLAKE_S3_BUCKET");
            return new ExternalConfiguration(
                GetRequiredEnvironmentVariable("DUCKLAKE_POSTGRES_HOST"),
                int.Parse(GetRequiredEnvironmentVariable("DUCKLAKE_POSTGRES_PORT"), CultureInfo.InvariantCulture),
                GetRequiredEnvironmentVariable("DUCKLAKE_POSTGRES_DATABASE"),
                GetRequiredEnvironmentVariable("DUCKLAKE_POSTGRES_USER"),
                GetRequiredEnvironmentVariable("DUCKLAKE_POSTGRES_PASSWORD"),
                GetRequiredEnvironmentVariable("DUCKLAKE_S3_ENDPOINT"),
                GetRequiredEnvironmentVariable("DUCKLAKE_S3_KEY_ID"),
                GetRequiredEnvironmentVariable("DUCKLAKE_S3_SECRET"),
                GetRequiredEnvironmentVariable("DUCKLAKE_S3_REGION"),
                $"s3://{bucket}/efcore-provider");
        }

        private static string GetRequiredEnvironmentVariable(string name)
            => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException(
                    $"Environment variable '{name}' is required when DUCKLAKE_EXTERNAL_TESTS=1.");
    }

    private sealed class ExternalDuckLakeContext(DbContextOptions<ExternalDuckLakeContext> options) : DbContext(options)
    {
        public DbSet<ExternalDuckLakeItem> Items => Set<ExternalDuckLakeItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<ExternalDuckLakeItem>(entity =>
            {
                entity.ToTable("external_items");
                entity.HasKey(item => item.Id);
                entity.Property(item => item.Name).IsRequired();
            });
    }

    private sealed class ExternalDuckLakeItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public int Quantity { get; set; }
    }
}

public sealed class DuckLakeExternalFactAttribute : FactAttribute
{
    public DuckLakeExternalFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("DUCKLAKE_EXTERNAL_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Run scripts/test-ducklake-external.sh to start PostgreSQL and MinIO and enable this test.";
        }
    }
}
