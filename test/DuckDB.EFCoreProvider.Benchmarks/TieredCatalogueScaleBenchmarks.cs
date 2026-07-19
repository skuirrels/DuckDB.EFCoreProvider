using BenchmarkDotNet.Attributes;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;

namespace DuckDB.EFCoreProvider.Benchmarks;

/// <summary>
///     Parameterised provider-neutral scale fixture for immutable generations and exact file catalogues. Each
///     leading partition value/lifecycle period produces one root and one child Parquet partition.
/// </summary>
[MemoryDiagnoser]
public class TieredCatalogueScaleBenchmarks
{
    private static readonly DateTime FirstPeriod = new(2020, 1, 1);
    private string _root = null!;
    private string _dbPath = null!;
    private string _archivePath = null!;
    private ScaleObjectStoreOptions? _objectStore;
    private long _viewSqlCharacters;
    private long _catalogueFiles;

    /// <summary>Number of distinct values in the leading exact partition dimension.</summary>
    [ParamsSource(nameof(PartitionCardinalities))]
    public int PartitionCardinality { get; set; }

    /// <summary>Number of monthly lifecycle periods represented in the cold generation.</summary>
    [ParamsSource(nameof(PeriodCounts))]
    public int Periods { get; set; }

    /// <summary>Default or environment-supplied exact-partition cardinalities.</summary>
    public IEnumerable<int> PartitionCardinalities => DimensionValues("DUCKDB_TIER_SCALE_PARTITIONS", [16, 128]);

    /// <summary>Default or environment-supplied lifecycle-period counts.</summary>
    public IEnumerable<int> PeriodCounts => DimensionValues("DUCKDB_TIER_SCALE_PERIODS", [12, 60]);

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "duckdb-tier-scale-" + Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_root, "scale.duckdb");
        _objectStore = ScaleObjectStoreOptions.FromEnvironment();
        _archivePath = _objectStore?.CreateArchivePath() ?? Path.Combine(_root, "archive");
        Directory.CreateDirectory(_root);
        using var context = CreateContext();
        context.Database.EnsureCreated();
        var rows = checked(PartitionCardinality * Periods);
#pragma warning disable EF1002, EF1003 // Benchmark dimensions are controlled integer parameters, not external SQL.
        context.Database.ExecuteSqlRaw(
            $"INSERT INTO scale_roots (\"Id\", \"ScopeKey\", \"EffectiveAt\", \"Payload\") "
            + $"SELECT i + 1, i % {PartitionCardinality}, DATE '2020-01-01' "
            + $"+ to_months(CAST(floor(i::DOUBLE / {PartitionCardinality}) AS INTEGER)), repeat('x', 64) "
            + $"FROM range(0, {rows}) AS source(i);");
        context.Database.ExecuteSqlRaw(
            "INSERT INTO scale_children (\"Id\", \"RootId\", \"Value\") "
            + $"SELECT i + 1, i + 1, i % 100 FROM range(0, {rows}) AS source(i);");
#pragma warning restore EF1002, EF1003
        context.Database.ArchiveTier<ScaleRoot>(FirstPeriod.AddMonths(Periods));
        _viewSqlCharacters = context.Database.SqlQueryRaw<long>(
                "SELECT COALESCE(sum(length(sql)), 0) AS \"Value\" FROM duckdb_views() "
                + "WHERE view_name IN ('scale_roots_tiered', 'scale_children_tiered')")
            .Single();
        _catalogueFiles = context.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM __duckdb_tier_generation_files "
                + "WHERE control_key = 'scale_roots'")
            .Single();
        var pruningPlan = Explain(context, ScopedQuery(context), analyze: true);
        var filesRead = Regex.Matches(
                pruningPlan,
                @"Total Files Read:[^\d]*(\d+)",
                RegexOptions.Singleline | RegexOptions.CultureInvariant)
            .Select(match => long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Sum();

        Console.WriteLine(
            $"Tier scale fixture: storage={(_objectStore is null ? "local" : "s3")}, "
            + $"partitions={PartitionCardinality}, periods={Periods}, nodes=2, "
            + $"catalogue_files={_catalogueFiles}, generated_view_sql_chars={_viewSqlCharacters}, "
            + $"scoped_total_files_read={filesRead}, archive_path={_archivePath}");
    }

    /// <summary>Measures restart plus exact-catalogue view regeneration.</summary>
    [Benchmark]
    public long RestartAndRegenerateViews()
    {
        using var context = CreateContext();
        context.Database.EnsureTieredStoresCreated();
        return context.Database.SqlQueryRaw<long>(
                "SELECT COALESCE(sum(length(sql)), 0) AS \"Value\" FROM duckdb_views() "
                + "WHERE view_name IN ('scale_roots_tiered', 'scale_children_tiered')")
            .Single();
    }

    /// <summary>Measures first bind/plan/execution with leading-partition and lifecycle predicates.</summary>
    [Benchmark]
    public int FirstScopedQuery()
    {
        using var context = CreateContext();
        return ScopedQuery(context).Count();
    }

    /// <summary>Measures DuckDB's scoped-query bind/plan path without executing the query.</summary>
    [Benchmark]
    public int BindPlanScopedQuery()
    {
        using var context = CreateContext();
        return Explain(context, ScopedQuery(context), analyze: false).Length;
    }

    /// <summary>Measures a global query without a predicate on the leading partition dimension.</summary>
    [Benchmark]
    public int FirstUnscopedQuery()
    {
        using var context = CreateContext();
        return context.RootHistory.Count();
    }

    /// <summary>Measures allocation and time needed to plan against the complete exact input catalogue.</summary>
    [Benchmark]
    public string PlanRetentionAgainstExactCatalogue()
    {
        using var context = CreateContext();
        return context.Database.PlanArchiveRetention<ScaleRoot>(
                new TierArchiveRetentionOptions { RetainFrom = FirstPeriod.AddMonths(Periods / 2) })
            .Fingerprint;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort benchmark cleanup.
        }
    }

    private ScaleContext CreateContext() => new(_dbPath, _archivePath, _objectStore);

    private static IEnumerable<int> DimensionValues(string variable, int[] defaults)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return defaults;
        }

        var values = configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.Parse(value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .ToArray();
        if (values.Length == 0 || values.Any(value => value <= 0))
        {
            throw new InvalidOperationException($"{variable} must contain one or more positive integers.");
        }

        return values;
    }

    private IQueryable<ScaleRootHistory> ScopedQuery(ScaleContext context)
    {
        var from = FirstPeriod.AddMonths(Periods / 2);
        var to = from.AddMonths(1);
        var scope = PartitionCardinality / 2;
        return context.RootHistory.Where(root =>
            root.ScopeKey == scope && root.EffectiveAt >= from && root.EffectiveAt < to);
    }

    private static string Explain<T>(ScaleContext context, IQueryable<T> query, bool analyze)
    {
        using var command = query.CreateDbCommand();
        command.CommandText = (analyze ? "EXPLAIN ANALYZE " : "EXPLAIN ") + command.CommandText;
        var openedHere = command.Connection!.State != ConnectionState.Open;
        if (openedHere)
        {
            context.Database.OpenConnection();
        }

        try
        {
            using var reader = command.ExecuteReader();
            var plan = new StringBuilder();
            while (reader.Read())
            {
                for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
                {
                    plan.AppendLine(reader.GetValue(ordinal)?.ToString());
                }
            }

            return plan.ToString();
        }
        finally
        {
            if (openedHere)
            {
                context.Database.CloseConnection();
            }
        }
    }

    private sealed class ScaleContext(
        string dbPath,
        string archivePath,
        ScaleObjectStoreOptions? objectStore) : DbContext
    {
        public DbSet<ScaleRootHistory> RootHistory => Set<ScaleRootHistory>();
        public string ModelKey => archivePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseDuckDB($"Data Source={dbPath}");
            options.ReplaceService<IModelCacheKeyFactory, ScaleModelCacheKeyFactory>();
            if (objectStore is not null)
            {
                options.AddInterceptors(new ScaleObjectStoreSetupInterceptor(objectStore));
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ScaleRoot>(builder =>
            {
                builder.ToTable("scale_roots");
                builder.HasKey(root => root.Id);
                builder.Property(root => root.Id).ValueGeneratedNever();
                builder.HasMany(root => root.Children).WithOne(child => child.Root).HasForeignKey(child => child.RootId);
            });
            modelBuilder.Entity<ScaleChild>(builder =>
            {
                builder.ToTable("scale_children");
                builder.HasKey(child => child.Id);
                builder.Property(child => child.Id).ValueGeneratedNever();
            });
            modelBuilder.ToTieredStore<ScaleRoot>(root => root.EffectiveAt, archivePath)
                .PartitionBy(partitions => partitions
                    .By(root => root.ScopeKey, "scope_key")
                    .ByMonth(root => root.EffectiveAt, "effective_month"))
                .WithReadModel<ScaleRootHistory>()
                .Including<ScaleChild>(
                    root => root.Children,
                    child => child.WithReadModel<ScaleChildHistory>());
        }
    }

    private sealed class ScaleModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), (context as ScaleContext)?.ModelKey, designTime);
    }

    private sealed class ScaleObjectStoreSetupInterceptor(ScaleObjectStoreOptions options) : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = connection.CreateCommand();
            command.CommandText = options.SetupSql();
            command.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = options.SetupSql();
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed record ScaleObjectStoreOptions(
        string Bucket,
        string Prefix,
        string Region,
        string? Endpoint,
        string? KeyId,
        string? Secret,
        string? SessionToken,
        bool UseSsl)
    {
        public string CreateArchivePath()
            => $"s3://{Bucket}/{Prefix.Trim('/')}/tier-scale-{Guid.NewGuid():N}";

        public string SetupSql()
        {
            var secret = new StringBuilder("INSTALL httpfs; LOAD httpfs; CREATE OR REPLACE SECRET tierscaletest (TYPE s3");
            if (KeyId is null)
            {
                secret.Append(", PROVIDER credential_chain");
            }
            else
            {
                secret.Append(", KEY_ID ").Append(SqlLiteral(KeyId))
                    .Append(", SECRET ").Append(SqlLiteral(Secret!));
                if (!string.IsNullOrEmpty(SessionToken))
                {
                    secret.Append(", SESSION_TOKEN ").Append(SqlLiteral(SessionToken));
                }
            }

            if (!string.IsNullOrEmpty(Region))
            {
                secret.Append(", REGION ").Append(SqlLiteral(Region));
            }

            if (!string.IsNullOrEmpty(Endpoint))
            {
                secret.Append(", ENDPOINT ").Append(SqlLiteral(Endpoint))
                    .Append(", URL_STYLE 'path', USE_SSL ").Append(UseSsl ? "true" : "false");
            }

            return secret.Append(");").ToString();
        }

        public static ScaleObjectStoreOptions? FromEnvironment()
        {
            var bucket = Environment.GetEnvironmentVariable("DUCKDB_TIER_SCALE_S3_BUCKET");
            if (string.IsNullOrWhiteSpace(bucket))
            {
                return null;
            }

            var keyId = Environment.GetEnvironmentVariable("DUCKDB_TIER_SCALE_S3_KEY");
            var secret = Environment.GetEnvironmentVariable("DUCKDB_TIER_SCALE_S3_SECRET");
            if (string.IsNullOrEmpty(keyId) != string.IsNullOrEmpty(secret))
            {
                throw new InvalidOperationException(
                    "DUCKDB_TIER_SCALE_S3_KEY and DUCKDB_TIER_SCALE_S3_SECRET must both be set or both be omitted.");
            }

            var endpoint = Environment.GetEnvironmentVariable("DUCKDB_TIER_SCALE_S3_ENDPOINT");
            var configuredSsl = Environment.GetEnvironmentVariable("DUCKDB_TIER_SCALE_S3_SSL");
            var useSsl = string.IsNullOrEmpty(configuredSsl)
                ? string.IsNullOrEmpty(endpoint)
                : bool.Parse(configuredSsl);
            return new ScaleObjectStoreOptions(
                bucket,
                Environment.GetEnvironmentVariable("DUCKDB_TIER_SCALE_S3_PREFIX") ?? "tiered-provider-scale",
                Environment.GetEnvironmentVariable("DUCKDB_TIER_SCALE_S3_REGION") ?? "us-east-1",
                endpoint,
                keyId,
                secret,
                Environment.GetEnvironmentVariable("DUCKDB_TIER_SCALE_S3_SESSION_TOKEN"),
                useSsl);
        }

        private static string SqlLiteral(string value) => $"'{value.Replace("'", "''")}'";
    }

    private sealed class ScaleRoot
    {
        public long Id { get; set; }
        public int ScopeKey { get; set; }
        public DateTime EffectiveAt { get; set; }
        public string Payload { get; set; } = null!;
        public List<ScaleChild> Children { get; set; } = [];
    }

    private sealed class ScaleChild
    {
        public long Id { get; set; }
        public long RootId { get; set; }
        public ScaleRoot? Root { get; set; }
        public int Value { get; set; }
    }

    private sealed class ScaleRootHistory
    {
        public long Id { get; set; }
        public int ScopeKey { get; set; }
        public DateTime EffectiveAt { get; set; }
        public string Payload { get; set; } = null!;
    }

    private sealed class ScaleChildHistory
    {
        public long Id { get; set; }
        public long RootId { get; set; }
        public int Value { get; set; }
    }
}
