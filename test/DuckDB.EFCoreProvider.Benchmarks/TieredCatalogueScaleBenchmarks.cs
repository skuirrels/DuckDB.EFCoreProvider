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
///     Parameterised provider-neutral scale fixture for immutable generations, exact file catalogues, bounded
///     aggregate depth, technical file fan-out, shared descendants, and retained partition scopes.
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

    /// <summary>Number of configured nodes in the primary root graph, from one through four.</summary>
    [ParamsSource(nameof(NodeCounts))]
    public int NodeCount { get; set; }

    /// <summary>Number of exact technical file-shard partitions per leading partition and lifecycle period.</summary>
    [ParamsSource(nameof(FileFanOuts))]
    public int FileFanOut { get; set; }

    /// <summary>Number of exact technical partition scopes supplied to retention planning.</summary>
    [ParamsSource(nameof(RetainedScopeCardinalities))]
    public int RetainedScopeCardinality { get; set; }

    /// <summary>Number of leading declared partition dimensions supplied by each retained scope.</summary>
    [ParamsSource(nameof(ScopePrefixWidths))]
    public int ScopePrefixWidth { get; set; }

    /// <summary>Whether a second root binding shares the primary graph's descendant table.</summary>
    [ParamsSource(nameof(SharedDescendantShapes))]
    public bool SharedDescendantShape { get; set; }

    /// <summary>Default or environment-supplied exact-partition cardinalities.</summary>
    public IEnumerable<int> PartitionCardinalities => DimensionValues("DUCKDB_TIER_SCALE_PARTITIONS", [16]);

    /// <summary>Default or environment-supplied lifecycle-period counts.</summary>
    public IEnumerable<int> PeriodCounts => DimensionValues("DUCKDB_TIER_SCALE_PERIODS", [12]);

    public IEnumerable<int> NodeCounts => DimensionValues("DUCKDB_TIER_SCALE_NODES", [2]);

    public IEnumerable<int> FileFanOuts => DimensionValues("DUCKDB_TIER_SCALE_FILE_FANOUT", [1]);

    public IEnumerable<int> RetainedScopeCardinalities
        => NonNegativeDimensionValues("DUCKDB_TIER_SCALE_RETAINED_SCOPES", [0]);

    public IEnumerable<int> ScopePrefixWidths => DimensionValues("DUCKDB_TIER_SCALE_SCOPE_PREFIX_WIDTH", [1]);

    public IEnumerable<bool> SharedDescendantShapes
        => BooleanDimensionValues("DUCKDB_TIER_SCALE_SHARED_DESCENDANT", [false]);

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "duckdb-tier-scale-" + Guid.NewGuid().ToString("N"));
        _dbPath = Path.Combine(_root, "scale.duckdb");
        _objectStore = ScaleObjectStoreOptions.FromEnvironment();
        _archivePath = _objectStore?.CreateArchivePath() ?? Path.Combine(_root, "archive");
        ValidateDimensions();
        Directory.CreateDirectory(_root);
        using var context = CreateContext();
        context.Database.EnsureCreated();
        var rows = checked(PartitionCardinality * Periods * FileFanOut);
#pragma warning disable EF1002, EF1003 // Benchmark dimensions are controlled integer parameters, not external SQL.
        context.Database.ExecuteSqlRaw(
            $"INSERT INTO scale_roots (\"Id\", \"ScopeKey\", \"FanOutKey\", \"EffectiveAt\", \"Payload\") "
            + $"SELECT i + 1, CAST(floor(i::DOUBLE / {FileFanOut}) AS BIGINT) % {PartitionCardinality}, "
            + $"i % {FileFanOut}, DATE '2020-01-01' "
            + $"+ to_months(CAST(floor(i::DOUBLE / {PartitionCardinality * FileFanOut}) AS INTEGER)), repeat('x', 64) "
            + $"FROM range(0, {rows}) AS source(i);");
        SeedDescendants(context, rows);
#pragma warning restore EF1002, EF1003
        context.Database.ArchiveTier<ScaleRoot>(FirstPeriod.AddMonths(Periods));
        if (SharedDescendantShape)
        {
            context.Database.ArchiveTier<ScaleAuxRoot>(FirstPeriod.AddMonths(Periods));
        }

#pragma warning disable EF1003 // View names are selected from the fixed Provider-neutral benchmark graph below.
        _viewSqlCharacters = context.Database.SqlQueryRaw<long>(
                "SELECT COALESCE(sum(length(sql)), 0) AS \"Value\" FROM duckdb_views() "
                + $"WHERE view_name IN ({ViewNamesSql()})")
            .Single();
#pragma warning restore EF1003
        _catalogueFiles = context.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM __duckdb_tier_generation_files "
                + "WHERE control_key IN ('scale_roots', 'scale_aux_roots')")
            .Single();
        var configuredNodes = context.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM __duckdb_tier_generation_nodes "
                + "WHERE control_key IN ('scale_roots', 'scale_aux_roots')")
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
            + $"partitions={PartitionCardinality}, periods={Periods}, configured_nodes={configuredNodes}, "
            + $"primary_nodes={NodeCount}, file_fanout={FileFanOut}, shared_descendant={SharedDescendantShape}, "
            + $"retained_scopes={RetainedScopeCardinality}, scope_prefix_width={ScopePrefixWidth}, "
            + $"catalogue_files={_catalogueFiles}, generated_view_sql_chars={_viewSqlCharacters}, "
            + $"scoped_total_files_read={filesRead}, archive_path={_archivePath}");
    }

    /// <summary>Measures restart plus exact-catalogue view regeneration.</summary>
    [Benchmark]
    public long RestartAndRegenerateViews()
    {
        using var context = CreateContext();
        context.Database.EnsureTieredStoresCreated();
#pragma warning disable EF1003 // View names are selected from the fixed Provider-neutral benchmark graph below.
        var characters = context.Database.SqlQueryRaw<long>(
                "SELECT COALESCE(sum(length(sql)), 0) AS \"Value\" FROM duckdb_views() "
                + $"WHERE view_name IN ({ViewNamesSql()})")
            .Single();
#pragma warning restore EF1003
        return characters;
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
                new TierArchiveRetentionOptions
                {
                    RetainFrom = FirstPeriod.AddMonths(Periods / 2),
                    RetainedPartitionScopes = RetainedScopes(),
                })
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

    private ScaleContext CreateContext()
        => new(_dbPath, _archivePath, _objectStore, NodeCount, SharedDescendantShape);

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

    private static IEnumerable<int> NonNegativeDimensionValues(string variable, int[] defaults)
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
        if (values.Length == 0 || values.Any(value => value < 0))
        {
            throw new InvalidOperationException($"{variable} must contain one or more non-negative integers.");
        }

        return values;
    }

    private static IEnumerable<bool> BooleanDimensionValues(string variable, bool[] defaults)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return defaults;
        }

        return configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value switch
            {
                "1" => true,
                "0" => false,
                _ => bool.Parse(value),
            })
            .Distinct()
            .ToArray();
    }

    private void ValidateDimensions()
    {
        if (NodeCount is < 1 or > 4)
        {
            throw new InvalidOperationException("DUCKDB_TIER_SCALE_NODES must contain values from 1 through 4.");
        }

        if (ScopePrefixWidth is < 1 or > 2)
        {
            throw new InvalidOperationException(
                "DUCKDB_TIER_SCALE_SCOPE_PREFIX_WIDTH must contain 1 or 2.");
        }

        if (SharedDescendantShape && NodeCount != 2)
        {
            throw new InvalidOperationException(
                "The shared-descendant preset uses two primary nodes; set DUCKDB_TIER_SCALE_NODES=2.");
        }

        var availableScopes = ScopePrefixWidth == 1
            ? PartitionCardinality
            : checked(PartitionCardinality * FileFanOut);
        if (RetainedScopeCardinality > availableScopes)
        {
            throw new InvalidOperationException(
                $"Retained scope cardinality {RetainedScopeCardinality} exceeds the {availableScopes} distinct "
                + $"scope prefixes available at width {ScopePrefixWidth}.");
        }
    }

    private void SeedDescendants(ScaleContext context, int rows)
    {
#pragma warning disable EF1002, EF1003 // Benchmark dimensions are controlled integer parameters, not external SQL.
        if (SharedDescendantShape)
        {
            context.Database.ExecuteSqlRaw(
                $"INSERT INTO scale_aux_roots (\"Id\", \"ScopeKey\", \"FanOutKey\", \"EffectiveAt\") "
                + $"SELECT i + 1, CAST(floor(i::DOUBLE / {FileFanOut}) AS BIGINT) % {PartitionCardinality}, "
                + $"i % {FileFanOut}, DATE '2020-01-01' "
                + $"+ to_months(CAST(floor(i::DOUBLE / {PartitionCardinality * FileFanOut}) AS INTEGER)) "
                + $"FROM range(0, {rows}) AS source(i);");
            context.Database.ExecuteSqlRaw(
                "INSERT INTO scale_shared_children (\"Id\", \"MainRootId\", \"AuxRootId\", \"Value\") "
                + $"SELECT i + 1, i + 1, NULL, i % 100 FROM range(0, {rows}) AS source(i) "
                + "UNION ALL "
                + $"SELECT {rows} + i + 1, NULL, i + 1, i % 100 FROM range(0, {rows}) AS source(i);");
            return;
        }

        if (NodeCount >= 2)
        {
            context.Database.ExecuteSqlRaw(
                "INSERT INTO scale_children (\"Id\", \"RootId\", \"Value\") "
                + $"SELECT i + 1, i + 1, i % 100 FROM range(0, {rows}) AS source(i);");
        }

        if (NodeCount >= 3)
        {
            context.Database.ExecuteSqlRaw(
                "INSERT INTO scale_grandchildren (\"Id\", \"ChildId\", \"Value\") "
                + $"SELECT i + 1, i + 1, i % 100 FROM range(0, {rows}) AS source(i);");
        }

        if (NodeCount >= 4)
        {
            context.Database.ExecuteSqlRaw(
                "INSERT INTO scale_leaves (\"Id\", \"GrandchildId\", \"Value\") "
                + $"SELECT i + 1, i + 1, i % 100 FROM range(0, {rows}) AS source(i);");
        }
#pragma warning restore EF1002, EF1003
    }

    private IReadOnlyList<TierMaintenanceScope> RetainedScopes()
        => Enumerable.Range(0, RetainedScopeCardinality)
            .Select(index =>
            {
                var values = new Dictionary<string, object?>
                {
                    [nameof(ScaleRoot.ScopeKey)] = index % PartitionCardinality,
                };
                if (ScopePrefixWidth == 2)
                {
                    values[nameof(ScaleRoot.FanOutKey)] = index / PartitionCardinality;
                }

                return TierMaintenanceScope.ForPartitionValues(values);
            })
            .ToArray();

    private string ViewNamesSql()
    {
        var names = new List<string> { "scale_roots_tiered" };
        if (SharedDescendantShape)
        {
            names.Add("scale_aux_roots_tiered");
            names.Add("scale_shared_children_tiered");
        }
        else
        {
            if (NodeCount >= 2)
            {
                names.Add("scale_children_tiered");
            }

            if (NodeCount >= 3)
            {
                names.Add("scale_grandchildren_tiered");
            }

            if (NodeCount >= 4)
            {
                names.Add("scale_leaves_tiered");
            }
        }

        return string.Join(", ", names.Select(name => $"'{name}'"));
    }

    private IQueryable<ScaleRootHistory> ScopedQuery(ScaleContext context)
    {
        var from = FirstPeriod.AddMonths(Periods / 2);
        var to = from.AddMonths(1);
        var scope = PartitionCardinality / 2;
        return context.RootHistory.Where(root =>
            root.ScopeKey == scope
            && root.FanOutKey == FileFanOut / 2
            && root.EffectiveAt >= from
            && root.EffectiveAt < to);
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
        ScaleObjectStoreOptions? objectStore,
        int nodeCount,
        bool sharedDescendantShape) : DbContext
    {
        public DbSet<ScaleRootHistory> RootHistory => Set<ScaleRootHistory>();
        public string ModelKey => $"{archivePath}|{nodeCount}|{sharedDescendantShape}";

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
                builder.HasMany(root => root.SharedChildren)
                    .WithOne(child => child.MainRoot)
                    .HasForeignKey(child => child.MainRootId);
            });
            modelBuilder.Entity<ScaleChild>(builder =>
            {
                builder.ToTable("scale_children");
                builder.HasKey(child => child.Id);
                builder.Property(child => child.Id).ValueGeneratedNever();
                builder.HasMany(child => child.Grandchildren)
                    .WithOne(grandchild => grandchild.Child)
                    .HasForeignKey(grandchild => grandchild.ChildId);
            });
            modelBuilder.Entity<ScaleGrandchild>(builder =>
            {
                builder.ToTable("scale_grandchildren");
                builder.HasKey(grandchild => grandchild.Id);
                builder.Property(grandchild => grandchild.Id).ValueGeneratedNever();
                builder.HasMany(grandchild => grandchild.Leaves)
                    .WithOne(leaf => leaf.Grandchild)
                    .HasForeignKey(leaf => leaf.GrandchildId);
            });
            modelBuilder.Entity<ScaleLeaf>(builder =>
            {
                builder.ToTable("scale_leaves");
                builder.HasKey(leaf => leaf.Id);
                builder.Property(leaf => leaf.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<ScaleAuxRoot>(builder =>
            {
                builder.ToTable("scale_aux_roots");
                builder.HasKey(root => root.Id);
                builder.Property(root => root.Id).ValueGeneratedNever();
                builder.HasMany(root => root.SharedChildren)
                    .WithOne(child => child.AuxRoot)
                    .HasForeignKey(child => child.AuxRootId);
            });
            modelBuilder.Entity<ScaleSharedChild>(builder =>
            {
                builder.ToTable("scale_shared_children");
                builder.HasKey(child => child.Id);
                builder.Property(child => child.Id).ValueGeneratedNever();
            });

            var primaryArchivePath = sharedDescendantShape ? archivePath + "/shared-main" : archivePath;
            var primary = modelBuilder.ToTieredStore<ScaleRoot>(root => root.EffectiveAt, primaryArchivePath)
                .PartitionBy(partitions => partitions
                    .By(root => root.ScopeKey, "scope_key")
                    .By(root => root.FanOutKey, "fanout_key")
                    .ByMonth(root => root.EffectiveAt, "effective_month"))
                .WithReadModel<ScaleRootHistory>();
            if (sharedDescendantShape)
            {
                primary.Including<ScaleSharedChild>(
                    root => root.SharedChildren,
                    child => child.WithTieredView("scale_shared_children_tiered"));
                modelBuilder.ToTieredStore<ScaleAuxRoot>(
                        root => root.EffectiveAt,
                        archivePath + "/shared-aux",
                        controlKey: "scale_aux_roots")
                    .PartitionBy(partitions => partitions
                        .By(root => root.ScopeKey, "scope_key")
                        .By(root => root.FanOutKey, "fanout_key")
                        .ByMonth(root => root.EffectiveAt, "effective_month"))
                    .WithTieredView("scale_aux_roots_tiered")
                    .Including<ScaleSharedChild>(
                        root => root.SharedChildren,
                        child => child.WithTieredView("scale_shared_children_tiered"));
            }
            else if (nodeCount >= 2)
            {
                primary.Including<ScaleChild>(root => root.Children, child =>
                {
                    child.WithReadModel<ScaleChildHistory>();
                    if (nodeCount >= 3)
                    {
                        child.Including<ScaleGrandchild>(item => item.Grandchildren, grandchild =>
                        {
                            grandchild.WithReadModel<ScaleGrandchildHistory>();
                            if (nodeCount >= 4)
                            {
                                grandchild.Including<ScaleLeaf>(
                                    item => item.Leaves,
                                    leaf => leaf.WithReadModel<ScaleLeafHistory>());
                            }
                        });
                    }
                });
            }
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
        public int FanOutKey { get; set; }
        public DateTime EffectiveAt { get; set; }
        public string Payload { get; set; } = null!;
        public List<ScaleChild> Children { get; set; } = [];
        public List<ScaleSharedChild> SharedChildren { get; set; } = [];
    }

    private sealed class ScaleChild
    {
        public long Id { get; set; }
        public long RootId { get; set; }
        public ScaleRoot? Root { get; set; }
        public int Value { get; set; }
        public List<ScaleGrandchild> Grandchildren { get; set; } = [];
    }

    private sealed class ScaleGrandchild
    {
        public long Id { get; set; }
        public long ChildId { get; set; }
        public ScaleChild? Child { get; set; }
        public int Value { get; set; }
        public List<ScaleLeaf> Leaves { get; set; } = [];
    }

    private sealed class ScaleLeaf
    {
        public long Id { get; set; }
        public long GrandchildId { get; set; }
        public ScaleGrandchild? Grandchild { get; set; }
        public int Value { get; set; }
    }

    private sealed class ScaleAuxRoot
    {
        public long Id { get; set; }
        public int ScopeKey { get; set; }
        public int FanOutKey { get; set; }
        public DateTime EffectiveAt { get; set; }
        public List<ScaleSharedChild> SharedChildren { get; set; } = [];
    }

    private sealed class ScaleSharedChild
    {
        public long Id { get; set; }
        public long? MainRootId { get; set; }
        public ScaleRoot? MainRoot { get; set; }
        public long? AuxRootId { get; set; }
        public ScaleAuxRoot? AuxRoot { get; set; }
        public int Value { get; set; }
    }

    private sealed class ScaleRootHistory
    {
        public long Id { get; set; }
        public int ScopeKey { get; set; }
        public int FanOutKey { get; set; }
        public DateTime EffectiveAt { get; set; }
        public string Payload { get; set; } = null!;
    }

    private sealed class ScaleChildHistory
    {
        public long Id { get; set; }
        public long RootId { get; set; }
        public int Value { get; set; }
    }

    private sealed class ScaleGrandchildHistory
    {
        public long Id { get; set; }
        public long ChildId { get; set; }
        public int Value { get; set; }
    }

    private sealed class ScaleLeafHistory
    {
        public long Id { get; set; }
        public long GrandchildId { get; set; }
        public int Value { get; set; }
    }
}