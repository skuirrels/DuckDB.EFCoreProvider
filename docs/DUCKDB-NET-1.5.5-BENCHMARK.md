# Skuirrels DuckDB.NET 1.5.5 benchmark

Date: 24 July 2026

For the publication-ready result and chart, see
[`DUCKDB-NET-1.5.5-PUBLISHABLE-SUMMARY.md`](DUCKDB-NET-1.5.5-PUBLISHABLE-SUMMARY.md).

## Verdict

The corrected benchmark shows that the optimized provider can beat Java and
Go on the prepared-command path:

| Workload | Stock DuckDB.NET 1.5.3 | Java 1.5.4.0 | Go v2.10504.0 | Optimized provider / Skuirrels 1.5.5 | Winner |
|---|---:|---:|---:|---:|---|
| Prepared command execution | 45.388 ± 1.349 µs | 13.658 ± 0.156 µs | 17.815 ± 0.288 µs | **9.261 ± 0.136 µs** | Skuirrels 1.5.5 |
| Prepared bulk insert, per row | 53.776 ± 0.492 µs | 26.981 ± 0.142 µs | 29.238 ± 0.218 µs | **19.374 ± 0.144 µs** | Skuirrels 1.5.5 |
| Public bulk / idiomatic appender, per row | 247.80 ± 2.90 ns | 171.99 ± 6.49 ns | 177.90 ± 2.56 ns | **79.14 ± 1.10 ns** | Optimized EFCoreProvider |

The public bulk row is the provider's actual
`DbContext.BulkInsert(IEnumerable<TEntity>)` API. The command and prepared
bulk rows call the provider's Skuirrels 1.5.5 dependency directly because EF
Core does not provide a prepared-command execution API of its own.

No renamed, repacked, or benchmark-only provider is involved. The benchmark
runner references the normal provider project in this workspace.

## Why the prior result was wrong

The previous comparison forced `.NET` to use `InvocationCount = 1` and
`UnrollFactor = 1`. That meant:

- .NET performed only one operation in each measured iteration;
- Java and Go repeated the operation continuously for 500 ms;
- the .NET number included a disproportionate share of cold-start and
  one-off harness overhead;
- .NET used 32-bit `INTEGER` values while Java and Go used 64-bit `BIGINT`
  values.

The corrected job removes the forced invocation count for small workloads.
Every language now uses the same three-`BIGINT` query, 64-bit values, five
500 ms warm-ups, ten 500 ms measurements, and a verified single DuckDB thread.

One invocation per iteration is still correct for the million-row appender
benchmarks, because one invocation already contains one million row appends.

## Complete comparison

Lower is better. Errors are 99.9% confidence-interval half-widths.

| Workload | DuckDB.NET 1.5.3 | Java 1.5.4.0 | Go v2.10504.0 | Skuirrels 1.5.5 | Winner |
|---|---:|---:|---:|---:|---|
| Prepared command execution | 45.388 ± 1.349 µs | 13.658 ± 0.156 µs | 17.815 ± 0.288 µs | **9.261 ± 0.136 µs** | Skuirrels 1.5.5 |
| Prepared bulk insert, per row | 53.776 ± 0.492 µs | 26.981 ± 0.142 µs | 29.238 ± 0.218 µs | **19.374 ± 0.144 µs** | Skuirrels 1.5.5 |
| Appender insert, per row | 247.80 ± 2.90 ns | 171.99 ± 6.49 ns | 177.90 ± 2.56 ns | **77.10 ± 0.69 ns** | Skuirrels 1.5.5 |
| Prepared analytical query | 2.569 ± 0.105 ms | **2.449 ± 0.041 ms** | 2.611 ± 0.078 ms | 2.493 ± 0.075 ms | No meaningful winner between Java and Skuirrels |
| Materialise 100,000 mixed rows | 10.140 ± 0.230 ms | 53.496 ± 0.829 ms | 27.601 ± 0.684 ms | **9.559 ± 0.126 ms** | Skuirrels 1.5.5 |
| TPC-H SF 0.1 Q1 | 13.819 ± 0.195 ms | **13.459 ± 0.178 ms** | 13.764 ± 0.124 ms | 13.654 ± 0.194 ms | No meaningful winner |
| TPC-H SF 0.1 Q6 | 0.939 ± 0.050 ms | 0.951 ± 0.072 ms | 0.954 ± 0.027 ms | **0.921 ± 0.033 ms** | No meaningful winner |
| TPC-H SF 0.1 Q12 | 7.711 ± 0.161 ms | **7.421 ± 0.124 ms** | 7.796 ± 0.586 ms | 7.662 ± 0.222 ms | No meaningful winner |
| TPC-H SF 0.1 Q14 | 1.656 ± 0.069 ms | 1.580 ± 0.065 ms | 1.538 ± 0.027 ms | **1.518 ± 0.011 ms** | No meaningful winner |

The provider's public `BulkInsert` measured 79.14 ± 1.10 ns/row. The direct
Skuirrels appender measured 77.10 ± 0.69 ns/row. Their 99.9% confidence
intervals overlap, so the provider has effectively reached the supporting
package's appender speed in this workload.

## `ListAppenderBenchmark`

Each case appends 1,000,000 rows containing 32 integers. This benchmark is
specific to the new .NET fork API and has no equivalent Java, Go, or
DuckDB.NET 1.5.3 lane in the source suite.

| Source value and DuckDB target | Mean | Mean per row | Managed allocation/run | Relative to array-to-LIST |
|---|---:|---:|---:|---:|
| `int[]` to `INTEGER[]` | 129.0 ± 0.4 ms | 129.0 ns | 93.73 KB | baseline |
| `List<int>` to `INTEGER[]` | 142.8 ± 1.0 ms | 142.8 ns | 93.73 KB | 10.7% slower |
| `ReadOnlyCollection<int>` to `INTEGER[]` | 166.8 ± 2.6 ms | 166.8 ns | 31,344.38 KB | 29.3% slower |
| `int[]` to `INTEGER[32]` | **123.4 ± 1.6 ms** | **123.4 ns** | 93.73 KB | 4.3% faster |

Arrays are the preferred source when the caller controls the collection
shape. The read-only collection fallback works, but allocates about 32 managed
bytes per row in this workload.

## Method

- Hardware: Apple M4 Pro, 14 logical cores.
- Operating system: macOS 26.5.2.
- .NET: SDK 10.0.300, runtime 10.0.8, Arm64 RyuJIT.
- Java: JDK 26.0.1, JMH 1.37, DuckDB JDBC 1.5.4.0.
- Go: `duckdb-go` v2.10504.0 with DuckDB 1.5.4.
- DuckDB connections: `threads = 1`, set and read back before measurement.
- Dataset sizes: 2,000,000 analytical rows; 100,000 materialisation rows;
  10,000 ingestion rows; TPC-H scale factor 0.1.
- .NET and Java: five 500 ms warm-up iterations and ten 500 ms measured
  iterations.
- Go: ten runs at 500 ms; the report uses the arithmetic mean and a 99.9%
  Student's t confidence interval across those means.
- All lanes ran sequentially on the same machine.

Stock DuckDB.NET uses DuckDB 1.5.3. Java, Go, and Skuirrels use DuckDB 1.5.4.
TPC-H therefore compares the complete driver-plus-engine combination.
Managed-allocation values are only directly comparable within one runtime.
