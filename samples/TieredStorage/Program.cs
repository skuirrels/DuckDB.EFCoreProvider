// Tiered storage over two independent root aggregates: Record (tiered on EffectiveAt) and AuditEvent
// (tiered on OccurredOn). Each root names its own timestamp property and archives on its own cutoff, so their
// hot/cold boundaries move independently. Reporting spans hot + cold through keyless read-models.
//
//   dotnet run --project samples/TieredStorage            # cold archive on the local filesystem (default)
//   dotnet run --project samples/TieredStorage -- s3      # cold archive on S3 / an S3-compatible store (httpfs)
//   dotnet run --project samples/TieredStorage -- gcs     # cold archive on Google Cloud Storage (httpfs)
//   dotnet run --project samples/TieredStorage -- azure   # cold archive on Azure Blob Storage (azure extension)
//
// The s3/gcs/azure modes target local MinIO / Azurite services by default. GCS uses MinIO's S3-compatible API
// to exercise DuckDB's TYPE gcs secret and gcs:// URL path locally; use a real GCS bucket to validate Google IAM.
// Start the services and auto-create the MinIO buckets with the compose file next to this sample:
//   docker compose -f samples/TieredStorage/docker-compose.yml up -d
// Override TIER_S3_*, TIER_GCS_*, or TIER_AZURE_* to point at a real cloud store.

using Azure.Storage.Blobs;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

var mode = (args.Length > 0 ? args[0].Trim('-') : "local").ToLowerInvariant();
var useS3 = mode == "s3";
var useGcs = mode == "gcs";
var useAzure = mode == "azure";
var useRemote = useS3 || useGcs || useAzure;

const string dbPath = "tiered_sample.duckdb";
const string localArchiveRoot = "tiered_sample_archive";

var httpfs =
    useS3 ? HttpfsOptions.FromS3Environment() :
    useGcs ? HttpfsOptions.FromGcsEnvironment() :
    null;
var azure = useAzure ? AzureOptions.FromEnvironment() : null;

var archiveRoot =
    httpfs is not null ? $"{httpfs.Scheme}://{httpfs.Bucket}/tiered_sample" :
    useAzure ? $"azure://{azure!.Container}/tiered_sample" :
    localArchiveRoot;
var recordArchive = $"{archiveRoot}/records";   // each root aggregate gets its own, non-overlapping path
var auditArchive = $"{archiveRoot}/audit";

// One connection interceptor prepares DuckDB for the chosen object store; local mode needs none.
DbConnectionInterceptor? cloudSetup =
    httpfs is not null ? new HttpfsSetupInterceptor(httpfs) :
    azure is not null ? new AzureSetupInterceptor(azure) :
    null;

// Fresh slate for the local artefacts (a remote prefix is left as-is — overwrites are idempotent).
if (File.Exists(dbPath)) File.Delete(dbPath);
if (!useRemote && Directory.Exists(localArchiveRoot)) Directory.Delete(localArchiveRoot, recursive: true);

// Azure only: unlike a local directory, DuckDB does not create the blob container, so ensure it exists first.
if (azure is not null) await new BlobContainerClient(azure.ConnectionString, azure.Container).CreateIfNotExistsAsync();

var coldArchiveDescription =
    httpfs is not null
        ? $"Cold archive: {archiveRoot}  ({httpfs.DisplayName}, "
          + (string.IsNullOrEmpty(httpfs.Endpoint) ? "default cloud endpoint)\n" : $"endpoint '{httpfs.Endpoint}')\n")
        : useAzure
            ? $"Cold archive: {archiveRoot}  (Azure Blob Storage)\n"
            : $"Cold archive: ./{localArchiveRoot}  (local filesystem)\n";
Console.WriteLine(coldArchiveDescription);

using (var db = new SampleContext(dbPath, recordArchive, auditArchive, cloudSetup))
{
    db.Database.EnsureCreated(); // also creates the control table + union views for both aggregates

    var thisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

    // Two years of records, two parts each — ordinary EF writes with a child graph.
    for (var m = 24; m >= 0; m--)
    {
        var record = new Record { GroupId = 100 + m % 3, EffectiveAt = thisMonth.AddMonths(-m) };
        record.Parts.Add(new RecordPart { Description = "Primary", Value = 100 + m });
        record.Parts.Add(new RecordPart { Description = "Secondary", Value = 25 });
        db.Records.Add(record);
    }

    // Three years of audit events — a different root, keyed on a different date.
    for (var m = 36; m >= 0; m--)
    {
        db.AuditEvents.Add(new AuditEvent { OccurredOn = thisMonth.AddMonths(-m), Action = "login" });
    }

    db.SaveChanges();

    // Each aggregate archives on its OWN cutoff, against its OWN timestamp property — independent boundaries.
    var recordCutoff = thisMonth.AddYears(-1);
    var auditCutoff = thisMonth.AddMonths(-6);
    await db.Database.ArchiveTierAsync<Record>(recordCutoff);
    await db.Database.ArchiveTierAsync<AuditEvent>(auditCutoff);

    Console.WriteLine($"Records   hot {db.Records.Count(),3}  |  hot+cold {db.RecordHistory.Count(),3}   (EffectiveAt, cutoff {recordCutoff:yyyy-MM})");
    Console.WriteLine($"Audit      hot {db.AuditEvents.Count(),3}  |  hot+cold {db.AuditHistory.Count(),3}   (OccurredOn, cutoff {auditCutoff:yyyy-MM})");

    // Most cold reports are single read-model aggregates — no join needed:
    var recordsByYear = db.RecordHistory
        .GroupBy(i => i.EffectiveAt.Year)
        .Select(g => new { Year = g.Key, Count = g.Count() })
        .OrderBy(r => r.Year);

    Console.WriteLine("\nRecords by year (hot + cold):");
    foreach (var row in recordsByYear)
    {
        Console.WriteLine($"  {row.Year}: {row.Count,3}");
    }

    // A cross-table report joins on the FK column — read-models are keyless, so there are no navigations:
    var totalValueByYear = db.PartHistory
        .Join(db.RecordHistory, l => l.RecordId, i => i.Id, (l, i) => new { i.EffectiveAt.Year, l.Value })
        .GroupBy(x => x.Year)
        .Select(g => new { Year = g.Key, TotalValue = g.Sum(x => x.Value) })
        .OrderBy(r => r.Year);

    Console.WriteLine("\nTotal value by year (hot + cold):");
    foreach (var row in totalValueByYear)
    {
        Console.WriteLine($"  {row.Year}: {row.TotalValue:N2}");
    }

    // Retention: on local disk we purge partitions directly; on an object store, PurgeArchiveOlderThan is not
    // supported (DuckDB can't delete objects) — use the cloud store's lifecycle-management policy.
    if (!useRemote)
    {
        db.Database.PurgeArchiveOlderThan<Record>(thisMonth.AddMonths(-18));
        db.Database.PurgeArchiveOlderThan<AuditEvent>(thisMonth.AddMonths(-18));
        Console.WriteLine("\nPurged archive partitions older than 18 months.");
    }
    else
    {
        Console.WriteLine("\nRetention on object storage: use a lifecycle rule/policy on the 'tiered_sample/' prefix " +
                          "(PurgeArchiveOlderThan is not supported for remote archives).");
    }
}

Console.WriteLine(useRemote
    ? "\nDone. Delete 'tiered_sample.duckdb' and the remote 'tiered_sample/' prefix to reset."
    : "\nDone. Delete 'tiered_sample.duckdb' and 'tiered_sample_archive/' to reset.");

// --- Hot model: ordinary EF Core entities. ---
internal sealed class Record
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public DateTime EffectiveAt { get; set; }
    public List<RecordPart> Parts { get; set; } = [];
}

internal sealed class RecordPart
{
    public int Id { get; set; }
    public int RecordId { get; set; }
    public Record? Record { get; set; }
    public required string Description { get; set; }
    public decimal Value { get; set; }
}

internal sealed class AuditEvent
{
    public int Id { get; set; }
    public DateTime OccurredOn { get; set; }
    public required string Action { get; set; }
}

// --- Cold read-models: keyless projections used for hot+cold reporting. ---
internal sealed class RecordReport
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public DateTime EffectiveAt { get; set; }
}

internal sealed class RecordPartReport
{
    public int Id { get; set; }
    public int RecordId { get; set; }
    public decimal Value { get; set; }
}

internal sealed class AuditEventReport
{
    public int Id { get; set; }
    public DateTime OccurredOn { get; set; }
}

internal sealed class SampleContext(string dbPath, string recordArchive, string auditArchive, DbConnectionInterceptor? cloudSetup) : DbContext
{
    public DbSet<Record> Records => Set<Record>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<RecordReport> RecordHistory => Set<RecordReport>();
    public DbSet<RecordPartReport> PartHistory => Set<RecordPartReport>();
    public DbSet<AuditEventReport> AuditHistory => Set<AuditEventReport>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseDuckDB($"Data Source={dbPath}");
        if (cloudSetup is not null)
        {
            // Loads the object-store extension and credentials on every connection. Because the provider opens
            // its connections through EF Core, this runs for the archive operations too.
            options.AddInterceptors(cloudSetup);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Two independent roots, each tiering on its own timestamp property — the date that defines its hot/cold boundary.
        modelBuilder.ToTieredStore<Record>(i => i.EffectiveAt, recordArchive, TierGranularity.Month)
            .PartitionBy(partitions => partitions
                .By(i => i.GroupId)
                .ByMonth(i => i.EffectiveAt)) // exact application order; RecordPart inherits both root values
            .WithReadModel<RecordReport>()
            .Including<RecordPart>(i => i.Parts, part => part.WithReadModel<RecordPartReport>());

        modelBuilder.ToTieredStore<AuditEvent>(e => e.OccurredOn, auditArchive, TierGranularity.Month)
            .WithReadModel<AuditEventReport>();
    }
}

// --- S3/GCS setup: loads httpfs and configures credentials on every connection. ---
internal sealed class HttpfsSetupInterceptor(HttpfsOptions options) : DbConnectionInterceptor
{
    private string SetupSql =>
        $"INSTALL httpfs; LOAD httpfs; CREATE OR REPLACE SECRET tiersample (TYPE {options.SecretType}, "
        + $"KEY_ID {SqlLiteral(options.KeyId)}, SECRET {SqlLiteral(options.Secret)}"
        + (string.IsNullOrEmpty(options.Region) ? "" : $", REGION {SqlLiteral(options.Region)}")
        + (string.IsNullOrEmpty(options.Endpoint) ? "" : $", ENDPOINT {SqlLiteral(options.Endpoint)}, URL_STYLE 'path', USE_SSL {(options.UseSsl ? "true" : "false")}")
        + ");";

    private static string SqlLiteral(string value) => $"'{value.Replace("'", "''")}'";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SetupSql;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SetupSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

// --- Azure setup: loads the azure extension and configures a blob credential on every connection. ---
internal sealed class AzureSetupInterceptor(AzureOptions azure) : DbConnectionInterceptor
{
    private string SetupSql =>
        "INSTALL azure; LOAD azure; CREATE OR REPLACE SECRET tiersample "
        + $"(TYPE azure, CONNECTION_STRING '{azure.ConnectionString}');";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SetupSql;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SetupSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

internal sealed record HttpfsOptions(
    string Scheme,
    string SecretType,
    string DisplayName,
    string Endpoint,
    string KeyId,
    string Secret,
    string? Region,
    string Bucket,
    bool UseSsl)
{
    public static HttpfsOptions FromS3Environment() => new(
        "s3",
        "s3",
        "S3",
        Environment.GetEnvironmentVariable("TIER_S3_ENDPOINT") ?? "localhost:9000",
        Environment.GetEnvironmentVariable("TIER_S3_KEY") ?? "minioadmin",
        Environment.GetEnvironmentVariable("TIER_S3_SECRET") ?? "minioadmin",
        Environment.GetEnvironmentVariable("TIER_S3_REGION") ?? "us-east-1",
        Environment.GetEnvironmentVariable("TIER_S3_BUCKET") ?? "tier",
        string.Equals(Environment.GetEnvironmentVariable("TIER_S3_SSL"), "true", StringComparison.OrdinalIgnoreCase));

    public static HttpfsOptions FromGcsEnvironment() => new(
        "gcs",
        "gcs",
        "Google Cloud Storage",
        Environment.GetEnvironmentVariable("TIER_GCS_ENDPOINT") ?? "localhost:9000",
        Environment.GetEnvironmentVariable("TIER_GCS_KEY_ID") ?? "minioadmin",
        Environment.GetEnvironmentVariable("TIER_GCS_SECRET") ?? "minioadmin",
        null,
        Environment.GetEnvironmentVariable("TIER_GCS_BUCKET") ?? "gcs-tier",
        string.Equals(Environment.GetEnvironmentVariable("TIER_GCS_SSL"), "true", StringComparison.OrdinalIgnoreCase));
}

internal sealed record AzureOptions(string ConnectionString, string Container)
{
    // Defaults target a local Azurite (the compose service). The well-known Azurite account/key are public.
    public static AzureOptions FromEnvironment() => new(
        Environment.GetEnvironmentVariable("TIER_AZURE_CONNECTION_STRING")
            ?? "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;"
             + "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
             + "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;",
        Environment.GetEnvironmentVariable("TIER_AZURE_CONTAINER") ?? "tier");
}
