using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.ValueGeneration.Internal;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

    [DuckLakeExternalFact]
    public async Task HiLo_writers_on_a_shared_PostgreSQL_catalog_reserve_disjoint_blocks()
    {
        var configuration = ExternalConfiguration.Load();
        var metadataSchema = $"hilo_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<HiLoLakeContext>()
            .UseDuckLake(
                duckLake => duckLake.UseNamedSecret("ducklake_external_hilo").CatalogName("hilo_lake"),
                duckDB => duckDB
                    .LoadExtension("httpfs")
                    .LoadExtension("postgres")
                    .ConfigureConnection(connection =>
                    {
                        InitializeSecrets(connection, configuration);
                        using var command = connection.CreateCommand();
                        command.CommandText =
                            $$"""
                            CREATE OR REPLACE SECRET ducklake_external_hilo (
                                TYPE ducklake,
                                METADATA_PATH '',
                                DATA_PATH '{{EscapeSqlLiteral(configuration.DataPath)}}/{{metadataSchema}}',
                                METADATA_SCHEMA '{{metadataSchema}}',
                                METADATA_PARAMETERS MAP {'TYPE': 'postgres', 'SECRET': 'ducklake_external_postgres'}
                            );
                            """;
                        command.ExecuteNonQuery();
                    }))
            .Options;

        await using (var context = new HiLoLakeContext(options))
        {
            Assert.True(await context.Database.EnsureCreatedAsync());
        }

        // Each writer has its own DuckDB instance attached to the shared catalog, so their counter updates
        // conflict in DuckLake and the losers retry with the committed value.
        const int writers = 6;
        const int reservationsPerWriter = 4;
        const int blockSize = 3;
        var lows = await Task.WhenAll(Enumerable.Range(0, writers).Select(_ => Task.Run(async () =>
        {
            await using var context = new HiLoLakeContext(options);
            var allocator = context.GetService<DuckDBHiLoBlockAllocator>();
            var counter = DuckDBHiLoCounter.For(
                context.Model.FindEntityType(typeof(HiLoLakeItem))!.FindProperty(nameof(HiLoLakeItem.Id))!);
            var reserved = new List<long>();
            for (var i = 0; i < reservationsPerWriter; i++)
            {
                reserved.Add(await allocator.AllocateAsync(counter, blockSize, CancellationToken.None));
            }

            return reserved;
        })));

        var values = lows.SelectMany(reserved => reserved)
            .SelectMany(low => Enumerable.Range(0, blockSize).Select(offset => low + offset))
            .ToArray();
        Assert.Equal(writers * reservationsPerWriter * blockSize, values.Distinct().Count());

        await using (var context = new HiLoLakeContext(options))
        {
            context.AddRange(Enumerable.Range(0, 5).Select(i => new HiLoLakeItem { Name = $"item {i}" }));
            Assert.Equal(5, await context.SaveChangesAsync());
            var keys = await context.Items.Select(item => item.Id).ToListAsync();
            Assert.Equal(5, keys.Distinct().Count());
            Assert.All(keys, key => Assert.DoesNotContain(key, values));
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

    private sealed class HiLoLakeContext(DbContextOptions<HiLoLakeContext> options) : DbContext(options)
    {
        public DbSet<HiLoLakeItem> Items => Set<HiLoLakeItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.UseHiLo(blockSize: 2);
            modelBuilder.Entity<HiLoLakeItem>().ToTable("hilo_items");
        }
    }

    private sealed class HiLoLakeItem
    {
        public long Id { get; set; }
        public string Name { get; set; } = null!;
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