# Tiered-storage compatibility and release acceptance

This is the compatibility contract for the provider-managed hot DuckDB/cold Parquet tier. It describes generic
technical behaviour only. Archive eligibility, retention, authorisation, legal holds, scheduling, distributed writer
coordination, deletion/restoration decisions, credentials, and production object-store policy remain the host
application's responsibility.

## Registration modes

| Mode | Query surface | Status |
|---|---|---|
| `ToTieredStore<TEntity>(...).WithTieredView(...)` | Creates and refreshes the physical hot/cold view without registering a duplicate CLR reader in the tier owner | Supported |
| `Entity<TEntity>().ToTieredView(...)` | Maps the application's existing CLR type as a keyless reader in a separate read-only context and enables contract-checked partition pruning | Supported |
| `WithReadModel<TReadModel>()` | Maps a backward-compatible keyless analytical read model in the tier owner | Supported |
| Root, descendant and nested descendant views | Convention or explicit unqualified view names | Supported |
| One descendant shared by multiple roots | One entity-wide view with deterministic, root-scoped archive bindings | Supported |
| Schema-qualified hot tables | Honoured by archive, delete and view generation | Supported |
| Schema-qualified tiered view names | Not supported; use an unqualified explicit name |

`WithTieredView(...)` alone is physical view registration; query it by mapping a separate context with
`ToTieredView(...)`, or also register a `WithReadModel<TReadModel>()` projection.

## Partition contract

| Capability | Status |
|---|---|
| Exact scalar partition (`By`) | Supported for mapped scalar store types; tested with integer, string, Boolean, decimal, `DateTime`, `DateOnly`, and `Guid` |
| Calendar partitions (`ByYear`, `ByMonth`, `ByDay`) | Supported for `DateTime`, `DateTime?`, `DateOnly`, and `DateOnly?` |
| Owner plus month/day hierarchy | Supported; order is application-defined |
| Explicit physical aliases | Supported, including aliases which avoid root/descendant column-name collisions |
| Nullable exact partition values | Supported; `IS NULL` queries remain correct and derive the aliased partition predicate |
| Nullable lifecycle | `NULL` remains hot and is never selected for archival; a later non-null value becomes eligible normally |
| Descendant partitions | Inherited from the selected root binding; descendants do not define independent partition plans |
| Contract drift | Rejected before mixed-layout reads/writes; `ToTieredView(...)` pruning also references a physical contract marker |

Range pruning is derived only from conjunctive comparisons. Predicates beneath `OR` are intentionally not inferred,
because adding a bucket predicate there could change query semantics. Keyset predicates may contain `OR`; the owner
and lifecycle range predicates outside that cursor expression still prune normally.

## LINQ query composition

The following shapes are covered for both `ToTieredView(...)` and `WithReadModel<TReadModel>()` where applicable:

- owner equality; inclusive lifecycle lower and exclusive upper bounds;
- ascending and descending `OrderBy`/`ThenBy`, `Take`, `Skip`/`Take`, and stable two-column keyset continuation;
- first, middle, final and empty pages, including equal lifecycle timestamps;
- entity, anonymous and member-initialiser scalar projections before or after ordering;
- `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Count`, `LongCount`, `Any`, `Min`, `Max`, `Sum`,
  `Average`, `Distinct`, and bounded-key `Contains`;
- grouping by owner or lifecycle bucket, aggregate-over-pruned-group, subqueries, joins, left joins, and composition
  after `Select` or `Take` where EF Core translates the shape;
- explicit root/descendant, descendant/root, nested-descendant and composite-key joins;
- synchronous and asynchronous execution and pre-cancelled cancellation tokens.

For pruned ordered/limited queries, the suite checks `ToQueryString()`, returned rows, deterministic order, owner
isolation, the exclusive upper boundary, absence of duplicate derived predicates, and DuckDB `EXPLAIN` file counts.
`ORDER BY`, `OFFSET` and `LIMIT` remain server-side; no client evaluation or unbounded provider materialisation is
introduced.

Tiered readers are keyless. They are read-only, are not tracked, and do not support navigation `Include`. Map every
scalar key/foreign-key column needed for an explicit LINQ join. Ordinary hot entities retain their normal keys,
tracking, navigations and `Include` behaviour. EF Core shapes which are not generally translatable, such as filtering
after some positional-constructor projections, still fail with EF Core's deterministic translation exception; use an
anonymous/member-initialiser projection or move the filter before the projection.

Owned/table-split values are available only when they are exposed as mapped scalar columns in the generated physical
view and corresponding keyless reader. Tiered relationships themselves are not reconstructed as tracked keyless
navigations.

## Lifecycle and context robustness

The generic tiered suites cover hot-only creation, first archive, no-op archive, restart, new hot and late rows,
stable-match-key correction suppression, caller-supplied tombstones, reconciliation, restoration, compaction,
contract rewrite, failure before and during publication, retry/restart recovery, schema evolution with nullable
columns, generation inventory, and partition-contract drift. Archive, reconciliation, restore, compaction and
contract-rewrite operations refresh the same physical views.

The query-composition suite also covers reader-before-owner creation order, simultaneous owner and read context,
multiple independent reader contexts, concurrent asynchronous reads, and model-cache keys containing dynamic archive
locations. EF model configuration is immutable after construction: when a context type varies database/archive paths
at runtime, its application-provided `IModelCacheKeyFactory` must include those values, as for any dynamic EF model.
There is no provider correctness dependency on which context type is created first or on mutable process-global
binding state.

DuckDB remains a single-writer embedded database. Cross-process/application-node writer leases are outside the
provider; concurrent read-only contexts are supported.

## Storage backends and evidence

| Backend | Provider path | Release acceptance |
|---|---|---|
| Local filesystem + local DuckDB + Parquet | Native filesystem paths | Always-on functional coverage, including spaces, restart/reopen, hot/cold union, lifecycle transitions, and `EXPLAIN` pruning |
| AWS S3 | `httpfs`, `TYPE s3`, `s3://` | S3-compatible MinIO matrix is always-on in CI; a real-AWS disposable-prefix failure matrix is opt-in |
| Google Cloud Storage | `httpfs`, `TYPE gcs`, `gcs://` | GCS-scheme/HMAC interoperability is exercised against MinIO; a real-GCS disposable-prefix failure matrix is opt-in |
| Azure Blob Storage | `azure` extension, `TYPE azure`, `azure://` | Runnable Azurite sample; a real-Azure disposable-container-prefix failure matrix is opt-in |
| Other S3-compatible stores/R2 | DuckDB `httpfs` configuration | Technically compatible where DuckDB supports the endpoint, but not a separately claimed release-acceptance backend |

Real-cloud tests never embed credentials, account names, buckets, containers, or permanent prefixes. Configure them
with these environment variables:

- AWS: `DUCKDB_AWS_S3_TEST_BUCKET`, optional `_PREFIX`, `_REGION`, paired `_KEY`/`_SECRET`, and `_SESSION_TOKEN`;
- GCS: `DUCKDB_GCS_TEST_BUCKET`, `DUCKDB_GCS_TEST_KEY`, `DUCKDB_GCS_TEST_SECRET`, optional `_PREFIX`;
- Azure: `DUCKDB_AZURE_TEST_CONNECTION_STRING`, `DUCKDB_AZURE_TEST_CONTAINER`, optional `_PREFIX`.

The caller must provide a disposable existing bucket/container and arrange lifecycle cleanup. Remote archive purge is
deliberately unsupported: the provider proves read/write/list/catalogue primitives but does not infer business
retention or delete remote objects.

## Stable-release gate

For `1.10.1`, the `1.10.1-rc.1` package passed the complete tiered conformance suite on 2026-07-17 with no
additional provider defect, authorising stable publication. Future stable packages follow the same gate: first
publish a prerelease, run the compatibility suite against that exact package, and publish stable only after
acceptance succeeds. A skipped credential-gated backend is reported as unverified for that release environment;
it is not recorded as passing evidence.
