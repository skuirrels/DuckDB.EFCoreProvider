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

# referenced-principal update regression
dotnet run -c Release --project test/DuckDB.EFCoreProvider.Benchmarks -- --filter *ReferencedPrincipalUpdateBenchmarks*

# provider allocation, adaptive width, connection, Upsert, and tier-query regressions
dotnet run -c Release --project test/DuckDB.EFCoreProvider.Benchmarks -- --filter \
  '*AllocationBenchmarks*' '*HotPathReviewBenchmarks*' '*SaveChangesWidthBenchmarks*' \
  '*ConnectionInitializationBenchmarks*' '*UpsertBatchSizeBenchmarks*' \
  '*TieredCatalogueScaleBenchmarks*'
```

- `WriteBenchmarks` — `SaveChanges` (per-statement `INSERT … RETURNING`) vs `BulkInsert` (Appender).
- `BulkInsertHotPathBenchmarks` — warmed provider `BulkInsert` vs direct
  `CreateRow`, scoped, and reusable Skuirrels appender paths.
- `ReadBenchmarks` — no-tracking read / filter materialisation.
- `ReferencedPrincipalUpdateBenchmarks` — referenced-table update correctness and
  the provider workaround's cost, with an unreferenced update as a regression guard.
- `AllocationBenchmarks` and `HotPathReviewBenchmarks` — SaveChanges planning and allocation guards.
- `SaveChangesWidthBenchmarks` — narrow and wide SaveChanges workloads for adaptive cell-limit coverage.
- `ConnectionInitializationBenchmarks` — default and multi-setting connection-open paths.
- `UpsertBatchSizeBenchmarks` — controlled staging-table reuse across batch sizes 25 through 1,000.
- `TieredCatalogueScaleBenchmarks` — tier catalogue, regeneration, pruning, and scoped-query costs.
- `CommandPlanExtractionBenchmarks`, `ParameterPathBenchmarks`, `SaveChangesParameterBenchmarks`, and
  `SqlGenerationPathBenchmarks` — regression coverage for scalar terminal command extraction, provider command-plan
  metadata, parameterized queries, and deterministic identifier metadata.

The implementation comparison for these provider improvements is recorded in
[`PROVIDER-PERFORMANCE-IMPROVEMENTS-2026-08.md`](PROVIDER-PERFORMANCE-IMPROVEMENTS-2026-08.md).

## LINQ provider follow-up regression guard

The command-plan implementation was compared with its exact base commit
`2025f34384d1249dfb46a2a3ddd289e08c31735f` on the same Apple Silicon machine,
.NET 10.0.8, and Skuirrels DuckDB.NET 1.5.5.3/native DuckDB 1.5.5. BenchmarkDotNet
used five warmups and fifteen measurements through the in-process toolchain.
Lower is better; `±` is the 99.9% confidence-interval half-width.

The first implementation registered every provider parameter in a
`ConditionalWeakTable`. The focused benchmark demonstrated a material shared-path
regression, so metadata capture was changed to a nested synchronous scope that is
active only while command-plan extraction creates its `DbCommand`.

| Parameters created | Exact base | Initial feature | Final scoped capture | Allocations: base / initial / final | Winner |
|---:|---:|---:|---:|---:|---|
| 1 | 23.67 ± 0.27 ns | 143.8 ± 2.19 ns | 23.08 ± 0.09 ns | 152 / 176 / 152 B | No meaningful winner: base vs final |
| 5 | 116.00 ± 0.55 ns | 678.6 ± 1.51 ns | 115.56 ± 0.79 ns | 760 / 880 / 760 B | No meaningful winner: base vs final |
| 20 | 457.10 ± 1.72 ns | 2,748.4 ± 38.17 ns | 448.45 ± 1.99 ns | 3,040 / 3,520 / 3,040 B | No meaningful winner: base vs final |

End-to-end execution remained native-engine dominated and final allocations
matched the base within a few bytes:

| Scenario | Exact base | Final scoped capture | Allocations: base / final | Winner |
|---|---:|---:|---:|---|
| Parameterized query, 1 parameter | 3.447 ± 0.059 ms | 3.354 ± 0.051 ms | 7,569 / 7,547 B | No meaningful winner |
| Parameterized query, 5 parameters | 3.577 ± 0.035 ms | 3.422 ± 0.029 ms | 12,431 / 12,442 B | Final scoped capture |
| Parameterized query, 20 parameters | 3.577 ± 0.043 ms | 3.559 ± 0.064 ms | 33,938 / 33,905 B | No meaningful winner |
| `SaveChanges`, 100 six-column rows | 21.41 ± 3.31 ms | 19.95 ± 1.43 ms | 901.62 / 901.62 KB | No meaningful winner |

Fifteen alternating fresh-process probes measured one-time startup paths. The
first baseline identifier sample was discarded as an OS-cache outlier; the table
reports medians for the remaining fourteen baseline and all fifteen feature runs.

| Startup path | Exact base median | Final feature median | Managed allocation: base / final | Winner |
|---|---:|---:|---:|---|
| First reserved identifier | 29.904 ms | 2.125 ms | 220.25 / 269.97 KB | Final feature |
| Warm reserved identifier | 99.90 ± 0.94 ns | 21.77 ± 0.16 ns | 40 / 40 B | Final feature |
| Five-property model startup | 157.932 ms | 157.863 ms | 1,291.55 / 1,350.04 KB | No meaningful winner |

Deterministic identifier metadata therefore trades about 50–59 KB of one-time
managed startup allocation for eliminating the scratch DuckDB connection and
system-table query. It does not add steady-state allocation and substantially
reduces both first-use and warmed identifier latency.

Reproduce the retained benchmarks with:

```bash
dotnet run -c Release --project test/DuckDB.EFCoreProvider.Benchmarks -- \
  --filter '*ParameterPathBenchmarks*' '*SqlGenerationPathBenchmarks*' \
  --inProcess --warmupCount 5 --iterationCount 15

dotnet run -c Release --project test/DuckDB.EFCoreProvider.Benchmarks -- \
  --filter '*SaveChangesParameterBenchmarks*' \
  --inProcess --warmupCount 3 --iterationCount 10

dotnet run -c Release --project test/DuckDB.EFCoreProvider.Benchmarks -- --cold-sql-probe
dotnet run -c Release --project test/DuckDB.EFCoreProvider.Benchmarks -- --model-startup-probe
```

For the current package-level and cross-language comparison, including the
one-million-row `ListAppenderBenchmark`, see
[`DUCKDB-NET-1.5.5-BENCHMARK.md`](DUCKDB-NET-1.5.5-BENCHMARK.md).
For the publication-ready Java, Go, optimized EFCoreProvider, and stock
DuckDB.NET 1.5.3 comparison, see
[`DUCKDB-CROSS-DRIVER-BENCHMARK.md`](DUCKDB-CROSS-DRIVER-BENCHMARK.md).

## Referenced-principal update fix

The benchmark compares the published `v1.15.0` source at commit
`7ee363bd1e9f6181a67eb7dee5c552f7813f8f23` with the final `v1.15.1` Release
build on the same Apple Silicon machine, .NET 10.0.8, Skuirrels DuckDB.NET
1.5.5.2/native DuckDB 1.5.5, and file-backed databases. Each measurement
iteration performs 25 tracked `SaveChanges` updates; the table has a computed
column so the provider must refresh a store-generated value. Results use five
warmups and fifteen measurements. Lower is better; `±` is BenchmarkDotNet's
99.9% confidence-interval half-width.

| Scenario | v1.15.0 | v1.15.1 fix | Change | Winner |
|---|---:|---:|---:|---|
| Referenced table, no dependent row | 5.885 ± 0.261 ms; 10.26 KB | 4.543 ± 0.246 ms; 13.50 KB | 22.8% lower mean; 3.24 KB more allocation | **v1.15.1 fix** |
| Referenced table, dependent row | Fails with a foreign-key constraint error | 4.384 ± 0.089 ms; 13.68 KB | Correctness restored | **v1.15.1 fix** |
| Unreferenced table regression guard | 5.348 ± 0.349 ms; 9.37 KB | 4.479 ± 0.122 ms; 9.59 KB | 16.2% lower mean in this run; 0.22 KB more allocation | **No meaningful winner** |

The valid before/after referenced-table comparison shows that replacing
`UPDATE ... RETURNING` with `UPDATE` plus a keyed `SELECT` does not impose a
latency regression on this DuckDB version; its mean was lower in this run.
The workaround allocates about 3.2 KB more per update because it executes a
second command. The unreferenced path remains on `RETURNING`; its 0.22 KB per
25-update invocation increase is approximately nine bytes per `SaveChanges`
and reflects the lightweight capability-aware batch state, with no bulk-list
allocation. Its lower cross-run latency is not attributed to the fix, so the
regression-guard row has no meaningful winner.

The exact final artifacts are:

- `DuckDB.EFCoreProvider.1.15.1.nupkg` SHA-256:
  `ae267ed87b54097e3daad6ddd49b4d504528e0d2de08fdd0afaba8eb3b4df570`
- `DuckDB.EFCoreProvider.dll` SHA-256:
  `2a4826e6730761fc4fbf55f0efa02611d87309439973903c553e674c5679e4c4`
- raw BenchmarkDotNet CSV and logs:
  [`benchmark-data/referenced-principal-update-2026-07-30`](benchmark-data/referenced-principal-update-2026-07-30/README.md)

## Historical DuckDB.NET 1.5.3 indicative results

Measured on a developer laptop (.NET 10, DuckDB.NET 1.5.3, file-backed database, explicit keys so both
write paths insert identical data). These are **indicative** wall-clock numbers from a quick harness, not a
rigorous BenchmarkDotNet report. They are retained as the pre-1.14.1 baseline;
use the current cross-driver report above for the 1.14.1 release result.

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
compiles the row writer (~600 µs in this historical run, including one DuckDB catalog query). That work is **cached per entity type +
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
