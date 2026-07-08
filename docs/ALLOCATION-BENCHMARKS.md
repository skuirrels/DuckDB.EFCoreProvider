# Allocation Benchmarks

This records the before/after allocation run for provider-owned allocation reductions.

Command:

```bash
dotnet run -c Release --no-restore --project test/DuckDB.EFCoreProvider.Benchmarks -- --filter '*AllocationBenchmarks*' --job short --warmupCount 1 --iterationCount 3 --inProcess
```

The run uses BenchmarkDotNet `ShortRun` with `MemoryDiagnoser`. `--inProcess` is intentional here:
the default out-of-process BenchmarkDotNet generated project stalled in its build step on this machine,
while in-process produced stable enough allocation snapshots for this targeted comparison.

Environment:

- macOS Tahoe 26.5.1
- .NET SDK 10.0.300
- Runtime .NET 10.0.8
- BenchmarkDotNet 0.15.8

## Results

### Allocation Cleanup

| Benchmark | Before allocated | After allocated | Delta |
|---|---:|---:|---:|
| `SaveChangesInsertBatching` | 9117.30 KB | 8941.22 KB | -176.08 KB |
| `BulkInsertAppender` | 1004.65 KB | 854.53 KB | -150.12 KB |
| `UpsertBatch` | 2562.56 KB | 2203.38 KB | -359.18 KB |
| `ArrayParameterEnumerable` | 348.53 KB | 348.53 KB | 0 KB |

| Benchmark | Before mean | After mean |
|---|---:|---:|
| `SaveChangesInsertBatching` | 55.906 ms | 55.036 ms |
| `BulkInsertAppender` | 8.043 ms | 7.883 ms |
| `UpsertBatch` | 43.204 ms | 45.706 ms |
| `ArrayParameterEnumerable` | 5.889 ms | 5.747 ms |

Notes:

- `ArrayParameterEnumerable` did not move in the EF query benchmark. The code change still avoids copying
  runtime values that are `List<T>` subclasses in `DuckDBArrayTypeMapping.CreateParameter`, but this query
  path appears to normalize the captured collection before the mapping branch.
- These are short local allocation snapshots, not release-quality performance claims. Use longer jobs for
  publication-grade numbers.

### Upsert Temp-Table Staging

Follow-up command:

```bash
dotnet run -c Release --no-restore --project test/DuckDB.EFCoreProvider.Benchmarks -- --filter '*AllocationBenchmarks.UpsertBatch*' --job short --warmupCount 1 --iterationCount 3 --inProcess
```

This compares the post-allocation-cleanup parameterized upsert path against the temp-table/appender upsert
path for the same 1,000-row benchmark.

| Benchmark | Before allocated | After allocated | Delta |
|---|---:|---:|---:|
| `UpsertBatch` | 2203.38 KB | 1046.66 KB | -1156.72 KB |

| Benchmark | Before mean | After mean | Delta |
|---|---:|---:|---:|
| `UpsertBatch` | 45.706 ms | 24.455 ms | -46.5% |
