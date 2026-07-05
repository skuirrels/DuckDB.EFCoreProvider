# Tiered storage: hot DuckDB tables + cold Parquet archive

Keep recent data hot in the writable DuckDB file and roll older data out to hive-partitioned Parquet, then
report across the whole history with ordinary LINQ. Tiering works over a **relational aggregate** — an
`Invoice` and its `InvoiceLine` (and deeper) children move together — governed by the root's date. Ideal for
time-series / financial data with a small working set but long, read-only retention.

> **Runnable example:** [`samples/TieredStorage`](../samples/TieredStorage) — `dotnet run --project samples/TieredStorage`.

## The model: two sides

- **Hot** is your **ordinary EF Core model** — regular entities, normal keys and relationships, full
  `SaveChanges`, cascade, and `Include`. The provider does not remap it.
- **Cold/reporting** uses a **separate keyless read-model type per table**, mapped to a generated union view.
  You query read-models as plain `DbSet`s and join them with LINQ; the view transparently spans the hot table
  and the Parquet archive.

Cold access is **read-only reporting**. You do not `Include`-rehydrate old aggregates; you run analytical joins
across the read-models.

## How the boundary works

A single **watermark** (a stored timestamp, one per aggregate) marks the split: aggregates whose root date is
before it live in Parquet, the rest live hot. The watermark is just the cutoff of your last archive run — you
advance it by running the archive on a schedule with a rolling cutoff like `now - 1 year`. A child's tier is
inherited from its root (a child is hot iff its root is hot), so the whole aggregate always moves as a unit.

## 1. Model the aggregate

```csharp
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;

// Hot entities — just normal EF Core:
public class Invoice
{
    public int Id { get; set; }
    public DateTime InvoiceDate { get; set; }
    public List<InvoiceLine> Lines { get; set; } = [];
}
public class InvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public decimal Amount { get; set; }
}

// Cold read-models — keyless projections for reporting (columns mirror the hot entity):
public class InvoiceReport { public int Id { get; set; } public DateTime InvoiceDate { get; set; } }
public class InvoiceLineReport { public int Id { get; set; } public int InvoiceId { get; set; } public decimal Amount { get; set; } }

public class BillingContext : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();               // hot: writes + hot-only reads
    public DbSet<InvoiceReport> InvoiceHistory => Set<InvoiceReport>();     // hot + cold
    public DbSet<InvoiceLineReport> LineHistory => Set<InvoiceLineReport>(); // hot + cold

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseDuckDB("Data Source=billing.duckdb");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>()
            .HasMany(i => i.Lines).WithOne(l => l.Invoice).HasForeignKey(l => l.InvoiceId);

        modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, "/var/data/archive/invoices", TierGranularity.Month)
            .WithReadModel<InvoiceReport>()
            .Including<InvoiceLine>(i => i.Lines, line => line.WithReadModel<InvoiceLineReport>());
        // Deeper aggregates: nest another .Including(...) inside the line builder.
    }
}
```

`.Including(...)` declares which navigations are **aggregate children** (archived with the root). Anything not
included — e.g. an `InvoiceLine → Product` reference — stays hot.

## 2. Create the physical objects

`EnsureCreated()` builds the control table and all union views automatically:

```csharp
db.Database.EnsureCreated();
```

With **migrations**, call `EnsureTieredStoresCreated()` once at startup instead (idempotent):

```csharp
db.Database.Migrate();
db.Database.EnsureTieredStoresCreated();
```

## 3. Write and read

```csharp
// Writes are ordinary EF Core, including child graphs and Include over hot data:
db.Invoices.Add(new Invoice { InvoiceDate = DateTime.UtcNow, Lines = { new() { Amount = 100 } } });
db.SaveChanges();
var recent = db.Invoices.Include(i => i.Lines).Where(i => i.InvoiceDate >= DateTime.UtcNow.AddDays(-30)).ToList();

// Reporting spans hot + cold via the read-models — a plain LINQ join:
var revenueByYear =
    from line in db.LineHistory
    join invoice in db.InvoiceHistory on line.InvoiceId equals invoice.Id
    group line.Amount by invoice.InvoiceDate.Year into g
    select new { Year = g.Key, Revenue = g.Sum() };
```

## 4. Offload old aggregates

Run on a schedule (hosted service / cron) with a rolling cutoff. Root and children archive together:

```csharp
var result = await db.Database.ArchiveTierAsync<Invoice>(DateTime.UtcNow.AddYears(-1)); // + sync ArchiveTier
```

It is **idempotent and crash-safe**: the cutoff snaps down to the granularity so each period moves whole; each
table's `COPY` overwrites only the partitions it writes; the views filter the hot side to `>= watermark` (roots)
or "root is hot" (children) so reads never double-count or drop a row even before the delete runs; a re-run
self-heals leftover hot rows. Deletes run leaf→root in one transaction.

## 5. Retention

```csharp
// Delete archived partitions older than 3 years, across every aggregate table.
int partitionsDeleted = db.Database.PurgeArchiveOlderThan<Invoice>(DateTime.UtcNow.AddYears(-3));
```

## Production notes

- **Single writer.** DuckDB allows one writer. Run `ArchiveTierAsync` / `PurgeArchiveOlderThan` from the
  writing process with no other writer active (a maintenance window or the app's own scheduler).
- **Absolute archive paths.** DuckDB resolves a relative archive path against the process working directory;
  prefer an absolute path in production. Each table archives under `<archivePath>/<table>/year=…/month=…/`.
- **Reference vs child.** Only `.Including`ed navigations archive. Archived rows are **snapshots** and may hold
  foreign keys into still-live tables (an archived line still points at a `Product` that could later be
  deleted) — correct for financial history, but a semantic to be aware of.
- **Read-models mirror the hot columns.** A read-model whose column is missing on the source is rejected at
  model validation. Adding a column to the hot entity is safe (cold rows read `NULL` after the view is
  regenerated); renaming/retyping an archived column needs the affected partitions rewritten.
- **Validation.** Reserved partition column names (`year`/`month`/`day`), a child without a single-column
  foreign key to its declared parent, and overlapping aggregate archive paths are all rejected at model
  validation.
- **Child view guard.** A child row is shown as hot only when its root is hot (a semijoin), which keeps reads
  correct in the brief window if an archive crashes between its `COPY` and delete. For deep aggregates you can
  opt out with `ToTieredStore(...).WithoutHotChildFilter()` — child views become a plain `SELECT * FROM child`
  (faster) and the next archive self-heals any transient double-count.
- **Backups.** The archive directory is part of your data — back it up alongside the `.duckdb` file.
