# DuckDB.EFCoreProvider

**The full-featured Entity Framework Core 10 provider for [DuckDB](https://duckdb.org) — analytics-grade speed with the EF Core developer experience you already know.**

[![NuGet](https://img.shields.io/nuget/v/DuckDB.EFCoreProvider.svg)](https://www.nuget.org/packages/DuckDB.EFCoreProvider)
[![NuGet (NTS)](https://img.shields.io/nuget/v/DuckDB.EFCoreProvider.NTS.svg?label=NuGet%20%28NTS%29)](https://www.nuget.org/packages/DuckDB.EFCoreProvider.NTS)
[![EF Core 10](https://img.shields.io/badge/EF%20Core-10-512BD4)](https://learn.microsoft.com/ef/core/)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

[DuckDB](https://duckdb.org) is a blazing-fast, in-process **columnar (OLAP)** database — think *"SQLite for analytics."* `DuckDB.EFCoreProvider` is a **complete, native EF Core 10 provider** for it: query **and** write with LINQ, run migrations, and get order-of-magnitude write throughput — no raw SQL, no glue code, no interceptor hacks.

## Why DuckDB.EFCoreProvider?

- **A real, native provider — not a query shim.** Full LINQ translation, type mapping, migrations, and scaffolding, validated against **EF Core's own relational conformance suite**.
- **Full read *and* write.** `SaveChanges`, migrations, transactions, optimistic concurrency, and store-generated keys — the whole EF Core experience, not read-only.
- **Order-of-magnitude write throughput.** Opt-in insert / update / delete **batching (~10–20× faster)**, **`Upsert`** (~8×), and appender-backed **`BulkInsert` (~1M rows/s)**.
- **Query Parquet, CSV & JSON in place.** Map an entity straight to a file or glob with `[FromParquet]` / `[FromCsv]` / `[FromJsonFile]` and let DuckDB's vectorised engine do the scanning.
- **Tiered storage (hot + cold).** Keep recent data in the writable DuckDB file and offload older data — a relational aggregate (root + children) at a time — to hive-partitioned Parquet with `ArchiveTierAsync`. Hot stays plain EF Core; report across hot+cold with LINQ. Idempotent and crash-safe. See [Tiered storage](#tiered-storage-hot-tables--cold-parquet).
- **Rich type support.** Decimals (precision/scale), arrays & lists, JSON (incl. owned `ToJson()`), temporal, GUID, blobs, row values — plus optional **NetTopologySuite** spatial.
- **Production knobs.** `MemoryLimit`, `FileSearchPath`, configurable batch sizing, and more.

> **Scope:** DuckDB is a single-writer, embedded **analytical** engine. It's a superb fit for analytics, reporting, embedded/edge stores, and Parquet-backed querying — not a drop-in replacement for a high-concurrency OLTP server database. See [Compatibility and Scope](#compatibility-and-scope).

## Installation

Install the [DuckDB.EFCoreProvider](https://www.nuget.org/packages/DuckDB.EFCoreProvider) NuGet package:

### .NET CLI
```bash
dotnet add package DuckDB.EFCoreProvider
```

### Package Manager
```powershell
Install-Package DuckDB.EFCoreProvider
```

## Usage

> **Want to run something now?** The [`samples/Quickstart`](samples/Quickstart) console app creates a database, writes and reads data, and shows `BulkInsert` and `Upsert` — run it with `dotnet run --project samples/Quickstart`.

To use DuckDB in an Entity Framework Core application, call `UseDuckDB` in `OnConfiguring` or when configuring your `DbContext` in dependency injection.

### Basic context

```csharp
using Microsoft.EntityFrameworkCore;

public class MyDbContext : DbContext
{
    public DbSet<Blog> Blogs { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseDuckDB("Data Source=my_database.db");
    }
}

public class Blog
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Content { get; set; }
}
```

### Dependency Injection

```csharp
builder.Services.AddDbContext<MyDbContext>(options =>
    options.UseDuckDB("Data Source=my_database.db"));
```

## Configuration and connection strings

The connection string follows DuckDB.NET's format. The `Data Source` (a file path, or `:memory:`) is all most apps need:

| Goal | Connection string |
|---|---|
| **File database** (persisted) | `Data Source=app.duckdb` |
| **In-memory** (lifetime of the connection) | `Data Source=:memory:` |
| **Read-only** existing file | `Data Source=app.duckdb;Access Mode=READ_ONLY` |

> **`:memory:` lifetime** — an in-memory database lives only as long as the underlying connection is open. EF Core opens and closes connections per operation, so for a database that must persist *between* operations use a file, or keep a connection explicitly open (`context.Database.OpenConnection()`).

Provider behaviour is configured through the optional `UseDuckDB(connectionString, duckdb => …)` builder:

| Option | What it does | Default |
|---|---|---|
| `.EnableBulkInsertBatching()` | Merge consecutive `SaveChanges` inserts into one multi-row statement (~10× faster). | off |
| `.EnableBulkUpdateBatching()` | Merge eligible `SaveChanges` updates into one statement (~8× faster). | off |
| `.EnableBulkDeleteBatching()` | Merge eligible `SaveChanges` deletes into one statement (~14× faster). | off |
| `.MemoryLimit("4GB")` | Cap DuckDB's buffer-manager memory. Accepts `"512MB"`, `"75%"`, etc. | 80% of RAM |
| `.FileSearchPath("/data")` | Base directory (or comma-separated directories) for resolving relative file paths. | DuckDB default |
| `.MigrationLockTimeout(TimeSpan.FromMinutes(10))` | Maximum wait for the migrations lock before failing with guidance (use `Timeout.InfiniteTimeSpan` to wait forever). | 5 minutes |
| `.UseNetTopologySuite()` | Enable spatial support (requires the `DuckDB.EFCoreProvider.NTS` package). | off |
| `.MaxBatchSize(n)` | Tune the batching merge size (standard EF Core option). | 100 |

```csharp
options.UseDuckDB(
    "Data Source=app.duckdb",
    duckdb => duckdb
        .EnableBulkInsertBatching()
        .MemoryLimit("4GB")
        .FileSearchPath("/data,/data/archive"));
```

The batching, memory, and file-search options are explained in detail under [Performance](#performance) and [Memory limit and file search path](#memory-limit-and-file-search-path).

## Supported Features

- EF Core 10 provider integration for DuckDB through DuckDB.NET.
- `SaveChanges` inserts, updates, deletes, transactions, rollback behaviour, and optimistic concurrency checks.
- Store-generated values through DuckDB `RETURNING`, including generated keys and computed/store-generated columns.
- Auto-increment key configuration with `UseAutoIncrement()`, backed by DuckDB sequences in migrations.
- LINQ query translation for relational queries, joins, grouping, ordering, paging, aggregates, string/math/temporal operations, row values, arrays, and JSON traversal.
- Migrations for tables, columns, indexes, sequences, generated values, comments, schema/history infrastructure, and DuckDB-specific SQL generation.
- DuckDB JSON support for `string`, `JsonDocument`, `JsonElement`, and EF Core owned JSON/structural documents via `ToJson()`.
- DuckDB array/list mappings for CLR arrays and `List<T>`, including typed `INTEGER[]`-style store types.
- File-source query mapping for Parquet, CSV, and JSON through `[FromParquet]`/`[FromCsv]`/`[FromJsonFile]` (or the fluent `FromParquet`/`FromCsv`/`FromJsonFile`), emitted as DuckDB `read_parquet(...)` / `read_csv(...)` / `read_json(...)`.
- Tiered storage (`ToTieredStore(...)` + `.Including(...)`, `ArchiveTierAsync(...)`, `PurgeArchiveOlderThan(...)`): keep recent data in the DuckDB file and offload older relational aggregates (root + children) to hive-partitioned Parquet, unified by generated views; hot side is plain EF Core, cold reporting via keyless read-models; idempotent and crash-safe. See [docs/TIERED-STORAGE.md](docs/TIERED-STORAGE.md).
- High-throughput bulk insert via `context.BulkInsert(...)` / `BulkInsertAsync(...)`, backed by the DuckDB `Appender` (a raw fast path that bypasses change tracking and store-generated values).
- High-throughput upsert via `context.Upsert(...)` / `UpsertAsync(...)`, backed by batched DuckDB `INSERT ... ON CONFLICT (key) DO UPDATE` — inserts new rows and updates existing ones by primary key in one round-trip per batch (see [Upsert](#upsert)).
- Opt-in `SaveChanges` insert, update, and delete batching via `UseDuckDB(o => o.EnableBulkInsertBatching())`, `EnableBulkUpdateBatching()`, and `EnableBulkDeleteBatching()`, which merge consecutive inserts/updates/deletes into a single multi-row statement for an order-of-magnitude (up to ~20×) speed-up while keeping change tracking and store-generated keys — delete batching is especially effective for orphan cleanup and child-collection replacement (see [Faster `SaveChanges`](#faster-savechanges-inserts-updates-and-deletes)).
- NetTopologySuite spatial support with DuckDB spatial extension loading and geometry translation.
- Configurable DuckDB `memory_limit` and `file_search_path` via `UseDuckDB(o => o.MemoryLimit("4GB").FileSearchPath("/data"))` (see [Memory limit and file search path](#memory-limit-and-file-search-path)).
- Raw SQL queries and commands through EF Core relational APIs.

## Examples

### Generated keys and `RETURNING`

Use `UseAutoIncrement()` for DuckDB-backed generated integer keys. The provider generates sequence-backed migrations and uses `RETURNING` so EF Core receives generated values after `SaveChanges`.

```csharp
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;

public class OrdersContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseDuckDB("Data Source=orders.duckdb");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseAutoIncrement();
            entity.Property(e => e.CustomerReference).IsConcurrencyToken();
        });
    }
}

public class Order
{
    public int Id { get; set; }
    public required string CustomerReference { get; set; }
}
```

### Query Parquet, CSV, and JSON files

Map an entity directly to a file (or glob pattern) and DuckDB reads it as a table — no physical table required. Use `[FromParquet]`, `[FromCsv]`, or `[FromJsonFile]`; DuckDB auto-detects the CSV/JSON schema and column types. Query SQL uses `read_parquet(...)`, `read_csv(...)`, or `read_json(...)`.

```csharp
using DuckDB.EFCoreProvider.Metadata;
using Microsoft.EntityFrameworkCore;

[FromParquet("data/orders/*.parquet")]
public class OrderSnapshot
{
    public int Id { get; set; }
    public required string CustomerReference { get; set; }
}

[FromCsv("data/customers.csv")]
public class Customer
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

[FromJsonFile("data/events.json")]
public class AuditEvent
{
    public int Id { get; set; }
    public required string Kind { get; set; }
}

public class AnalyticsContext : DbContext
{
    public DbSet<OrderSnapshot> OrderSnapshots => Set<OrderSnapshot>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseDuckDB("Data Source=:memory:");
}
```

Use the fluent API instead when the path is configured at runtime:

```csharp
using DuckDB.EFCoreProvider.Extensions;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<OrderSnapshot>().FromParquet("data/orders/*.parquet");
    modelBuilder.Entity<Customer>().FromCsv("data/customers.csv");
    modelBuilder.Entity<AuditEvent>().FromJsonFile("data/events.json");
}
```

### Tiered storage (hot tables + cold Parquet)

Keep recent data hot in the writable DuckDB file and roll older data out to hive-partitioned Parquet, then
report across the whole history with ordinary LINQ. Tiering works over a **relational aggregate** — a root and
its children move together, governed by the root's date. Your **hot side stays plain EF Core** (normal
entities, `SaveChanges`, `Include`); the **cold/reporting side** uses a keyless read-model per table. The
offload is **idempotent and crash-safe**. Full guide: [docs/TIERED-STORAGE.md](docs/TIERED-STORAGE.md).

**Each root aggregate declares its own timestamp property** — the first argument to `ToTieredStore<TRoot>` (in the
example below, `i => i.InvoiceDate`). It governs the whole aggregate's hot/cold boundary and is chosen **per
aggregate**, so every tiered root can tier on a different date: `Invoice` on `InvoiceDate`, `Order` on `PlacedUtc`,
`AuditEvent` on `OccurredOn` — each independent, each with its own archive path.

```csharp
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.Metadata;

public class BillingContext : DbContext
{
    public DbSet<Invoice> Invoices => Set<Invoice>();                       // hot: writes + hot-only reads
    public DbSet<InvoiceReport> InvoiceHistory => Set<InvoiceReport>();     // hot + cold reporting
    public DbSet<InvoiceLineReport> LineHistory => Set<InvoiceLineReport>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseDuckDB("Data Source=billing.duckdb");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Invoice → InvoiceLine is discovered by convention (Lines / Invoice / InvoiceId).
        modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, "/var/data/archive/invoices", TierGranularity.Month)
            .WithReadModel<InvoiceReport>()
            .Including<InvoiceLine>(i => i.Lines, line => line.WithReadModel<InvoiceLineReport>());
    }
}
```

```csharp
db.Database.EnsureCreated();                 // also creates the control table + all union views
// (when using Migrate() instead, call db.Database.EnsureTieredStoresCreated() once at startup)

// ... write invoices with lines normally, then periodically offload aggregates older than a year:
var result = await db.Database.ArchiveTierAsync<Invoice>(DateTime.UtcNow.AddYears(-1));

var recent = db.Invoices.Count();          // hot only — just the DuckDB file
var everything = db.InvoiceHistory.Count(); // hot + cold — also scans the Parquet archive

// Cold reporting over the read-models. Single-table aggregates need no join:
var invoicesByYear = db.InvoiceHistory.GroupBy(i => i.InvoiceDate.Year).Select(g => new { g.Key, Count = g.Count() });

// Read-models are keyless (no navigations), so a cross-table report joins on the FK column; then enforce retention:
var byYear = db.LineHistory.Join(db.InvoiceHistory, l => l.InvoiceId, i => i.Id, (l, i) => new { i.InvoiceDate.Year, l.Amount })
    .GroupBy(x => x.Year).Select(g => new { g.Key, Revenue = g.Sum(x => x.Amount) });
db.Database.PurgeArchiveOlderThan<Invoice>(DateTime.UtcNow.AddYears(-3));
```

Run `ArchiveTierAsync` from a scheduled job in the writing process (DuckDB is single-writer).

> **Try it now.** The runnable [`samples/TieredStorage`](samples/TieredStorage) console app tiers two independent
> roots — an `Invoice` → `InvoiceLine` aggregate on `InvoiceDate` and an `AuditEvent` on `OccurredOn`, each on its
> own cutoff — and reports across hot + cold:
> ```bash
> dotnet run --project samples/TieredStorage          # cold archive on the local filesystem
> dotnet run --project samples/TieredStorage -- s3     # cold archive on S3 (defaults to a local MinIO)
> ```

**Cold storage on S3.** Point `archivePath` at an object-store URL (`s3://`, `gcs://`, `r2://`, `azure://`) and
DuckDB reads and writes the archive there directly — hot data in the local file, cold data on cheap durable
storage, one set of views over both. Load `httpfs` and configure credentials with a connection interceptor;
enforce retention with a bucket lifecycle rule (remote `PurgeArchiveOlderThan` is intentionally not supported).
Full guide: [Cold storage on S3](docs/TIERED-STORAGE.md#6-cold-storage-on-s3-and-other-object-stores); the
sample above runs against S3 with `-- s3`.

**Child view guard (advanced).** In an aggregate, a child row is shown as hot only when its root is on the hot
side of the boundary — a semijoin that keeps reports correct even in the brief window if an archive process
dies between writing Parquet and deleting the hot rows. For deep aggregates where that per-query guard costs
more than it's worth, opt out with `.WithoutHotChildFilter()`; child views become a plain `SELECT * FROM child`,
and the next `ArchiveTierAsync` still self-heals any transient double-count.

```csharp
modelBuilder.ToTieredStore<Invoice>(i => i.InvoiceDate, "/var/data/archive/invoices", TierGranularity.Month)
    .WithoutHotChildFilter()                        // trade the crash-window guard for faster child reads
    .WithReadModel<InvoiceReport>()
    .Including<InvoiceLine>(i => i.Lines, line => line.WithReadModel<InvoiceLineReport>());
```

### Bulk insert

For high-throughput loading, `BulkInsert` / `BulkInsertAsync` append rows directly through DuckDB's columnar `Appender` — much faster than `SaveChanges` for large batches. It is a deliberate raw fast path: it bypasses the change tracker, concurrency checks, interceptors, and store-generated values (provide a value for every mapped column), and the target table must already exist.

```csharp
using DuckDB.EFCoreProvider.Extensions;

using var context = new MyDbContext();
context.Database.EnsureCreated();

var rows = Enumerable.Range(1, 100_000)
    .Select(i => new Measurement { Id = i, Sensor = $"s{i % 10}", Value = i * 0.5 })
    .ToList();

var inserted = context.BulkInsert(rows);
// or: var inserted = await context.BulkInsertAsync(rows);
```

### Upsert

`Upsert` / `UpsertAsync` insert the supplied entities and update any whose primary key already exists, using
batched `INSERT ... ON CONFLICT (key) DO UPDATE` statements. This replaces the usual
read-then-insert-or-update pattern — it removes the existence-check round-trip and batches the writes,
running roughly an order of magnitude faster (~8× in local measurements). The conflict target is the entity's
primary key (whose values you supply), and all mapped non-key columns are overwritten from the supplied
values; an entity with only key columns becomes `ON CONFLICT DO NOTHING`.

```csharp
using DuckDB.EFCoreProvider.Extensions;

using var context = new MyDbContext();

var rows = new[]
{
    new Measurement { Id = 1, Sensor = "s1", Value = 1.5 }, // updated if Id 1 exists
    new Measurement { Id = 2, Sensor = "s2", Value = 2.5 }, // inserted if Id 2 is new
};

var processed = context.Upsert(rows);
// or: var processed = await context.UpsertAsync(rows);
// batch size is configurable: context.Upsert(rows, batchSize: 200);
```

Like `BulkInsert`, this is a raw fast path: it bypasses the change tracker, concurrency checks, and
interceptors, requires primary-key values (store-generated keys are not supported), and does not support
shadow or database-computed columns.

### JSON, owned JSON, and arrays

DuckDB JSON columns can be mapped to `string`, `JsonDocument`, or `JsonElement`. EF Core owned JSON documents are supported through `ToJson()`, and CLR arrays/lists map to DuckDB array types.

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

public class EventRecord
{
    public int Id { get; set; }
    public JsonDocument? Payload { get; set; }
    public List<int> Scores { get; set; } = [];
    public EventDetails Details { get; set; } = new();
}

public class EventDetails
{
    public string? Source { get; set; }
    public string[] Tags { get; set; } = [];
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<EventRecord>(entity =>
    {
        entity.Property(e => e.Payload).HasColumnType("JSON");
        entity.Property(e => e.Scores).HasColumnType("INTEGER[]");
        entity.OwnsOne(e => e.Details, owned => owned.ToJson());
    });
}
```

### Spatial queries

Spatial support lives in the separate `DuckDB.EFCoreProvider.NTS` project and assembly. Reference that
project/assembly to enable it. It enables the DuckDB spatial extension and maps NetTopologySuite geometry
members/methods into DuckDB spatial SQL.
Geometry is stored in DuckDB's native `GEOMETRY` column type; WKT text is used only as the wire format
to and from the driver (reads project `ST_AsWKT(...)`, writes wrap `ST_GeomFromText(...)`), because
DuckDB.NET cannot yet read the native binary geometry type directly.

```csharp
using DuckDB.EFCoreProvider.NTS.Extensions;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

builder.Services.AddDbContext<SpatialContext>(options =>
    options.UseDuckDB(
        "Data Source=spatial.duckdb",
        duckdb => duckdb.UseNetTopologySuite()));

public class Site
{
    public int Id { get; set; }
    public Point Location { get; set; } = null!;
}
```

### Memory limit and file search path

By default DuckDB sizes its buffer manager to **80% of physical RAM**. To cap that — useful when DuckDB shares
a host with other services — set `MemoryLimit`. To resolve relative file paths (for example in `[FromParquet]`)
against a base directory rather than the process working directory, set `FileSearchPath`. Both are applied as
DuckDB settings when each connection opens:

```csharp
builder.Services.AddDbContext<MyDbContext>(options =>
    options.UseDuckDB(
        "Data Source=app.duckdb",
        duckdb => duckdb
            .MemoryLimit("4GB")                  // also accepts e.g. "512MB", "75%"
            .FileSearchPath("/data,/data/archive")));  // one or more comma-separated directories
```

When not configured, DuckDB's defaults are left untouched. DuckDB spills larger-than-memory intermediates to
its temp directory, so a lower memory limit trades memory for more disk spilling on big analytical queries
rather than failing. (For an in-memory database — `Data Source=:memory:` — spilling requires a
`temp_directory`, which DuckDB does not set automatically.)

## Compatibility and Scope

This provider targets EF Core 10.0.x and .NET 10. DuckDB is an embedded analytical database, so some relational features differ from server databases. Unsupported DuckDB features should fail clearly or be documented as provider limitations.

For the full feature support matrix, the DuckDB engine limitations, and the roadmap, see
[docs/CAPABILITY-MAP.md](docs/CAPABILITY-MAP.md). For the `dotnet ef` migrations workflow and its
DuckDB-specific details, see the [migrations guide](docs/MIGRATIONS.md). See also
[CHANGELOG.md](CHANGELOG.md) for release history, [VERSIONING.md](VERSIONING.md) for the
versioning / breaking-change policy, and [SECURITY.md](SECURITY.md) for vulnerability reporting.

## Performance

DuckDB is a columnar analytical engine, which shapes performance:

- **Analytical reads** are fast — DuckDB's sweet spot.
- **Bulk loading** via `BulkInsert` / `BulkInsertAsync` (the DuckDB `Appender`) is roughly **two orders of
  magnitude faster** than looping `SaveChanges` (~1M rows/s vs ~6–8k rows/s in local measurements). Use it
  for ETL and large batches.
- **High-frequency small `SaveChanges` writes are slow** — DuckDB is not optimised for many single-row
  `INSERT` statements. This is inherent to the engine, not the provider.

### Faster `SaveChanges` inserts, updates, and deletes

DuckDB is a columnar engine, so the default one-statement-per-row path that EF Core uses for `SaveChanges` is
slow for inserts, updates, and deletes. Three independent, opt-in options merge consecutive operations within
a save into a single multi-row statement:

```csharp
options.UseDuckDB(
    "Data Source=app.duckdb",
    duckdb => duckdb
        .EnableBulkInsertBatching()   // INSERT INTO t (..) VALUES (..),(..),..
        .EnableBulkUpdateBatching()   // UPDATE t SET .. FROM (VALUES (..),(..)) ..
        .EnableBulkDeleteBatching()); // DELETE FROM t WHERE key IN (..)
```

- **An order of magnitude faster** in local measurements (all while **keeping change tracking and
  store-generated keys**, unlike `BulkInsert`):
  - inserts ~120k rows/s (vs ~12k),
  - updates ~52k rows/s (vs ~6k),
  - deletes ~119k rows/s (vs ~9k) — delete batching is especially effective for **orphan cleanup** and
    **child-collection replacement**, which issue one delete per orphaned row by default.
- **All disabled by default**, and independent (enable any combination). When enabled, each merged run is
  **atomic**: if any row in the run fails, none of the rows in that run are applied, even when `SaveChanges`
  runs without an enclosing transaction. This differs from EF Core's default behaviour (where a failed save
  without a transaction can leave earlier rows applied), which is why it is opt-in. Under EF Core's default
  `AutoTransactionBehaviour.WhenNeeded`, multi-row saves are already transactional, so most applications see no
  behavioural difference.
- **Update and delete batching apply only to safe operations** — those whose `WHERE` clause is the primary
  key (and, for updates, which read no database-computed values back). Operations that use **concurrency
  tokens** (or, for updates, **computed columns**) automatically fall back to the standard per-row path, so
  their concurrency-detection and value-propagation behaviour is unchanged.
- Tune the merge size with the standard `MaxBatchSize` option (defaults to 100, the measured sweet spot for
  DuckDB; very large batches regress).
- For pure bulk inserts that do not need change tracking or generated values, `BulkInsert` (below) is faster
  still.

### When to use `BulkInsert` (batch size)

`SaveChanges` costs ~87 µs per row (each row is a round-trip); `BulkInsert` has a small fixed per-call cost
(~200 µs once warm) and then near-free rows. So:

| Batch size | Recommendation |
|---|---|
| **< ~5 rows** | Either is fine; prefer `SaveChanges` (you keep change tracking, generated keys, concurrency). |
| **≥ ~10 rows** | Use `BulkInsert` — already several× faster, and the gap grows with size. |
| **hundreds → millions** | Use `BulkInsert` in a single call (it streams internally; chunk only to bound memory). |

Rough throughput: `BulkInsert` ≈ 1M rows/s; `SaveChanges` ≈ 6–8k rows/s. **Never loop `SaveChanges` for
bulk loads.** A BenchmarkDotNet project lives in `test/DuckDB.EFCoreProvider.Benchmarks`; see
[docs/PERFORMANCE.md](docs/PERFORMANCE.md) for full results, the crossover table, methodology, and guidance.

## Testing

The repository includes EF Core relational specification coverage plus DuckDB-specific write-provider tests. Use the test-suite script for repeatable local and CI runs:

```bash
scripts/test-suite.sh write-critical
scripts/test-suite.sh write-broad
scripts/test-suite.sh migrations
scripts/test-suite.sh updates
scripts/test-suite.sh all
scripts/test-suite.sh full-project
```

`write-critical` is the minimum gate for write-provider changes. It covers SQL generation, `RETURNING`, generated keys, optimistic concurrency, generated-key migrations, model validation, and read-only existence checks.

`all` is the complete production write-provider gate and is intended to stay green in CI. `full-project` runs the raw EF Core functional test project for broader provider-backlog discovery; it includes unrelated query/spec coverage that may fail until those areas are implemented.

## Troubleshooting and FAQ

**`IOException` / "Could not set lock on file" / "database is locked".** DuckDB is **single-writer**: only one process can open a file database for writing at a time. Close other writers (including an open DuckDB CLI, a second app instance, or a held-open connection) before writing or running migrations. Multiple **readers** are fine with `Access Mode=READ_ONLY`.

**My in-memory data disappeared.** `Data Source=:memory:` lives only while its connection is open, and EF Core opens/closes connections per operation. Use a file database, or call `context.Database.OpenConnection()` to keep one connection open for the lifetime you need. See [Configuration and connection strings](#configuration-and-connection-strings).

**My `[FromParquet]` / `[FromCsv]` relative path isn't found.** Relative paths resolve against the process working directory by default. Set a base directory with `.FileSearchPath("/data")`, or use absolute paths. See [Query Parquet, CSV, and JSON files](#query-parquet-csv-and-json-files).

**`SaveChanges` is slow for many rows.** That is inherent to DuckDB's columnar engine, not the provider. Turn on `.EnableBulkInsertBatching()` (and the update/delete equivalents) to keep change tracking while merging writes, or use `BulkInsert` / `Upsert` for raw throughput. See [Performance](#performance).

**`UseDuckDB` / `BulkInsert` / `UseAutoIncrement` not found.** Add `using DuckDB.EFCoreProvider.Extensions;`.

**`BulkInsert` or `Upsert` throws about a missing key or column.** Both are raw fast paths that bypass change tracking and store-generated values: supply primary-key values yourself (configure the key as `ValueGeneratedNever()`), provide a value for every mapped column, and ensure the target table already exists. See [Bulk insert](#bulk-insert) and [Upsert](#upsert).

**Migrations.** For the `dotnet ef` workflow, generated keys, and the DuckDB-specific `ALTER TABLE`/index limitations, see the [migrations guide](docs/MIGRATIONS.md).

**A LINQ query throws a "could not be translated" error.** DuckDB is analytical and a few relational constructs differ from server databases. Check the [capability map](docs/CAPABILITY-MAP.md); as a workaround, materialise earlier (e.g. `AsEnumerable()`) or use a raw SQL query.

## Support and Project Status

This is a community open-source project, maintained on a best-effort basis. It is **not** affiliated with, or supported by, Microsoft, the .NET Foundation, or the DuckDB project, and it does **not** come with a commercial support contract or SLA.

What that means for adopters:

- **Support is best-effort.** Issues and pull requests are handled as maintainer time allows. There is no guaranteed response or resolution time.
- **Bus factor is low.** Day-to-day maintenance currently rests with a small number of contributors. If you depend on this provider in production, plan accordingly: pin versions, vendor or fork if you need guarantees, and budget for the possibility of maintaining patches yourself.
- **Versioning.** The package follows semantic versioning. Breaking changes will be reserved for major version bumps where practical; review the release notes before upgrading.
- **Production fit.** DuckDB is an embedded, single-writer analytical (OLAP) database. This provider is a strong fit for analytics, reporting, embedded/edge stores, and Parquet-backed querying. It is **not** a drop-in replacement for a server database in high-concurrency, multi-writer OLTP scenarios. See [Compatibility and Scope](#compatibility-and-scope).

Contributions are welcome. If your organisation relies on this provider, contributing fixes, tests, and review is the most effective way to keep it healthy.

## Acknowledgments

This project began as a fork of [DuckDB.EFCore](https://github.com/denis-ivanov/DuckDB.EFCore) by [Denis Ivanov](https://github.com/denis-ivanov), and substantial portions of the code derive from that project. It is distributed under the same MIT licence; the original copyright notice is retained in [LICENSE](LICENSE).

This provider is built on top of the [DuckDB.NET](https://github.com/Giorgi/DuckDB.NET) ADO.NET provider.
