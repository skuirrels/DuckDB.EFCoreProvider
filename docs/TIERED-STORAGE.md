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
        // i => i.InvoiceDate is this aggregate's timestamp property: the date that defines its hot/cold boundary (the watermark).
        modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, "/var/data/archive/invoices", TierGranularity.Month)
            .WithReadModel<InvoiceReport>()
            .Including<InvoiceLine>(i => i.Lines, line => line.WithReadModel<InvoiceLineReport>());
        // Deeper aggregates: nest another .Including(...) inside the line builder.
    }
}
```

**Each root aggregate declares its own timestamp property** — the first argument to `ToTieredStore<TRoot>` (here,
`i => i.InvoiceDate`). It governs that aggregate's entire hot/cold boundary and is chosen **per aggregate**, so
`Invoice` can tier on `InvoiceDate`, `Order` on `PlacedUtc`, `AuditEvent` on `OccurredOn` — each independent, each
with its own archive path.

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

Writes are ordinary EF Core — child graphs, `SaveChanges`, `Include` over hot data:

```csharp
db.Invoices.Add(new Invoice { InvoiceDate = DateTime.UtcNow, Lines = { new() { Amount = 100 } } });
db.SaveChanges();
var recent = db.Invoices.Include(i => i.Lines).Where(i => i.InvoiceDate >= DateTime.UtcNow.AddDays(-30)).ToList();
```

Cold reporting queries the read-models, which span hot + cold. Most reports are single-read-model aggregates
that need no join:

```csharp
var invoicesByYear = db.InvoiceHistory
    .GroupBy(i => i.InvoiceDate.Year)
    .Select(g => new { Year = g.Key, Count = g.Count() });
```

The read-models are **keyless** (mapped to views), so they carry no navigation properties. A report that spans
two tables therefore joins on the foreign-key column — there is no `invoice.Lines` to navigate on the cold side:

```csharp
var revenueByYear = db.LineHistory
    .Join(db.InvoiceHistory, l => l.InvoiceId, i => i.Id, (l, i) => new { i.InvoiceDate.Year, l.Amount })
    .GroupBy(x => x.Year)
    .Select(g => new { Year = g.Key, Revenue = g.Sum(x => x.Amount) });
```

### Avoiding the join: denormalize the report columns

If a cross-table join is on a hot reporting path and you'd rather not pay it, **denormalize** the parent column
onto the child. A read-model can only project columns that exist on its source table, so the copy has to live on
the **hot child entity** — carry the parent's date on the line and populate it when you write:

```csharp
public class InvoiceLine
{
    public int Id { get; set; }
    public int InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime InvoiceDate { get; set; } // denormalized from the invoice at write time
}
public class InvoiceLineReport
{
    public decimal Amount { get; set; }
    public DateTime InvoiceDate { get; set; } // now available on the line read-model
}

// Revenue by year is now a single-read-model aggregate — no join:
var revenueByYear = db.LineHistory
    .GroupBy(l => l.InvoiceDate.Year)
    .Select(g => new { Year = g.Key, Revenue = g.Sum(l => l.Amount) });
```

The trade-off is storage and consistency: the duplicated column lives in the hot table **and** every archived
Parquet file, and you are responsible for keeping it in sync on write (it's a snapshot — safe for immutable
history like invoices, riskier for values that change after the fact). Denormalize the few columns your hot
reports need; join for the rest.

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

## 6. Cold storage on S3 (and other object stores)

The cold tier does not have to be a local disk. Point `archivePath` at an object-store URL — `s3://`,
`gcs://`/`gs://`, `r2://`, or `azure://` — and DuckDB reads and writes the Parquet there directly via its
`httpfs` (or `azure`) extension. Recent data stays in the local `.duckdb` file; the archive lives on cheap,
durable object storage; the union views query across both.

The archive `COPY`, incremental partition writes, idempotent re-runs, and the crash-safety invariant all behave
identically on S3 (verified against MinIO). Reads stay efficient: hive-partition pruning plus Parquet row-group
statistics mean a range-scoped report fetches only the byte ranges it needs, not whole files.

**Setup.** Load `httpfs` and configure credentials on every connection with a connection interceptor. The
provider opens its connections through EF Core, so the interceptor runs for the archive operations too.

```csharp
public sealed class HttpfsSetup : DbConnectionInterceptor
{
    private const string Sql = """
        INSTALL httpfs; LOAD httpfs;
        CREATE OR REPLACE SECRET s3 (TYPE s3, PROVIDER credential_chain, REGION 'eu-west-2');
        """; // or KEY_ID/SECRET, or ENDPOINT/URL_STYLE 'path'/USE_SSL false for MinIO
    public override void ConnectionOpened(DbConnection c, ConnectionEndEventData e)
    { using var cmd = c.CreateCommand(); cmd.CommandText = Sql; cmd.ExecuteNonQuery(); }
    public override async Task ConnectionOpenedAsync(DbConnection c, ConnectionEndEventData e, CancellationToken ct = default)
    { await using var cmd = c.CreateCommand(); cmd.CommandText = Sql; await cmd.ExecuteNonQueryAsync(ct); }
}

options.UseDuckDB("Data Source=app.duckdb").AddInterceptors(new HttpfsSetup());

modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, "s3://my-bucket/archive/invoices", TierGranularity.Month)
    .WithReadModel<InvoiceReport>()
    .Including<InvoiceLine>(i => i.Lines, l => l.WithReadModel<InvoiceLineReport>());
```

`s3://`, `gcs://`/`gs://`, and `r2://` all go through the `httpfs` extension shown above.

**Azure Blob Storage.** Azure uses a **different DuckDB extension** — `azure`, not `httpfs` — so the interceptor
loads `azure` and creates an `azure`-typed secret. Point the archive at an `az://`, `azure://`, or `abfss://`
(ADLS Gen2) URL. The archive `COPY` (hive-partitioned, `OVERWRITE_OR_IGNORE`) and the `read_parquet` glob behave
the same as on S3 — verified against Azurite, the Azure Storage emulator.

```csharp
public sealed class AzureSetup : DbConnectionInterceptor
{
    private const string Sql = """
        INSTALL azure; LOAD azure;
        CREATE OR REPLACE SECRET az (TYPE azure, PROVIDER credential_chain, ACCOUNT_NAME 'mystorageacct');
        """; // or CONNECTION_STRING '...', or PROVIDER service_principal / managed_identity
    public override void ConnectionOpened(DbConnection c, ConnectionEndEventData e)
    { using var cmd = c.CreateCommand(); cmd.CommandText = Sql; cmd.ExecuteNonQuery(); }
    public override async Task ConnectionOpenedAsync(DbConnection c, ConnectionEndEventData e, CancellationToken ct = default)
    { await using var cmd = c.CreateCommand(); cmd.CommandText = Sql; await cmd.ExecuteNonQueryAsync(ct); }
}

options.UseDuckDB("Data Source=app.duckdb").AddInterceptors(new AzureSetup());

modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, "azure://my-container/archive/invoices", TierGranularity.Month)
    .WithReadModel<InvoiceReport>()
    .Including<InvoiceLine>(i => i.Lines, l => l.WithReadModel<InvoiceLineReport>());
```

**Retention on object storage.** Object stores can't delete files through DuckDB, so
`PurgeArchiveOlderThan` throws `NotSupportedException` for a remote archive. Enforce retention with the store's
own age-based expiry instead — an **S3 bucket lifecycle rule** or an **Azure Blob lifecycle-management policy** on
the archive prefix (for example, expire objects under `archive/invoices/` after 7 years). The layout is
hive-partitioned by period, so age-based expiry maps cleanly onto it.

**Try it.** The sample ships a compose file with a local MinIO (S3) and Azurite (Azure), so the remote modes are
two commands each — and they exercise the real `ArchiveTierAsync` against those emulators (verified end to end):

```bash
docker compose -f samples/TieredStorage/docker-compose.yml up -d
dotnet run --project samples/TieredStorage -- s3      # archive to S3 (MinIO)
dotnet run --project samples/TieredStorage -- azure   # archive to Azure Blob (Azurite)
```

It targets that MinIO by default; override the `TIER_S3_*` environment variables to point at real S3.

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
