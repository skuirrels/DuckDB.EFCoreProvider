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
    public int CustomerId { get; set; }
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
public class InvoiceReport { public int Id { get; set; } public int CustomerId { get; set; } public DateTime InvoiceDate { get; set; } }
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
        // InvoiceDate defines this aggregate's hot/cold boundary. The application separately declares
        // the physical Hive hierarchy, in exact outer-to-inner directory order.
        modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, "/var/data/archive/invoices", TierGranularity.Month)
            .PartitionBy(partitions => partitions
                .By(i => i.CustomerId)
                .ByMonth(i => i.InvoiceDate))
            .WithReadModel<InvoiceReport>()
            .Including<InvoiceLine>(i => i.Lines, line => line.WithReadModel<InvoiceLineReport>());
        // Deeper aggregates: nest another .Including(...) inside the line builder.
    }
}
```

### Generate views without duplicate CLR read models

`WithReadModel<T>()` remains useful when one context should expose a dedicated analytical projection. If the
application already has a separate historical-query context, request only the physical union views instead:

```csharp
static void ConfigureInvoicePartitions(TieredPartitionBuilder<Invoice> partitions)
    => partitions.ByMonth(invoice => invoice.InvoiceDate);

// Writable context: keyed tables, relationships, and provider-owned archive maintenance.
modelBuilder.ToTieredStore<Invoice>(invoice => invoice.InvoiceDate, archivePath)
    .PartitionBy(ConfigureInvoicePartitions)
    .WithTieredView() // inferred as invoices_tiered
    .Including<InvoiceLine>(invoice => invoice.Lines, line => line.WithTieredView());
```

`WithTieredView()` sets the same physical view metadata used by `WithReadModel<T>()`, but it does not register a
second CLR entity. It is available on roots, children, and deeper descendants. The provider creates a hot-only view
before the first archive and replaces it with the normal hot/Parquet union whenever archive publication,
reconciliation, restoration, compaction, contract rewrite, purge, or startup repair changes the active cold
representation.

The consuming application can map its existing entities in another context because EF models are context-specific:

```csharp
public sealed class BillingHistoryContext : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> Lines => Set<InvoiceLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.ToTieredView("invoices_tiered", ConfigureInvoicePartitions);
            invoice.Ignore(row => row.Lines);
        });
        modelBuilder.Entity<InvoiceLine>(line =>
        {
            line.HasNoKey();
            line.ToView("invoice_lines_tiered");
            line.Ignore(row => row.Invoice);
        });
    }
}
```

The provider owns the physical views, hot/cold suppression, archive schema checks, refresh lifecycle, and derivation
of Hive-bucket predicates in both EF models. The history context owns navigation treatment, historical joins,
authorization, tenant isolation, and query bounds. `ToTieredView(...)` makes the existing application type keyless,
maps it to the generated view, and records the complete physical partition plan in the read-only model. Reuse one
partition configuration delegate in `PartitionBy(...)` and `ToTieredView(...)` to keep the declarations aligned.
Because contexts and deployments can still evolve independently, each generated root view also exposes a
provider-owned contract marker derived from the resolved partition columns, order, transforms, aliases, and store
types. The read-only query path references that marker whenever it adds a pruning predicate. A stale or mismatched
reader therefore fails explicitly instead of applying a potentially incorrect filter. A plain `ToView(...)` mapping
still relies only on DuckDB's native pushdown and does not satisfy this pruning requirement.

An explicit unqualified name may be supplied, for example `.WithTieredView("invoice_history")`. Names cannot collide
with mapped tables, provider tier metadata tables, or another tiered entity's physical view. One descendant shared
by several archive roots has one entity-wide combined view, so repeated explicit registrations must agree. Calling
`WithTieredView(customName)` before `WithReadModel<T>()` maps the optional read model to that same custom view;
attempting to change an already registered name fails. A later deployment may create a replacement name, but the
provider does not drop the previous physical view because consumers may still depend on it.

**Each root aggregate declares its own timestamp property** — the first argument to `ToTieredStore<TRoot>` (here,
`i => i.InvoiceDate`). It governs that aggregate's entire hot/cold boundary and is chosen **per aggregate**, so
`Invoice` can tier on `InvoiceDate` and `AuditEvent` on `OccurredOn` — each independent, each
with its own archive path.

The lifecycle property may be `DateTime`, `DateOnly`, or a nullable form. A `NULL` value is never selected by `ArchiveTierAsync`, remains on
the hot side of every generated view, and can participate in `ByMonth` / `ByDay` partition declarations. Once
the application populates it, a later forward archive run selects it when the value falls inside that run's
half-open watermark window. A value populated behind an already-advanced watermark is late data and remains
hot; see the late-data policy below.

### Stable hot/cold match keys

By default each table's EF primary key suppresses retry duplicates. When that key is provider-local but the
source system has a stable identity, configure `MatchBy` independently on the root and any included child that
needs it:

```csharp
modelBuilder.ToTieredStore<Invoice>(invoice => invoice.CompletedDate, archivePath)
    .MatchBy(invoice => invoice.EdcId)
    .PartitionBy(partitions => partitions.ByMonth(invoice => invoice.CompletedDate))
    .WithReadModel<InvoiceHistory>()
    .Including<InvoiceLine>(invoice => invoice.Lines, lines => lines
        .MatchBy(line => new { line.InvoiceEdcId, line.LineNumber })
        .WithReadModel<InvoiceLineHistory>());
```

Single and composite selectors must contain direct mapped scalar properties. By default the selected layout
must be an EF key or unique index. If uniqueness is enforced by the ingestion pipeline instead, opt in explicitly
with `TierMatchKeyUniqueness.ExternallyEnforced`. Every key component must still be non-null for rows selected by
an archive run. The configured key is used consistently by hot/cold view suppression, retry cleanup, correction
detection, and aggregate children; omitting `MatchBy` preserves primary-key behavior.

The ordered `PartitionBy` builder is the application's physical layout declaration. The example produces
`CustomerId=42/InvoiceDate_month=2025-06-01/`; reversing the two calls reverses the directory hierarchy. Nothing in
the provider is tied to customer or invoice terminology: each application chooses its own mapped root properties,
their transformations, and their order:

```csharp
.PartitionBy(partitions => partitions
    .By(order => order.TenantId, "tenant_key")              // exact value
    .ByYear(order => order.CreatedAt, "created_year")        // calendar-year DATE bucket
    .ByMonth(order => order.ReviewedAt, "reviewed_month")    // calendar-month DATE bucket
    .ByDay(order => order.CompletedAt, "completed_day"))     // calendar-day DATE bucket
```

The second argument is optional. When omitted, exact partitions use the mapped column name and date buckets use
`{column}_{transform}`. When supplied, it becomes the physical Hive directory and virtual-column name while the
EF property remains the source for archive values, maintenance scopes, and LINQ query pruning. Aliases are
especially useful when a descendant maps the same column name as a root partition: for example,
`.By(order => order.OwnerId, "root_owner_id")` lets a child retain its own `OwnerId` column. Names must be
non-empty and unique within the plan (case-insensitively). A name that differs from its source must also be distinct
from mapped root columns and inherited child columns; an exact-value key may retain its own source-column name.

An explicit plan must include the lifecycle property passed to `ToTieredStore` at a granularity at least as precise
as the archive window (`ByMonth` or `ByDay` for monthly archives; `ByDay` for daily archives). This keeps incremental
`OVERWRITE_OR_IGNORE` windows disjoint and crash-safe. `ByYear` is useful for another application date, but is not
precise enough by itself to protect monthly or daily lifecycle windows.

For the common exact-key-first layout, `.PartitionBy(i => i.CustomerId)` remains shorthand for an exact
`CustomerId` key followed by an implicit bucket for the configured lifecycle property. The named form
`.PartitionBy(i => i.CustomerId, "customer_key")` produces
`customer_key=42/InvoiceDate_month=2025-06-01/`. Use the builder when the position, transformation, or lifecycle
partition name must be explicit.

Partition selectors must be direct mapped scalar properties on the aggregate root. Date transforms require
`DateTime` or `DateOnly`. Child builders deliberately have no `PartitionBy` API: every included
child obtains the root values through the existing child-to-root join and uses the same hierarchy. Use stable
identifiers and avoid high-cardinality keys that would create many small files. Exact Hive values are cast back to
their mapped DuckDB store type; derived date buckets are stored as `DATE`.

### Query pruning follows the declared metadata

Predicates stay application-shaped: query `CustomerId` and `InvoiceDate`, not the hidden Hive bucket column. For
conjunctive equality/range predicates, the provider derives the corresponding bucket predicate from the model:

```csharp
var february = db.InvoiceHistory.Where(i =>
    i.CustomerId == customerId
    && i.InvoiceDate >= new DateTime(2025, 2, 1)
    && i.InvoiceDate < new DateTime(2025, 3, 1));
```

With `CustomerId` then `InvoiceDate` month configured, DuckDB can prune to that customer's February directory.
This works unchanged for `WithReadModel<T>()` in the tier-owning context and equivalently for an existing entity
mapped by `ToTieredView(...)` in a separate read-only context. The same applies when those Hive keys are aliased;
the provider adds predicates for the configured physical names. Before applying those read-only predicates, it also
references the owner-generated partition-contract marker. If the owner and reader resolve different physical plans,
or the owner view has not yet been refreshed after upgrading to a contract-aware provider version, DuckDB reports the
missing provider marker instead of returning an incomplete result.
If the date predicate is omitted, it scans that customer's completed-date partitions; if the customer predicate is
omitted, it scans the matching month across customers. Predicates beneath `OR` are deliberately not inferred,
because adding a bucket filter there could change query semantics.

`EXPLAIN ANALYZE` reports the result-bearing Parquet pruning as `Scanning Files: X/Y`. The crash/late-row guard may
also show a separate key/timestamp probe over the cold files; that probe preserves the no-duplicate invariant and
does not materialize the report's full projected columns.

`.Including(...)` declares which navigations are **aggregate children** (archived with the root). Anything not
included — e.g. an `InvoiceLine → Product` reference — stays hot.

### Sharing one child entity across independent roots

The same physical child entity/table may be included beneath more than one independently archived root. Each
`.Including(...)` creates a deterministic root-scoped binding, so archive, reconciliation, restoration,
compaction, inventory, and cleanup continue to use the selected root's control key and archive path:

Every configured root must use a unique `controlKey`, because the provider persists its watermark, active
generation, and archive contract under that key.

```csharp
modelBuilder.ToTieredStore<ImportJob>(
        job => job.CompletedAt,
        "s3://archive/imports",
        controlKey: "imports")
    .WithReadModel<ImportJobHistory>()
    .Including<JobEvent>(job => job.Events, events => events.WithReadModel<JobEventHistory>());

modelBuilder.ToTieredStore<ExportJob>(
        job => job.CompletedAt,
        "s3://archive/exports",
        controlKey: "exports")
    .WithReadModel<ExportJobHistory>()
    .Including<JobEvent>(job => job.Events, events => events.WithReadModel<JobEventHistory>());
```

The provider creates one stable child view containing the hot table once plus every published root-specific
Parquet branch. Configuration order and archive order do not change that view. `MatchBy(...)` belongs to the
shared entity, so its key must be globally stable across all of those branches.

Each physical child row must belong to **at most one** configured root binding. If, for example, one `JobEvent`
has both an import-job FK and an export-job FK that resolve to existing roots, storage preflight reports
`TierStorageCapability.BindingOwnership` as unsupported and mutating tier operations throw
`TierAmbiguousBindingException` before writing Parquet or deleting hot rows.

For metadata inspection, call `GetTieredStoreBindings()` on the child entity type. The older singular getters
(`GetTieredStoreRoot()`, `GetTieredStoreParent()`, `GetTieredStoreParentNavigation()`,
`GetTieredStoreArchivePath()`, and `GetTieredStoreControlKey()`) remain compatible for a single binding and
return `null` when the child is shared.

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
table's `COPY` overwrites only the partitions it writes; root views separate normal hot rows from late/backdated
rows and suppress a hot row whose key is already cold, while child views use "root is hot", so reads never
double-count or drop a row even before the delete runs; a re-run
self-heals leftover hot rows. Deletes run leaf→root as separate autocommit statements because DuckDB checks
foreign keys immediately and rejects deleting descendants and then their principals inside one transaction.
Do not wrap `ArchiveTierAsync` in an application transaction. Parquet writes are external side effects and cannot
be rolled back with the DuckDB transaction, so the provider rejects that combination before copying. A crash
between delete statements remains safe and a rerun removes leftovers.

`TierArchiveResult` contains the published watermark transition and table-level evidence: the deterministic
root binding, selected, verified-copy, and deleted row counts; the archive window; active generation/revision;
archive paths and visible Parquet files; no-op state; and the final workflow stage. A failure after preflight
throws `TierArchiveOperationException`, whose
`Stage` and `PartialResult` show how far the operation reached without including object-store credentials.
Use `TierManifestOptions` to return summary-only evidence, a bounded representative set, or all paths. Writer
controls are strongly typed through `TierParquetWriterOptions` (compression, compression level, row-group sizing,
and partition-safe filename patterns). DuckDB's file-rotation and per-thread controls cannot be combined with the
partitioned `COPY` used by tiered storage, so the provider does not expose them.

### Late rows, corrections, and reopened aggregates

Ordinary archive runs only advance the watermark. A previously unseen row whose lifecycle value arrives behind
that watermark stays visible and hot. A practical first defence is to delay the cutoff by one complete period.
For daily archives, running after midnight with yesterday's midnight as the cutoff archives through the day before
yesterday, leaving a full extra day for delayed ingestion. For monthly archives, running on the second day of the
month with the first day as the cutoff gives the completed month an extra day. This grace period reduces late rows;
it does not catch a row that arrives after the watermark has already advanced.

Before an archive or no-op retry changes data, the provider compares hot rows whose stable key already exists in
published Parquet. In simple terms:

- Same key and same data means a harmless retry duplicate; cleanup can remove the hot copy.
- Same key and different data means a correction; normal archiving stops and leaves the hot copy untouched.
- A previously unseen key below the watermark is late data; normal archiving leaves it hot.
- Clearing or moving the lifecycle date of an archived root means reopening it; reconciliation rejects that
  because the whole aggregate must first be restored to the hot model.

After the application has approved the hot corrections and late rows, run the explicit reconciliation operation:

```csharp
TierArchiveResult reconciliation =
    await db.Database.ReconcileArchiveTierAsync<Invoice>(); // + sync ReconcileArchiveTier
```

Reconciliation reads the complete active cold range, lets hot rows win by the configured stable match key, writes
a **new immutable Parquet generation**, verifies every node's row count, and atomically switches the control row and
generated views to that generation. Only then does it delete hot rows whose complete representation matches the
published generation. It never overwrites the currently active generation in place, so a failure before publication
leaves readers on the old generation and a failure after publication is safe to retry. The previous generation is
retained under the archive prefix for recovery. Cleanup must consult `active_archive_path` in
`__duckdb_tier_control`, retain the active generation plus the required rollback/audit set, and delete or tag only
generations proven obsolete. Do not apply blind age expiry to the entire `_revisions/` prefix: the active generation
also lives there after reconciliation and could otherwise be deleted.

Reconciliation is a key-wise upsert, not an inferred graph deletion. A hot child replaces a cold child with the
same configured match key, and a new hot child is added, but the absence of a hot child does not delete its cold
representation. When the application has authorised an actual deletion, supply exact configured match keys:

```csharp
await db.Database.ReconcileArchiveTierAsync<Invoice>(
    new TierReconciliationOptions
    {
        Scope = TierMaintenanceScope.ForRootMatchKeys(
            TierRowIdentity.For<Invoice>(
                new Dictionary<string, object?> { ["InvoiceNumber"] = "INV-1001" })),
        Tombstones =
        [
            TierRowIdentity.For<InvoiceLine>(
                new Dictionary<string, object?>
                {
                    ["InvoiceNumber"] = "INV-1001",
                    ["LineNumber"] = 2,
                }),
        ],
    });
```

`Scope` may instead be a leading prefix of the declared root partition plan. Tombstones can identify roots or
children; a root tombstone is propagated through the declared cold aggregate so the replacement generation stays
graph-consistent. The provider validates technical identity and publication safety only—the application decides
whether deletion is genuine and authorised.

### Restoration, diagnostics, compaction, and contract migration

Restore an exact root-key or declared-partition scope to the mapped hot tables before performing a domain state
transition:

```csharp
TierRestoreResult restore = await db.Database.RestoreArchiveTierAsync<Invoice>(
    new TierRestoreOptions
    {
        Scope = TierMaintenanceScope.ForRootMatchKeys(
            TierRowIdentity.For<Invoice>(
                new Dictionary<string, object?> { ["InvoiceNumber"] = "INV-1001" })),
    });
```

The provider copies the root and declared children parents-first, rejects differing existing hot
representations, and then publishes a replacement cold generation that omits exactly that root scope. The hot
inserts and active-generation metadata/view switch commit in one internal DuckDB transaction; if publication
fails, the hot inserts roll back and the previously active generation remains selected. The immutable Parquet
candidate can remain unpublished for inventory and caller-controlled cleanup. Restore does not clear or otherwise
change the lifecycle property.

Operational reads are bounded:

```csharp
TierConflictPage conflicts =
    await db.Database.GetArchiveConflictsAsync<Invoice>(offset: 0, limit: 100);

TierArchiveGenerationInventory inventory =
    await db.Database.GetArchiveGenerationInventoryAsync<Invoice>();

TierArchiveCleanupPlan cleanup =
    await db.Database.PlanArchiveGenerationCleanupAsync<Invoice>(selectedGenerationIds);

TierStoragePreflightResult preflight =
    await db.Database.PreflightTieredStorageAsync<Invoice>();
```

Inventory classifies the active generation, prior provider-published generations, and locally discoverable
unpublished candidates. Cleanup planning is read-only: retention, legal hold, rollback depth, and actual object
deletion remain application/deployment policy. For remote active generations, generated views use the provider's
persisted exact file catalogue when available and fall back compatibly to recursive glob discovery.
Archive results, restoration publication results, inventory, cleanup plans, and preflight results expose
non-secret `Binding` evidence (`BindingId`, root CLR type, and control key) so callers can prove which root-scoped
operation was performed.

`CompactArchiveTierAsync` rewrites the complete active cold range into a verified immutable generation using new
writer controls. Archive schema changes use a separate reviewable flow:

```csharp
TierArchiveContractInspection inspection =
    await db.Database.InspectArchiveContractAsync<Invoice>();

TierArchiveRewritePlan plan =
    await db.Database.PlanArchiveContractRewriteAsync<Invoice>(
        new TierArchiveRewriteOptions
        {
            Columns =
            [
                new TierArchiveColumnRewrite
                {
                    EntityType = typeof(Invoice),
                    TargetProperty = nameof(Invoice.Currency),
                    ConstantValue = "GBP",
                },
            ],
        });

TierArchiveResult migration =
    await db.Database.RewriteArchiveContractAsync<Invoice>(plan);
```

Nullable additions need no mapping. Required additions, renamed/retyped columns, and nullability tightening require
an explicit source column or constant. Aggregate-layout, match-key, lifecycle, path, granularity, and partition
changes are rejected as ambiguous rather than inferred.

## 5. Retention

```csharp
// Delete archived partitions older than 3 years, across every aggregate table.
int partitionsDeleted = db.Database.PurgeArchiveOlderThan<Invoice>(DateTime.UtcNow.AddYears(-3));
```

## 6. Cold storage on S3, Google Cloud Storage, and other object stores

The cold tier does not have to be a local disk. Point `archivePath` at an object-store URL — `s3://`,
`gcs://`/`gs://`, `r2://`, or `azure://` — and DuckDB reads and writes the Parquet there directly via its
`httpfs` (or `azure`) extension. Recent data stays in the local `.duckdb` file; the archive lives on cheap,
durable object storage; the union views query across both.

The archive `COPY`, incremental partition writes, idempotent re-runs, and the crash-safety invariant use the same
provider path for every remote scheme. Reads stay efficient: hive-partition pruning plus Parquet row-group
statistics mean a range-scoped report fetches only the byte ranges it needs, not whole files.

**Setup.** Load `httpfs` and configure credentials through the provider-owned connection initializer. The
initializer runs after configured extensions load and its commands bypass EF command logging, keeping secret
values out of generated SQL logs.

```csharp
options.UseDuckDB("Data Source=app.duckdb", duckdb => duckdb
    .LoadExtension("httpfs")
    .ConfigureConnection(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE OR REPLACE SECRET s3 (TYPE s3, PROVIDER credential_chain, REGION 'eu-west-2');
            """; // or resolve KEY_ID/SECRET here; do not put credentials in the EF model
        command.ExecuteNonQuery();
    }));

modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, "s3://my-bucket/archive/invoices", TierGranularity.Month)
    .WithReadModel<InvoiceReport>()
    .Including<InvoiceLine>(i => i.Lines, l => l.WithReadModel<InvoiceLineReport>());
```

**Google Cloud Storage.** GCS also uses `httpfs`, but selects DuckDB's `TYPE gcs` secret and `gcs://` or `gs://`
URLs. DuckDB accesses GCS through its S3 interoperability API, so production credentials are an HMAC access ID
and secret rather than an ordinary OAuth service-account JSON file or Application Default Credentials. Create
the bucket first, keep credentials outside the EF model, and install/load `httpfs` on every connection as above.

```csharp
options.UseDuckDB("Data Source=app.duckdb", duckdb => duckdb
    .LoadExtension("httpfs")
    .ConfigureConnection(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE OR REPLACE SECRET gcs (
                TYPE gcs,
                KEY_ID 'your-hmac-access-id',
                SECRET 'your-hmac-secret'
            );
            """; // resolve these values from a secret store; do not put them in the EF model
        command.ExecuteNonQuery();
    }));

modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, "gcs://my-bucket/archive/invoices", TierGranularity.Month)
    .WithReadModel<InvoiceReport>()
    .Including<InvoiceLine>(i => i.Lines, l => l.WithReadModel<InvoiceLineReport>());
```

The sample's local GCS mode points this exact `TYPE gcs` + `gcs://` path at MinIO. That proves the DuckDB
interoperability protocol and tiered archive round-trip, but MinIO is not a GCS emulator and cannot validate
Google IAM, HMAC provisioning, quotas, or GCS-specific service behavior. Keep at least one integration test
against a real, disposable GCS bucket for those concerns.

**Azure Blob Storage.** Azure uses a **different DuckDB extension** — `azure`, not `httpfs` — so configuration
loads `azure` and creates an `azure`-typed secret. Point the archive at an `az://`, `azure://`, or `abfss://`
(ADLS Gen2) URL. The archive `COPY` (hive-partitioned, `OVERWRITE_OR_IGNORE`) and the `read_parquet` glob behave
the same as on S3 — verified against Azurite, the Azure Storage emulator.

```csharp
options.UseDuckDB("Data Source=app.duckdb", duckdb => duckdb
    .LoadExtension("azure")
    .ConfigureConnection(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE OR REPLACE SECRET az (TYPE azure, PROVIDER credential_chain, ACCOUNT_NAME 'mystorageacct');
            """; // or CONNECTION_STRING, service_principal, or managed_identity
        command.ExecuteNonQuery();
    }));

modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, "azure://my-container/archive/invoices", TierGranularity.Month)
    .WithReadModel<InvoiceReport>()
    .Including<InvoiceLine>(i => i.Lines, l => l.WithReadModel<InvoiceLineReport>());
```

**Retention on object storage.** Object stores can't delete files through DuckDB, so
`PurgeArchiveOlderThan` throws `NotSupportedException` for a remote archive. Enforce retention with the store's
own age-based expiry instead — an **S3 bucket lifecycle rule**, **GCS Object Lifecycle Management**, or an
**Azure Blob lifecycle-management policy** on the archive prefix (for example, expire objects under
`archive/invoices/` after 7 years). These policies normally use object creation/upload age. If retention must
follow the partition's business date, especially for backfilled data, use a prefix-aware external cleanup job.

**Try it.** The sample ships a compose file with MinIO and Azurite. MinIO serves both the local S3 mode and the
GCS interoperability mode, using separate buckets; Azurite serves Azure:

```bash
docker compose -f samples/TieredStorage/docker-compose.yml up -d
dotnet run --project samples/TieredStorage -- s3      # archive to S3 (MinIO)
dotnet run --project samples/TieredStorage -- gcs     # TYPE gcs + gcs:// archive (MinIO)
dotnet run --project samples/TieredStorage -- azure   # archive to Azure Blob (Azurite)
```

It targets MinIO / Azurite by default. Override `TIER_S3_*`, `TIER_GCS_*`, or `TIER_AZURE_*` to point at a real
cloud store. For real GCS, set `TIER_GCS_ENDPOINT` to an empty value so DuckDB uses the default Google endpoint,
then set `TIER_GCS_KEY_ID`, `TIER_GCS_SECRET`, and `TIER_GCS_BUCKET` to an HMAC key and an existing bucket:

```bash
TIER_GCS_ENDPOINT="" \
TIER_GCS_KEY_ID="$GCS_HMAC_ACCESS_ID" \
TIER_GCS_SECRET="$GCS_HMAC_SECRET" \
TIER_GCS_BUCKET="my-archive-bucket" \
dotnet run --project samples/TieredStorage -- gcs
```

**Failure/retry acceptance matrix.** Run the provider's dedicated MinIO lane with:

```bash
scripts/test-tiered-storage-s3.sh
```

It covers first archive, no-op rerun, restart/read, failure after copy, failure after control/view publication,
partial child deletion, reconciliation publication failure, and nullable-column schema evolution. The same test
class runs against a unique disposable real-AWS prefix when `DUCKDB_AWS_S3_TEST_BUCKET` is set. Optional variables
are `DUCKDB_AWS_S3_TEST_PREFIX`, `DUCKDB_AWS_S3_TEST_REGION`, and the paired
`DUCKDB_AWS_S3_TEST_KEY`/`DUCKDB_AWS_S3_TEST_SECRET` plus `DUCKDB_AWS_S3_TEST_SESSION_TOKEN`; when explicit keys
are omitted, DuckDB's AWS credential chain is used. Configure lifecycle expiry on the test prefix because the
provider intentionally does not delete remote objects.

**How remote reads stay cheap.** A scoped cold query prunes to the partitions it needs and range-reads only the
Parquet byte ranges within them — it never downloads whole files. `EXPLAIN ANALYZE` shows the pruning, e.g.
`Scanning Files: 1/60` for a single-month query. To measure this on your own S3 / Azure endpoints (cold vs warm
latency, files scanned, S3 vs Azure side by side), point [`scripts/bench-remote-read.sh`](../scripts/bench-remote-read.sh)
at your buckets/containers (it's configured entirely by `BENCH_*` environment variables — see the script header).

## Production notes

- **Single writer.** DuckDB allows one writer. Run archive, reconciliation, restoration, compaction, contract
  rewrite, and `PurgeArchiveOlderThan` from the
  writing process with no other writer active (a maintenance window or the app's own scheduler).
- **Absolute archive paths.** DuckDB resolves a relative archive path against the process working directory;
  prefer an absolute path in production. Each table archives under
  `<archivePath>/<table>/<declared-key-1>=…/<declared-key-2>=…/` (or the legacy temporal layout when no
  `PartitionBy` is configured).
- **Reference vs child.** Only `.Including`ed navigations archive. Archived rows are **snapshots** and may hold
  foreign keys into still-live tables (an archived line still points at a `Product` that could later be
  deleted) — correct for financial history, but a semantic to be aware of.
- **Views project the hot columns.** Physical views are generated from every mapped scalar source column whether or
  not an EF read model is registered. A read-model whose column is missing on the source is rejected at
  model validation. Adding a column to the hot entity is safe (cold rows read `NULL` after the view is
  regenerated) when the new column is nullable; removing, renaming, retyping, or adding a required archived
  column needs the affected archive migrated.
- **Validation.** Missing/duplicate partition properties, non-date bucket transforms, an explicit plan without a
  safe lifecycle bucket, partitions declared anywhere except the aggregate root, physical-name collisions with
  root or child columns, duplicate or empty root control keys, an invalid child foreign key to its declared
  parent, multiple relationship paths from one root to the same physical child entity, and overlapping
  aggregate archive paths are rejected at model validation. Changing partition order, transforms, granularity,
  archive path, match keys, include layout, mapped names, or mapped store types after cold files exist requires
  rewriting or clearing that aggregate's archive. The provider records the complete versioned partition and
  aggregate contract before copying files, so crash-orphaned or mixed layouts are rejected rather than opened
  with an incompatible schema. Separate `ToTieredView(...)` contexts additionally validate their resolved pruning
  contract against a provider-owned marker in the physical view whenever pruning is applied.
- **Child view guard.** A child row is shown as hot only when its root is hot (a semijoin), which keeps reads
  correct in the brief window if an archive crashes between its `COPY` and delete. For deep aggregates you can
  opt out with `ToTieredStore(...).WithoutHotChildFilter()` — child views become a plain `SELECT * FROM child`
  (faster) and the next archive self-heals any transient double-count.
- **Shared-child ownership.** A child table can participate in multiple independently archived roots, but each
  physical row must resolve to at most one binding. Preflight and mutating operations reject ambiguous ownership
  before external writes or hot deletion.
- **Backups.** The archive directory is part of your data — back it up alongside the `.duckdb` file.
