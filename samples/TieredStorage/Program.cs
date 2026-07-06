// Tiered storage over two independent root aggregates: Invoice (tiered on InvoiceDate) and AuditEvent
// (tiered on OccurredOn). Each root names its own timestamp property and archives on its own cutoff, so their
// hot/cold boundaries move independently. Reporting spans hot + cold through keyless read-models.
//
//   dotnet run --project samples/TieredStorage          # cold archive on the local filesystem (default)
//   dotnet run --project samples/TieredStorage -- s3     # cold archive on S3 / an S3-compatible store
//
// S3 mode defaults to a local MinIO (docker run -p 9000:9000 -e MINIO_ROOT_USER=minioadmin
// -e MINIO_ROOT_PASSWORD=minioadmin minio/minio server /data — and create a bucket named 'tier').
// Override with TIER_S3_ENDPOINT / _BUCKET / _KEY / _SECRET / _REGION / _SSL to point at real S3.

using System.Data.Common;
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var useS3 = args.Length > 0 && args[0].Trim('-').Equals("s3", StringComparison.OrdinalIgnoreCase);
const string dbPath = "tiered_sample.duckdb";
const string localArchiveRoot = "tiered_sample_archive";

var s3 = useS3 ? S3Options.FromEnvironment() : null;
var archiveRoot = useS3 ? $"s3://{s3!.Bucket}/tiered_sample" : localArchiveRoot;
var invoiceArchive = $"{archiveRoot}/invoices";   // each root aggregate gets its own, non-overlapping path
var auditArchive = $"{archiveRoot}/audit";

// Fresh slate for the local artefacts (the S3 prefix is left as-is — overwrites are idempotent).
if (File.Exists(dbPath)) File.Delete(dbPath);
if (!useS3 && Directory.Exists(localArchiveRoot)) Directory.Delete(localArchiveRoot, recursive: true);

Console.WriteLine(useS3
    ? $"Cold archive: {archiveRoot}  (S3 endpoint '{s3!.Endpoint}')\n"
    : $"Cold archive: ./{localArchiveRoot}  (local filesystem)\n");

using (var db = new SampleContext(dbPath, invoiceArchive, auditArchive, s3))
{
    db.Database.EnsureCreated(); // also creates the control table + union views for both aggregates

    var thisMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

    // Two years of invoices, two lines each — ordinary EF writes with a child graph.
    for (var m = 24; m >= 0; m--)
    {
        var invoice = new Invoice { InvoiceDate = thisMonth.AddMonths(-m) };
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

    // Retention: on local disk we purge partitions directly; on an object store, use a bucket lifecycle rule
    // (PurgeArchiveOlderThan is not supported for remote archives).
    if (!useS3)
    {
        db.Database.PurgeArchiveOlderThan<Invoice>(thisMonth.AddMonths(-18));
        db.Database.PurgeArchiveOlderThan<AuditEvent>(thisMonth.AddMonths(-18));
        Console.WriteLine("\nPurged archive partitions older than 18 months.");
    }
    else
    {
        Console.WriteLine($"\nRetention on S3: use bucket lifecycle rules on the 'tiered_sample/' prefix.");
    }
}

Console.WriteLine(useS3
    ? "\nDone. Delete 'tiered_sample.duckdb' and the S3 'tiered_sample/' prefix to reset."
    : "\nDone. Delete 'tiered_sample.duckdb' and 'tiered_sample_archive/' to reset.");

// --- Hot model: ordinary EF Core entities. ---
internal sealed class Invoice
{
    public int Id { get; set; }
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

internal sealed class SampleContext(string dbPath, string invoiceArchive, string auditArchive, S3Options? s3) : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<InvoiceReport> InvoiceHistory => Set<InvoiceReport>();
    public DbSet<InvoiceLineReport> LineHistory => Set<InvoiceLineReport>();
    public DbSet<AuditEventReport> AuditHistory => Set<AuditEventReport>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseDuckDB($"Data Source={dbPath}");
        if (s3 is not null)
        {
            // Loads httpfs and configures object-store credentials on every connection. Because the provider
            // opens its connections through EF Core, this runs for the archive operations too.
            options.AddInterceptors(new HttpfsSetupInterceptor(s3));
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Two independent roots, each tiering on its own timestamp property — the date that defines its hot/cold boundary.
        modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, invoiceArchive, TierGranularity.Month)
            .WithReadModel<InvoiceReport>()
            .Including<InvoiceLine>(i => i.Lines, line => line.WithReadModel<InvoiceLineReport>());

        modelBuilder.ToTieredStore<AuditEvent>(e => e.OccurredOn, auditArchive, TierGranularity.Month)
            .WithReadModel<AuditEventReport>();
    }
}

// --- S3 setup: an EF Core connection interceptor that prepares DuckDB for object-store access. ---
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
