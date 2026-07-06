// Tiered storage over a relational aggregate: keep recent invoices (and their lines) in the writable DuckDB
// file, offload older ones to hive-partitioned Parquet, and report across hot+cold with ordinary LINQ joins.
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

var s3 = useS3 ? S3Options.FromEnvironment() : null;
var archivePath = useS3 ? $"s3://{s3!.Bucket}/tiered_sample" : "tiered_sample_archive";

// Fresh slate for the local artefacts (the S3 prefix is left as-is — overwrites are idempotent).
if (File.Exists(dbPath)) File.Delete(dbPath);
if (!useS3 && Directory.Exists(archivePath)) Directory.Delete(archivePath, recursive: true);

Console.WriteLine(useS3
    ? $"Cold archive: {archivePath}  (S3 endpoint '{s3!.Endpoint}')\n"
    : $"Cold archive: ./{archivePath}  (local filesystem)\n");

using (var db = new BillingContext(dbPath, archivePath, s3))
{
    db.Database.EnsureCreated(); // also creates the control table + union views for the whole aggregate

    // Seed two years of monthly invoices, each with two lines. These are ordinary EF writes.
    var today = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
    for (var monthsAgo = 24; monthsAgo >= 0; monthsAgo--)
    {
        var invoice = new Invoice { InvoiceDate = today.AddMonths(-monthsAgo) };
        invoice.Lines.Add(new InvoiceLine { Description = "Services", Amount = 100 + monthsAgo });
        invoice.Lines.Add(new InvoiceLine { Description = "Expenses", Amount = 25 });
        db.Invoices.Add(invoice);
    }

    db.SaveChanges();
    Console.WriteLine($"Seeded {db.Invoices.Count()} invoices with {db.Set<InvoiceLine>().Count()} lines.\n");

    // Offload everything older than one year to Parquet — root and lines move together (to disk or S3).
    var cutoff = today.AddYears(-1);
    var result = await db.Database.ArchiveTierAsync<Invoice>(cutoff);
    Console.WriteLine($"Archived {result.RowsArchived} invoices older than {cutoff:yyyy-MM}. Watermark: {result.Watermark:yyyy-MM}.\n");

    // Hot = just the DuckDB file (your normal DbSet). Tiered read-models = hot + cold Parquet.
    Console.WriteLine($"Hot invoices  (DuckDB file):  {db.Invoices.Count(),3}");
    Console.WriteLine($"All invoices  (hot + cold):   {db.InvoiceHistory.Count(),3}");

    // Reporting: a plain LINQ join across the read-models, spanning hot and cold transparently.
    var revenueByYear =
        from line in db.LineHistory
        join invoice in db.InvoiceHistory on line.InvoiceId equals invoice.Id
        group line.Amount by invoice.InvoiceDate.Year into g
        orderby g.Key
        select new { Year = g.Key, Revenue = g.Sum() };

    Console.WriteLine("\nRevenue by year (hot + cold):");
    foreach (var row in revenueByYear)
    {
        Console.WriteLine($"  {row.Year}: {row.Revenue:C}");
    }

    // Retention. On local disk we purge partitions directly; on an object store, DuckDB can't delete files,
    // so PurgeArchiveOlderThan is not supported — use a bucket lifecycle rule on the archive prefix instead.
    if (useS3)
    {
        Console.WriteLine("\nRetention on S3: use a bucket lifecycle rule on the 'tiered_sample/' prefix " +
                          "(PurgeArchiveOlderThan is not supported for remote archives).");
    }
    else
    {
        var purged = db.Database.PurgeArchiveOlderThan<Invoice>(today.AddMonths(-18));
        Console.WriteLine($"\nPurged {purged} archive partitions older than 18 months.");
        Console.WriteLine($"All invoices after purge:     {db.InvoiceHistory.Count(),3}");
    }
}

Console.WriteLine(useS3
    ? "\nDone. Delete 'tiered_sample.duckdb' and the S3 'tiered_sample/' prefix to reset."
    : "\nDone. Delete 'tiered_sample.duckdb' and 'tiered_sample_archive/' to reset.");

// --- Hot model: ordinary EF Core entities with a normal relationship (full SaveChanges + Include). ---
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

// --- Cold read-models: keyless projections used for hot+cold reporting queries. ---
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

internal sealed class BillingContext(string dbPath, string archivePath, S3Options? s3) : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceReport> InvoiceHistory => Set<InvoiceReport>();
    public DbSet<InvoiceLineReport> LineHistory => Set<InvoiceLineReport>();

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
        modelBuilder.Entity<Invoice>(b =>
        {
            b.ToTable("invoices");
            b.HasMany(i => i.Lines).WithOne(l => l.Invoice).HasForeignKey(l => l.InvoiceId);
        });
        modelBuilder.Entity<InvoiceLine>().ToTable("invoice_lines");

        modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, archivePath, TierGranularity.Month)
            .WithReadModel<InvoiceReport>()
            .Including<InvoiceLine>(i => i.Lines, line => line.WithReadModel<InvoiceLineReport>());
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
