# Performance Review Follow-ups — Benchmarked Assessment

Date: 24 August 2026
Assessed commit: `b1ac329` (`v1.22.0`, exact `origin/main`)
Benchmark source: [`PerformanceReviewFollowUpBenchmarks.cs`](../test/DuckDB.EFCoreProvider.Benchmarks/PerformanceReviewFollowUpBenchmarks.cs)

Implementation status (24 August 2026): U-1, U-5, U-2's explicit opt-in contract, and M-2 are implemented in
the working tree with focused native and DuckLake regression coverage. Production validation results should be
recorded in the delivery handoff rather than inferred from the pre-implementation measurements below.

## Verdict

The original review found several real costs, but its priority order was not evidence-based. Four changes are
now supported strongly enough to implement, one is a useful Parquet-specific roadmap item, and the remaining
items should be opportunistic, deferred, or rejected.

Implement next:

1. **U-1 — inline supported `ValueConverter` expressions into the compiled Appender writer.** Three
   value-type converters allocate 144 bytes per row in the real provider path; the isolated converter cost is
   48 bytes per converted value. The implementation must retain the current fallback for field-only or
   unsupported provider types.
2. **U-5 — replace the correlated cardinality guard with an atomic set-based guard inside the same `MERGE`.**
   The safe single-statement candidate is 42–84% faster in the measured target-size range and rejects a
   duplicate target before mutation. The original two-statement proposal should not be used because it opens a
   validation-to-mutation race unless a transaction is mandatory.
3. **U-2 — add an explicitly opt-in distinct-input contract that bypasses the window.** It reduces the native
   merge statement by 28–30% for unique 10,000- and 50,000-row staging sets. The deterministic last-input-wins
   behavior must remain the default.
4. **M-2 — pass supported `IReadOnlyList<T>` values directly to DuckDB.NET.** Direct binding was functionally
   verified and removes a 2 KB defensive copy for the measured 500-element parameter. The latency result is
   directionally positive but its confidence intervals overlap, so allocation—not speed—is the acceptance
   reason.

Roadmap, with a narrower contract:

- **U-6 — Parquet-to-table ingestion.** A pre-existing Parquet source loaded at about 12.0 million rows/s in
  this four-column workload versus about 5.36 million rows/s for already-materialized objects through
  `BulkInsert`. This supports a mapped Parquet API after mapping, identifier, schema, path, transaction, and
  diagnostics contracts are designed. It does not prove the untested Arrow proposal or a universal
  10–50 million rows/s claim.

Do not prioritize:

- **U-3 — generic Upsert plan delegate:** typed and object delegates both measured about 0.347 ns with no
  allocation. A reference cast is not boxing and does not justify making the immutable Upsert plan generic.
- **U-4 — automatic internal transaction:** the complete provider path was 106.01 ms with per-batch
  auto-commit and 96.95 ms in one explicit transaction, but the confidence intervals overlap. More
  importantly, changing the default would change the documented partial-commit/atomicity contract. Keep the
  caller-owned transaction behavior; consider a separately named atomic option only for semantics.
- **S-1 — array pooling:** the isolated pool operation saves an allocation, but the immutable plans own their
  arrays until rendering completes. No provider-level result demonstrates a worthwhile gain, and safe leasing
  would add lifecycle complexity.
- **C-1 — attachment-verification cache:** the probe costs about 77.6 microseconds and 1.23 KB. That is too
  small to justify cache invalidation risk without a measured open-heavy DuckLake or encrypted-database
  workload.
- **M-1 — affinity-rule loop:** the loop is clearly cheaper, but this is a model/type-mapping fallback rather
  than a row hot path. Take the simple cleanup opportunistically, not as performance-priority work.

## Corrections to the original review

- `CheckpointThreshold("1GB")` already exists and is documented in the README; it is not a new follow-up.
- The document announced a Query Pipeline category but contained no query-pipeline finding.
- “Every row undergoes casting” in U-3 was true but misleading: the reference cast neither boxes nor allocates.
- The measured four-column Appender path reached about 5.36 million rows/s, so the stated universal
  1.5–3 million rows/s Appender range was too low for this repository and machine.
- U-5's “exponential slowdown” was not established. The current guard is target-size-sensitive and materially
  slower, but only the measured points below are supported.
- “Sub-millisecond execution for steady-state row batches” needs a batch size. The current four-column
  100,000-row object workload took about 18.66 ms; reporting per-row or rows/s is less ambiguous.
- Absolute `file://` links were replaced by repository-relative links.

## Method

The comparisons ran on the same Apple Silicon machine and process environment:

- macOS 26.5.2;
- .NET SDK 10.0.400 and runtime 10.0.11;
- BenchmarkDotNet 0.15.8 with its in-process toolchain;
- two or three warmups and six to eight measurement iterations, depending on the lane;
- correctness setup outside the measured operation;
- native DuckDB in-memory databases for SQL-shape comparisons, a local DuckDB file for transaction-boundary
  comparisons, and one pre-generated local Parquet file for the ingestion-modality comparison.

The SQL benchmarks use one invocation per iteration because each invocation mutates its fixture. BenchmarkDotNet
therefore warns that several iteration times are below its preferred 100 ms. The large, non-overlapping effects
are useful implementation evidence; they are not production SLA or capacity measurements.

## Measured evidence

### Native Upsert and logical-key SQL

Lower is better. The distinct-input rows measure only the final `INSERT ... ON CONFLICT` statement against a
staging table whose keys are unique. The cardinality rows use 10,000 unique incoming keys, half updates and half
inserts, against a target without a physical logical-key constraint.

| Workload | Current safe path | Candidate | Winner |
|---|---:|---:|---|
| Distinct input, 10,000 staged rows | Window: 3.160 ms; 880 B | Direct source: 2.266 ms; 1,552 B | Direct source, 28.3% lower latency |
| Distinct input, 50,000 staged rows | Window: 9.811 ms; 4,528 B | Direct source: 6.859 ms; 4,528 B | Direct source, 30.1% lower latency |
| Logical-key guard, 100,000-row target | Correlated guard: 6.897 ms | Atomic semi-join guard: 4.027 ms | Atomic guard, 41.6% lower latency |
| Logical-key guard, 500,000-row target | Correlated guard: 20.247 ms | Atomic semi-join guard: 3.302 ms | Atomic guard, 83.7% lower latency |

The benchmark setup deliberately inserts two existing rows with the same logical key, runs the atomic set-guard
candidate, and verifies that it throws `duplicate logical key` while the two target rows and their original
payload sum remain unchanged.

### Transaction boundary

The raw lane measures ten generated 10,000-row `INSERT ... ON CONFLICT` statements. The provider lane measures
the complete 100,000-row Appender staging and Upsert path with the same requested batch size.

| Workload | Per-batch auto-commit | One explicit transaction | Winner |
|---|---:|---:|---|
| Raw SQL boundary | 50.53 ms; 4.66 KB | 47.30 ms; 9.42 KB | No material winner; confidence intervals overlap |
| Complete provider Upsert | 106.01 ms; 167.12 KB | 96.95 ms; 168.00 KB | No proven winner; confidence intervals overlap |

This run did not reproduce sustained checkpoint spikes. A separate endurance benchmark would need repeated
multi-million-row operations, indexed target growth, WAL/checkpoint telemetry, latency percentiles, and both the
default and raised checkpoint thresholds. Even if that test proves a throughput benefit, atomicity remains a
public behavior choice rather than a transparent optimization.

### Managed allocation candidates

The converter and delegate rows are per value/delegate invocation. The affinity rows are per fallback lookup.
The planner-array rows isolate a 512-reference array and do not represent a provider-level change.

| Workload | Current form | Candidate form | Winner |
|---|---:|---:|---|
| Value-type conversion | Object converter: 4.932 ns; 48 B | Typed expression: 0.279 ns; 0 B | Typed expression |
| Entity delegate dispatch | Object delegate: 0.349 ns; 0 B | Typed delegate: 0.347 ns; 0 B | Tie |
| Affinity-rule fallback | LINQ: 17.589 ns; 48 B | Loop: 6.896 ns; 0 B | Loop, but cold-path only |
| 512-reference plan array | Allocate: 168.38 ns; 4,120 B | Rent/clear/return: 69.08 ns; 0 B | Pool in isolation; not provider-proven |

The end-to-end `BulkInsert` fixture with three value-type converters allocated **144 B per row**, while a
provider-shaped version allocated **0 B per row**. Its short latency run became bimodal as dynamic optimization
settled, so only the stable allocation result is used for the decision. For one million rows and three such
columns, 144 B/row projects to about **137.3 MiB** of avoidable managed allocation. The original claim of exactly
six million heap objects is plausible for two boxes per value-type conversion, but allocation bytes are the
measured evidence.

### Array parameters, ingestion modality, and attachment probe

| Workload | Current / object path | Candidate / columnar path | Winner |
|---|---:|---:|---|
| 500-element read-only-list parameter | Defensive copy: 431.8 us; 42.54 KB | Direct bind: 402.9 us; 40.54 KB | Direct bind for 2.00 KB lower allocation; latency unproven |
| 100,000 four-column rows | Object `BulkInsert`: 186.55 ns/row; 5.36M rows/s | Existing Parquet: 83.16 ns/row; 12.03M rows/s | Parquet for an already-columnar source |
| Attachment lookup | `duckdb_databases()` probe: 77.58 us; 1,256 B | Cached field read: below timer resolution; 0 B | Not decision-grade without invalidation coverage |

The read-only-list setup executes the direct binding and requires an exact count of 500 before BenchmarkDotNet
starts. The implementation allowlists the concrete shapes verified with DuckDB.NET (`T[]`, `List<T>`,
`ReadOnlyCollection<T>`, and `ImmutableArray<T>`). A custom `IReadOnlyList<T>` implementation failed direct
binding and therefore retains the defensive `List<T>` conversion. The Parquet comparison excludes the cost of
producing the Parquet file and excludes object creation,
because it answers the narrower question: “If the source is already Parquet, is a direct mapped load worth an
API?” It does not show that applications should convert object streams to Parquet before inserting them.

## Revised implementation order

| Order | Finding | Required design and validation | Decision |
|---:|---|---|---|
| 1 | U-1 converter expressions | Inline supported converter expressions in [`DuckDBAppenderRowWriter.cs`](../src/DuckDB.EFCoreProvider/Extensions/Internal/DuckDBAppenderRowWriter.cs); retain fallback; cover nullable, enum, custom value-object, field-backed, unsupported-provider-type, BulkInsert, Upsert, native DuckDB, and DuckLake paths | Implement |
| 2 | U-5 logical-key guard | Render one atomic set-based guard in [`DuckDBUpsertSqlRenderer.cs`](../src/DuckDB.EFCoreProvider/Extensions/Internal/DuckDBUpsertSqlRenderer.cs); preserve the exact error and fail-before-mutation contract; test duplicate targets, composite keys, null semantics, native logical merge, and DuckLake | Implement |
| 3 | U-2 distinct-input contract | Add a typed, explicit duplicate-handling option; keep deterministic last-wins as default; cover duplicates inside and across width-aware chunks and both Upsert strategies | Implement opt-in |
| 4 | M-2 read-only lists | Bypass `ToList()` only for driver-supported `IReadOnlyList<T>` shapes in [`DuckDBArrayTypeMapping.cs`](../src/DuckDB.EFCoreProvider/Storage/Internal/DuckDBArrayTypeMapping.cs); add array/list/immutable/read-only/null element execution tests plus a custom-implementation fallback test | Implement |
| 5 | U-6 mapped Parquet input | Define model-to-file column matching, conversion, missing/extra columns, identifiers, schemas, transactions, cancellation, diagnostics, local/remote path policy, and DuckLake capability behavior before adding a public API | Roadmap |
| 6 | M-1 affinity loop | Replace the two LINQ fallbacks in [`DuckDBTypeMappingSource.cs`](../src/DuckDB.EFCoreProvider/Storage/Internal/DuckDBTypeMappingSource.cs) when touching the file; retain mapping tests | Opportunistic |
| — | U-3 typed Upsert plan | No measurable benefit and unnecessary generic-plan complexity | Reject |
| — | U-4 automatic transaction | Performance not proven; silently changes partial-commit semantics; the existing explicit transaction and documented `CheckpointThreshold` are sufficient | Defer |
| — | S-1 array pooling | Requires a safe ownership/lease redesign and an end-to-end SaveChanges allocation win | Defer |
| — | C-1 catalog cache | Requires connection-scoped invalidation for attach/detach, EnsureDeleted, encrypted attachment, aliases, and pooled/native connection reuse; current saving is only ~78 us/open | Defer |
| — | Arrow ingestion | No Arrow benchmark or API-contract assessment was performed | Unproven |

## Reproduction

Build the benchmark project:

```bash
dotnet build test/DuckDB.EFCoreProvider.Benchmarks/DuckDB.EFCoreProvider.Benchmarks.csproj \
  -c Release --no-restore
```

Run the assessment lanes:

```bash
dotnet run --no-build -c Release \
  --project test/DuckDB.EFCoreProvider.Benchmarks -- \
  --filter '*DistinctInputUpsertBenchmarks*' \
           '*LogicalKeyCardinalityBenchmarks*' \
           '*UpsertTransactionBoundaryBenchmarks*' \
           '*ProviderUpsertTransactionBenchmarks*' \
           '*ConvertedBulkInsertBenchmarks*' \
           '*PerformanceReviewManagedCandidateBenchmarks*' \
           '*ReadOnlyListParameterBenchmarks*' \
           '*ParquetIngestionModalityBenchmarks*' \
           '*AttachedCatalogProbeBenchmarks*' \
  --inProcess --warmupCount 3 --iterationCount 8
```

Before merging any production implementation, run focused native and DuckLake behavior tests, the complete
solution build, `scripts/test-suite.sh all`, formatting, package/API validation, and a final diff review as
required by the repository guidance.
