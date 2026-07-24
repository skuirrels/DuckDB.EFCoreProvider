# DuckDB .NET 1.5.5 benchmark summary

![Corrected DuckDB cross-driver benchmark](images/duckdb-cross-driver-benchmark-chart.svg)

## Headline result

The corrected 24 July run uses the normal optimized EFCoreProvider source,
the same three-`BIGINT` workload in every language, one verified DuckDB thread,
and five 500 ms warm-ups followed by ten 500 ms measurements.

The optimized provider wins both headline scenarios:

1. **Prepared command:** 9.261 ± 0.136 µs, 32.2% faster than Java and
   48.0% faster than Go.
2. **Public `DbContext.BulkInsert`:** 79.14 ± 1.10 ns/row, 54.0% faster than
   Java's idiomatic appender and 55.5% faster than Go's.
3. **Prepared bulk insert:** 19.374 ± 0.144 µs/row through the provider's
   Skuirrels 1.5.5 dependency, also faster than Java and Go.

No benchmark-only provider was built. The provider lane is a normal project
reference to the current source.

## Easy comparison

| Scenario | Java | Go | Optimized EFCoreProvider | Stock DuckDB.NET 1.5.3 | Winner |
|---|---:|---:|---:|---:|---|
| Prepared command | 13.658 µs | 17.815 µs | **9.261 µs*** | 45.388 µs | **EFCoreProvider dependency** |
| Prepared insert / row | 26.981 µs | 29.238 µs | **19.374 µs*** | 53.776 µs | **EFCoreProvider dependency** |
| Public bulk/appender / row | 171.99 ns | 177.90 ns | **79.14 ns** | 247.80 ns | **Optimized EFCoreProvider** |
| Prepared analytics | **2.449 ms** | 2.611 ms | 2.493 ms* | 2.569 ms | Java / provider dependency statistical tie |
| Materialise 100,000 rows | 53.496 ms | 27.601 ms | **9.559 ms*** | 10.140 ms | **EFCoreProvider dependency** |

`*` Direct-driver workload through the provider's Skuirrels DuckDB.NET 1.5.5
dependency. Only the public bulk row measures `DbContext.BulkInsert`.

## New `ListAppenderBenchmark`

Each test appends 1,000,000 rows containing 32 integers:

1. **`int[]` to fixed `INTEGER[32]`:** 123.4 ± 1.6 ns/row
2. **`int[]` to `INTEGER[]`:** 129.0 ± 0.4 ns/row
3. **`List<int>` to `INTEGER[]`:** 142.8 ± 1.0 ns/row
4. **`ReadOnlyCollection<int>` to `INTEGER[]`:** 166.8 ± 2.6 ns/row

Arrays remain the best source collection when the caller controls the shape.
The read-only collection fallback works, but allocates about 32 managed bytes
per row.

## Why the benchmark changed

The prior .NET job forced one invocation per measured iteration. That produced
only ten cold .NET calls while Java and Go looped continuously for five
seconds. The scalar types also differed between languages.

The corrected benchmark lets each harness batch the small workload naturally
for the same 500 ms duration and uses 64-bit values everywhere. One-invocation
jobs are retained only for the million-row appender cases where one call is
already a complete substantial workload.

## Provenance

- Provider: normal project reference to the current optimized
  `DuckDB.EFCoreProvider` source; no renamed or repacked provider.
- Apple M4 Pro, macOS 26.5.2.
- .NET 10.0.8 / BenchmarkDotNet.
- Java JDK 26.0.1 / JMH 1.37 / DuckDB JDBC 1.5.4.0.
- Go `duckdb-go` v2.10504.0 / DuckDB 1.5.4.
- Benchmark date: 24 July 2026.

Full statistics and methodology:
[`DUCKDB-CROSS-DRIVER-BENCHMARK.md`](DUCKDB-CROSS-DRIVER-BENCHMARK.md).
