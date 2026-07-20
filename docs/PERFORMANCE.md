# DuckDB.EFCoreProvider — Performance

This document summarises the provider's performance characteristics, how to reproduce the measurements,
and an honest assessment of where it is fast and where it is slow.

## How to run the benchmarks

A [BenchmarkDotNet](https://benchmarkdotnet.org/) project lives in
`test/DuckDB.EFCoreProvider.Benchmarks`:

```bash
# all benchmarks
dotnet run -c Release --project test/DuckDB.EFCoreProvider.Benchmarks -- --filter *

# just the write comparison
dotnet run -c Release --project test/DuckDB.EFCoreProvider.Benchmarks -- --filter *WriteBenchmarks*
```

- `WriteBenchmarks` — `SaveChanges` (per-statement `INSERT … RETURNING`) vs `BulkInsert` (Appender).
- `ReadBenchmarks` — no-tracking read / filter materialisation.

## Indicative results

Measured on a developer laptop (.NET 10, DuckDB.NET 1.5.3, file-backed database, explicit keys so both
write paths insert identical data). These are **indicative** wall-clock numbers from a quick harness, not a
rigorous BenchmarkDotNet report — run the project above for statistically robust figures on your hardware.

### Writes

| Operation | 10,000 rows | 100,000 rows | Throughput |
|---|---|---|---|
| `SaveChanges` (INSERT … RETURNING) | ~1,660 ms | ~12,500 ms | ~6,000–8,000 rows/s |
| `BulkInsert` (Appender) | ~21 ms | ~90 ms | ~0.5–1.1 M rows/s |
| **Speed-up** | **~80×** | **~140×** | |

Disabling EF change detection (`AutoDetectChangesEnabled = false`) did **not** materially change the
`SaveChanges` time (≈2,290 ms vs ≈2,350 ms for 20k rows). The bottleneck is therefore the **SQL write path**
— many small `INSERT` statements against DuckDB — not EF Core's change tracker.

### Reads

| Operation | 10,000 rows | 100,000 rows |
|---|---|---|
| `AsNoTracking().ToList()` | ~75 ms | ~50 ms |

Read latency is dominated by fixed per-query overhead at small sizes; throughput scales well (100k rows
materialised in tens of milliseconds).

## Assessment — good or bad?

**Reads: good.** DuckDB's columnar/vectorised engine plus EF Core materialisation handles analytical
read workloads comfortably. This is the intended use and it performs well.

**Bulk writes via `BulkInsert`: excellent.** The Appender path reaches ~1 M rows/s and is the right tool
for loading/ETL. Roughly **two orders of magnitude** faster than `SaveChanges`.

**`SaveChanges` writes: poor for volume — and inherently so.** DuckDB is an analytical engine optimised for
bulk/columnar operations, not for many small row-at-a-time `INSERT` statements. EF Core's `SaveChanges`
cannot use the Appender (it needs `RETURNING`, store-generated keys, and concurrency checks), so its write
throughput (~6–8k rows/s) is fundamentally limited by DuckDB's per-statement cost. This is not a fixable
provider defect; it is a property of using an OLAP engine for OLTP-style writes.

### Practical guidance

- **Loading / ETL / large batches** → use `BulkInsert` / `BulkInsertAsync` (see the README). Do not loop
  `SaveChanges`.
- **Analytical reads / reporting** → expected to perform well; this is DuckDB's sweet spot.
- **High-frequency, small transactional writes (OLTP)** → expect poor throughput. This reinforces the
  guidance in [`CAPABILITY-MAP.md`](CAPABILITY-MAP.md): DuckDB (and therefore this provider) is not
  suited to OLTP system-of-record workloads.

## Batch size: when does `BulkInsert` pay off?

`BulkInsert` has a small **fixed per-call cost** and then a very low per-row cost; `SaveChanges` has no
fixed cost but a high per-row cost (~87 µs/row — each row is effectively a round-trip). So the question is
where the fixed cost is amortised. Per-call cost by batch size (best-of-N, pre-opened connection):

| Rows | `SaveChanges` | `BulkInsert` | Winner |
|---|---|---|---|
| 1 | ~280 µs | ~225 µs | ~break-even |
| 5 | ~700 µs | ~225 µs | Bulk ~3× |
| 10 | ~1,030 µs | ~195 µs | Bulk ~5× |
| 50 | ~3,800 µs | ~195 µs | Bulk ~20× |
| 100 | ~8,270 µs | ~200 µs | Bulk ~42× |
| 1,000 | ~88,500 µs | ~487 µs | Bulk ~180× |
| 5,000 | ~567,000 µs | ~1,840 µs | Bulk ~300× |

(1 µs = one microsecond = 1/1000 ms.)

**Fixed cost.** The first `BulkInsert` for a given entity type resolves the physical column order and
builds the column accessors (~600 µs, one DuckDB catalog query). That work is **cached per entity type +
table**, so subsequent calls drop to ~200 µs of fixed cost (appender setup). The rows themselves are nearly
free up to several hundred.

**Guidance on size:**

- **< ~5 rows** → either path is fine; `SaveChanges` is comparable and gives you change tracking,
  store-generated keys, and concurrency. Prefer it unless you specifically don't need those.
- **≥ ~10 rows** → use `BulkInsert`. It is already several times faster and the gap widens with size.
- **hundreds → millions** → `BulkInsert`, in a **single call** if it fits in memory. The appender streams
  and flushes internally, so there is no speed reason to chunk; chunk (e.g. 50k–100k) only to bound memory
  when streaming from a large source.
- **Never loop `SaveChanges`** for volume — at ~87 µs/row, 100k rows is ~12 s versus ~90 ms for `BulkInsert`.

## Caveats

- Numbers above are from a single machine and a quick harness; treat them as orders of magnitude, not
  precise figures. Use the BenchmarkDotNet project for rigorous, reproducible measurement.
- `BulkInsert` is a raw fast path: no change tracking, concurrency, EF command interceptors, or store-generated
  values. It does emit bounded provider start/completion/failure diagnostics for the overall operation. Use
  `SaveChanges` when you need the EF update pipeline.
