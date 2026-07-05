// Tiered storage over a relational aggregate: keep recent invoices (and their lines) in the writable DuckDB
// file, offload older ones to hive-partitioned Parquet, and report across hot+cold with ordinary LINQ joins.
//
// Run it:  dotnet run --project samples/TieredStorage

using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;

const string dbPath = "tiered_sample.duckdb";
const string archivePath = "tiered_sample_archive";

if (File.Exists(dbPath)) File.Delete(dbPath);
if (Directory.Exists(archivePath)) Directory.Delete(archivePath, recursive: true);

using (var db = new BillingContext(dbPath, archivePath))
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

    // Offload everything older than one year to Parquet — root and lines move together.
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

    // Retention: drop archived partitions older than 18 months across every aggregate table.
    var purged = db.Database.PurgeArchiveOlderThan<Invoice>(today.AddMonths(-18));
    Console.WriteLine($"\nPurged {purged} archive partitions older than 18 months.");
    Console.WriteLine($"All invoices after purge:     {db.InvoiceHistory.Count(),3}");
}

Console.WriteLine("\nDone. Delete 'tiered_sample.duckdb' and 'tiered_sample_archive/' to reset.");

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

internal sealed class BillingContext(string dbPath, string archivePath) : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceReport> InvoiceHistory => Set<InvoiceReport>();
    public DbSet<InvoiceLineReport> LineHistory => Set<InvoiceLineReport>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseDuckDB($"Data Source={dbPath}");

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
