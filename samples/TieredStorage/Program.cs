// Tiered storage over two independent root aggregates: Invoice (tiered on InvoiceDate) and AuditEvent
// (tiered on OccurredOn). Each root names its own timestamp property and archives on its own cutoff, so their
// hot/cold boundaries move independently. Reporting spans hot + cold through keyless read-models.
//
//   dotnet run --project samples/TieredStorage            # cold archive on the local filesystem (default)
//   dotnet run --project samples/TieredStorage -- s3      # cold archive on S3 / an S3-compatible store (httpfs)
//   dotnet run --project samples/TieredStorage -- azure   # cold archive on Azure Blob Storage (azure extension)
//
// The s3/azure modes target a local MinIO / Azurite by default. Start them (and, for S3, auto-create the
// bucket) with the compose file next to this sample:
//   docker compose -f samples/TieredStorage/docker-compose.yml up -d
// Override TIER_S3_* or TIER_AZURE_CONNECTION_STRING / TIER_AZURE_CONTAINER to point at real S3 / Azure.

using Azure.Storage.Blobs;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

var mode = (args.Length > 0 ? args[0].Trim('-') : "local").ToLowerInvariant();
var useS3 = mode == "s3";
var useAzure = mode == "azure";
var useRemote = useS3 || useAzure;

const string dbPath = "tiered_sample.duckdb";
const string localArchiveRoot = "tiered_sample_archive";

var s3 = useS3 ? S3Options.FromEnvironment() : null;
var azure = useAzure ? AzureOptions.FromEnvironment() : null;

var archiveRoot =
    useS3 ? $"s3://{s3!.Bucket}/tiered_sample" :
    useAzure ? $"azure://{azure!.Container}/tiered_sample" :
    localArchiveRoot;
var invoiceArchive = $"{archiveRoot}/invoices";   // each root aggregate gets its own, non-overlapping path
var auditArchive = $"{archiveRoot}/audit";

// One connection interceptor prepares DuckDB for the chosen object store; local mode needs none.
DbConnectionInterceptor? cloudSetup =
    s3 is not null ? new HttpfsSetupInterceptor(s3) :
    azure is not null ? new AzureSetupInterceptor(azure) :
    null;

// Fresh slate for the local artefacts (a remote prefix is left as-is — overwrites are idempotent).
if (File.Exists(dbPath)) File.Delete(dbPath);
if (!useRemote && Directory.Exists(localArchiveRoot)) Directory.Delete(localArchiveRoot, recursive: true);

// Azure only: unlike a local directory, DuckDB does not create the blob container, so ensure it exists first.
if (azure is not null) await new BlobContainerClient(azure.ConnectionString, azure.Container).CreateIfNotExistsAsync();

Console.WriteLine(
    useS3 ? $"Cold archive: {archiveRoot}  (S3 endpoint '{s3!.Endpoint}')\n" :
    useAzure ? $"Cold archive: {archiveRoot}  (Azure Blob Storage)\n" :
    $"Cold archive: ./{localArchiveRoot}  (local filesystem)\n");

using (var db = new SampleContext(dbPath, invoiceArchive, auditArchive, cloudSetup))
{
    db.Database.EnsureCreated(); // also creates the control table + union views for both aggregates

    var thisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

    // Two years of invoices, two lines each — ordinary EF writes with a child graph.
    for (var m = 24; m >= 0; m--)
    {
        var invoice = new Invoice { CustomerId = 100 + m % 3, InvoiceDate = thisMonth.AddMonths(-m) };
        invoice.Lines.Add(new InvoiceLine { Description = "Services", Amount = 100 + m });
        invoice.Lines.Add(new InvoiceLine { Description = "Expenses", Amount = 25 });
        db.Invoices.Add(invoice);
    }

    // Three years of audit events — a different root, keyed on a different date.
    for (var m = 36; m >= 0; m--)
    {
        db.AuditEvents.Add(new AuditEvent { OccurredOn = thisMonth.AddMonths(-m), Action = "login" });
    }

    db.SaveChanges();

    // Each aggregate archives on its OWN cutoff, against its OWN timestamp property — independent boundaries.
    var invoiceCutoff = thisMonth.AddYears(-1);
    var auditCutoff = thisMonth.AddMonths(-6);
    await db.Database.ArchiveTierAsync<Invoice>(invoiceCutoff);
    await db.Database.ArchiveTierAsync<AuditEvent>(auditCutoff);

    Console.WriteLine($"Invoices   hot {db.Invoices.Count(),3}  |  hot+cold {db.InvoiceHistory.Count(),3}   (InvoiceDate, cutoff {invoiceCutoff:yyyy-MM})");
    Console.WriteLine($"Audit      hot {db.AuditEvents.Count(),3}  |  hot+cold {db.AuditHistory.Count(),3}   (OccurredOn, cutoff {auditCutoff:yyyy-MM})");

    // Most cold reports are single read-model aggregates — no join needed:
    var invoicesByYear = db.InvoiceHistory
        .GroupBy(i => i.InvoiceDate.Year)
        .Select(g => new { Year = g.Key, Count = g.Count() })
        .OrderBy(r => r.Year);

    Console.WriteLine("\nInvoices by year (hot + cold):");
    foreach (var row in invoicesByYear)
    {
        Console.WriteLine($"  {row.Year}: {row.Count,3}");
    }

    // A cross-table report joins on the FK column — read-models are keyless, so there are no navigations:
    var revenueByYear = db.LineHistory
        .Join(db.InvoiceHistory, l => l.InvoiceId, i => i.Id, (l, i) => new { i.InvoiceDate.Year, l.Amount })
        .GroupBy(x => x.Year)
        .Select(g => new { Year = g.Key, Revenue = g.Sum(x => x.Amount) })
        .OrderBy(r => r.Year);

    Console.WriteLine("\nRevenue by year (hot + cold):");
    foreach (var row in revenueByYear)
    {
        Console.WriteLine($"  {row.Year}: {row.Revenue:C}");
    }

    // Retention: on local disk we purge partitions directly; on an object store, PurgeArchiveOlderThan is not
    // supported (DuckDB can't delete objects) — use a bucket lifecycle rule / Azure lifecycle-management policy.
    if (!useRemote)
    {
        db.Database.PurgeArchiveOlderThan<Invoice>(thisMonth.AddMonths(-18));
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
internal sealed class Invoice
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public DateTime InvoiceDate { get; set; }
    public List<InvoiceLine> Lines { get; set; } = [];
}

internal sealed class InvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
}

internal sealed class AuditEvent
{
    public int Id { get; set; }
    public DateTime OccurredOn { get; set; }
    public required string Action { get; set; }
}

// --- Cold read-models: keyless projections used for hot+cold reporting. ---
internal sealed class InvoiceReport
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public DateTime InvoiceDate { get; set; }
}

internal sealed class InvoiceLineReport
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public decimal Amount { get; set; }
}

internal sealed class AuditEventReport
{
    public int Id { get; set; }
    public DateTime OccurredOn { get; set; }
}

internal sealed class SampleContext(string dbPath, string invoiceArchive, string auditArchive, DbConnectionInterceptor? cloudSetup) : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<InvoiceReport> InvoiceHistory => Set<InvoiceReport>();
    public DbSet<InvoiceLineReport> LineHistory => Set<InvoiceLineReport>();
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
        modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, invoiceArchive, TierGranularity.Month)
            .PartitionBy(partitions => partitions
                .By(i => i.CustomerId)
                .ByMonth(i => i.InvoiceDate)) // exact application order; InvoiceLine inherits both root values
            .WithReadModel<InvoiceReport>()
            .Including<InvoiceLine>(i => i.Lines, line => line.WithReadModel<InvoiceLineReport>());

        modelBuilder.ToTieredStore<AuditEvent>(e => e.OccurredOn, auditArchive, TierGranularity.Month)
            .WithReadModel<AuditEventReport>();
    }
}

// --- S3 setup: loads httpfs and configures S3 credentials on every connection. ---
internal sealed class HttpfsSetupInterceptor(S3Options s3) : DbConnectionInterceptor
{
    private string SetupSql =>
        "INSTALL httpfs; LOAD httpfs; CREATE OR REPLACE SECRET tiersample (TYPE s3, "
        + $"KEY_ID '{s3.KeyId}', SECRET '{s3.Secret}', REGION '{s3.Region}'"
        + (string.IsNullOrEmpty(s3.Endpoint) ? "" : $", ENDPOINT '{s3.Endpoint}', URL_STYLE 'path', USE_SSL {(s3.UseSsl ? "true" : "false")}")
        + ");";

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

internal sealed record S3Options(string Endpoint, string KeyId, string Secret, string Region, string Bucket, bool UseSsl)
{
    public static S3Options FromEnvironment() => new(
        Environment.GetEnvironmentVariable("TIER_S3_ENDPOINT") ?? "localhost:9000",
        Environment.GetEnvironmentVariable("TIER_S3_KEY") ?? "minioadmin",
        Environment.GetEnvironmentVariable("TIER_S3_SECRET") ?? "minioadmin",
        Environment.GetEnvironmentVariable("TIER_S3_REGION") ?? "us-east-1",
        Environment.GetEnvironmentVariable("TIER_S3_BUCKET") ?? "tier",
        string.Equals(Environment.GetEnvironmentVariable("TIER_S3_SSL"), "true", StringComparison.OrdinalIgnoreCase));
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
