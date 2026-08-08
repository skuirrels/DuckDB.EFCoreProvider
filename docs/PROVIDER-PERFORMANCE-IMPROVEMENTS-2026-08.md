# Provider performance improvements — August 2026

## Executive summary

This change set implements the reviewed provider performance recommendations against exact
`origin/main` commit `341efacc18729e2ca43ba03652454f762027196e`. It reduces allocation in the
`SaveChanges` planning path, makes Upsert staging reusable and width-aware, prevents oversized wide-row
batches, coalesces connection-initialization commands, and replaces the second tiered-storage Parquet
probe with a persisted active-generation key index.

The strongest repeatable results are allocation and physical-I/O reductions:

- SaveChanges insert-plan allocation is **12,040.59 KB at baseline** and **7,771.36 KB currently**;
  update-plan allocation is **7,390.00 KB at baseline** and **4,976.05 KB currently**.
- The merge-decision workload is **28.41 MB at baseline** and **16.42 MB currently**.
- Controlled Upsert allocation ranges from **283.37–434.79 KB at baseline** and **133.40–217.27 KB currently**.
- The scoped tiered query opens **194 files at baseline** and **2 files currently**.
- The complete functional project passed: **25,748 total, 21,611 passed, 4,137 skipped, 0 failed**.

Latency results are included as measured, but the short local run has wide confidence intervals on several
native database operations. Allocation and file counts are the more stable comparison signals.

## Implemented changes

### SaveChanges planning and batching

- Scalar insert/update planning no longer creates LINQ candidate arrays before determining whether STRUCT
  handling is needed.
- Each pending insert run resolves one immutable physical-shape descriptor and reuses it for candidate checks
  and final plan construction; SQL generation no longer revalidates the completed run.
- Dual-role update classification is reused between batch eligibility, append checks, and final planning.
- Bulk-insert plans retain the already-owned EF modifications during immediate SQL generation instead of
  allocating one detached snapshot object per cell.
- A 10,000-cell batch ceiling complements the existing parameter and SQL-length guards, so `MaxBatchSize`
  cannot create disproportionately large statements for wide entities.
- Debug diagnostics now report the operation, row count, column/cell count, parameter count, SQL length,
  and flush/rejection reason through `DuckDBEventId.SaveChangesBatch`.

### Appender and Upsert

- BulkInsert and Upsert share one compiled typed Appender row-writer implementation.
- Directly supported scalar properties use typed `AppendValue` calls; converter and shadow/fallback paths
  retain the provider-value conversion behavior.
- Upsert creates one temporary staging table per operation, clears and reuses it between chunks, and drops
  it in the final cleanup path.
- The default Upsert batch size increased from 100 to 500 after the controlled batch-size sweep.
- The effective Upsert chunk is capped by a 100,000-cell budget, preventing a caller-supplied row limit from
  becoming unsafe for very wide entities.

### Connection initialization

- Configured DuckDB `SET` statements are issued as one command instead of one command per setting.
- Spatial `INSTALL` and `LOAD` are issued together in sync and async connection-open paths.

### Tiered storage

- The provider metadata schema now includes an active-generation root-key index and its generation/watermark
  state.
- Generation publication, startup recovery, retention, and purge rebuild, reuse, or clear the key index with
  the active cold generation.
- The hot/cold no-duplicate guard probes the compact DuckDB key index instead of opening the full cold
  Parquet catalogue a second time.
- Exact scan-fraction regression assertions ensure the query plan contains only the intended result-bearing
  Parquet scan.

## Baseline/current benchmark comparison

All baseline/current measurements used the same machine and runtime: Apple Silicon, macOS 26.5.2,
.NET SDK 10.0.300, .NET 10.0.8, BenchmarkDotNet 0.15.8, in-process toolchain, one warmup, and three
measurement iterations. Lower is better. Allocation and file counts are the most stable signals in this
short run. The current column was refreshed with the complete 19-case corrected-code rerun on
9 August 2026; the baseline remains the matching exact `origin/main` measurement.

| Workload | Baseline (`origin/main`) | Current |
|---|---:|---:|
| SaveChanges insert batching | 61.468 ms; 12,040.59 KB | 51.354 ms; 7,771.36 KB |
| SaveChanges update batching | 52.256 ms; 7,390.00 KB | 51.625 ms; 4,976.05 KB |
| Merge-decision insert batching | 143.176 ms; 28.41 MB | 69.970 ms; 16.42 MB |
| Raw BulkInsert Appender | 6.665 ms; 58.15 KB | 6.273 ms; 59.86 KB |
| Array parameter materialization | 5.633 ms; 340.57 KB | 4.845 ms; 340.58 KB |
| Upsert using each version's default | 23.037 ms; 312.79 KB (batch 100) | 9.461 ms; 134.73 KB (batch 500) |
| First scoped tier query | 244.502 ms; 70.63 KB; 194 files | 32.761 ms; 65.52 KB; 2 files |

The default-Upsert row is an end-to-end comparison of the old and new defaults. The controlled sweep below
holds batch size constant and isolates the staging-table and compiled-writer changes.

## Controlled Upsert batch-size comparison

Each invocation upserts 1,000 three-column rows into a table preloaded with 500 matching keys.

| Batch size | Baseline (`origin/main`) | Current |
|---:|---:|---:|
| 25 | 64.330 ms; 434.79 KB | 45.301 ms; 217.27 KB |
| 100 | 24.084 ms; 313.88 KB | 14.392 ms; 148.05 KB |
| 500 | 12.013 ms; 283.61 KB | 7.984 ms; 133.40 KB |
| 1,000 | 9.184 ms; 283.37 KB | 7.113 ms; 135.00 KB |

Batch size 500 is the better default: it is close to the 1,000-row latency in this run, avoids choosing the
maximum public batch size as the default, and remains bounded by the new cell budget for wide models.

## New regression benchmark coverage

These figures characterize the optimized implementation and remain as repeatable regression workloads.
They are not equivalent-workload before/after comparisons.

| Scenario | Baseline | Current |
|---|---:|---:|
| Open default connection | Not collected | 3.224 ms; 47.78 KB |
| Open connection with three settings | Not collected | 3.434 ms; 51.41 KB |
| SaveChanges, 700 four-column rows | Not collected | 19.68 ms; 3.48 MB |
| SaveChanges, 700 sixteen-column rows | Not collected | 132.29 ms; 9.61 MB |

## Correctness and compatibility validation

| Gate | Result |
|---|---:|
| Focused native, tiered-storage, Upsert, and connection review suite | 145 passed; 0 failed |
| Complete functional test project | 21,611 passed; 4,137 skipped; 0 failed; 25,748 total |
| Production write-provider filter | 721 passed; 465 skipped; 0 failed; 1,186 total |
| Release solution build | 0 warnings; 0 errors |
| Changed-file formatting verification | Passed |
| Release package and public API compatibility validation | Passed; package produced successfully |

The maintained `scripts/test-suite.sh all` production gate is the passing 1,186-test result above. Native
DuckDB and DuckLake Upsert paths are included in the complete project. Tiered storage is validated and
benchmarked on native DuckDB, where that feature is supported.
Package validation emitted the repository's existing unresolved-reference notices for the optional
`SQLitePCLRaw` assemblies in both comparison packages, but completed with no compatibility error.

## Reproduction

```bash
dotnet test test/DuckDB.EFCoreProvider.FunctionalTests/DuckDB.EFCoreProvider.FunctionalTests.csproj \
  -c Release

scripts/test-suite.sh all

dotnet run -c Release --project test/DuckDB.EFCoreProvider.Benchmarks -- \
  --filter '*AllocationBenchmarks*' '*HotPathReviewBenchmarks*' \
  '*TieredCatalogueScaleBenchmarks*' '*ConnectionInitializationBenchmarks*' \
  '*UpsertBatchSizeBenchmarks*' '*SaveChangesWidthBenchmarks*' \
  --inProcess --warmupCount 1 --iterationCount 3
```

For release-quality latency claims, increase the warmup and iteration counts and repeat the A/B run in
fresh processes. The three-iteration run is deliberately sufficient for implementation feedback and
allocation/I/O regression evidence, but it is not a production throughput or SLA measurement.
