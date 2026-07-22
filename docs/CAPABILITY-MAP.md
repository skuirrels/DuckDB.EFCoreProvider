# DuckDB.EFCoreProvider — Capability Map & Limitations

This document is the published capability matrix for the provider. It serves two purposes:

1. **Limitations matrix** — what is supported, and what is not, for adoption decisions.
2. **Skip triage** — the EF Core relational specification suite contributes thousands of inherited tests;
   many are skipped. This map sorts the *reasons* into two buckets so the skip list is a capability map
   rather than an opaque "TBD".

**Targets:** EF Core 10.0.x · .NET 10 · DuckDB.NET 1.5.x · DuckLake 1.0. Last reviewed: 2026-07-22.

---

## Legend

- ✅ **Supported** — implemented and covered by tests.
- ⛔ **DuckDB limitation** — cannot be supported because of the DuckDB engine. Some are flagged by DuckDB
  itself as "not yet supported" and *may* lift as DuckDB evolves; they are still outside this provider's
  control.
- 🛠️ **Roadmap** — could be supported by the provider but is not implemented yet (the bulk of the `"TBD"`
  skips). Not a confirmed limitation.

---

## 1. Native DuckDB support ✅

| Area | Notes |
|---|---|
| CRUD via `SaveChanges` | insert / update / delete |
| Generated keys & store-generated values | DuckDB `RETURNING`; `UseAutoIncrement()` backed by sequences |
| Optimistic concurrency | concurrency tokens |
| Transactions | commit / rollback |
| Migrations — create | tables, columns, indexes, sequences, comments, history table |
| LINQ query translation | joins, grouping, ordering, paging, aggregates, string/math/temporal, row values, arrays, JSON traversal, set operations |
| JSON | `string`, `JsonDocument`, `JsonElement`, owned JSON via `ToJson()` |
| Arrays / `List<T>` | CLR arrays and lists, typed `INTEGER[]`-style store types |
| Type-mapping contract | EF model mappings and raw DuckDB.NET reader mappings are documented separately in [TYPE-MAPPINGS.md](TYPE-MAPPINGS.md) |
| File sources | `[FromParquet]`/`[FromCsv]`/`[FromJsonFile]` (and fluent `FromParquet`/`FromCsv`/`FromJsonFile`) → `read_parquet`/`read_csv`/`read_json` |
| Tiered storage (hot + cold) | `ToTieredStore(...)` + root-only ordered `.PartitionBy(p => p.By(..., "alias").ByMonth(..., "alias"))` + `.WithTieredView()` or `.WithReadModel<T>()` + `ArchiveTierAsync(...)`: provider-managed union views with optional EF projection types, `ToTieredView(...)` mapping with equivalent read-only-context pruning, bounded first publication via `BootstrapArchiveTierAsync(...)`, immutable active-cold trimming via `PlanArchiveRetentionAsync(...)` / `PublishArchiveRetentionAsync(...)`, fail-closed generation cleanup planning, and external-checkpoint recovery via `CaptureArchiveRecoveryCheckpointAsync(...)` / `PlanArchiveRecoveryAsync(...)` / `ApplyArchiveRecoveryAsync(...)`. Supports application-defined Hive names/order/transforms, inherited child layout, and root-scoped bindings when one child table participates in multiple independent archives. See [docs/TIERED-STORAGE.md](TIERED-STORAGE.md) and the [tiered compatibility matrix](TIERED-STORAGE-COMPATIBILITY.md) |
| Bulk insert | `DbContext.BulkInsert(...)` / `BulkInsertAsync(...)` via the DuckDB `Appender` (raw fast path — see §4) |
| Spatial (NetTopologySuite) | `UseNetTopologySuite()`; native DuckDB `GEOMETRY` columns (WKT is only the driver wire format) |
| Raw SQL | EF Core relational raw-SQL APIs |
| Resource settings | `MemoryLimit(...)` and `Threads(...)` apply DuckDB's global instance settings when a connection opens |
| Database-first scaffolding | `dotnet ef dbcontext scaffold` (tables, columns, keys, indexes, sequences, FKs) |

### DuckLake backend profile

`UseDuckLake(...)` is included in the main package and configures DuckLake as an attached backend catalog.
The profile is covered by real-extension functional tests, not inferred from native DuckDB behaviour.

| Area | DuckLake profile |
|---|---|
| Connection lifecycle | ✅ extension load, secret callback, safe `ATTACH`, and `USE` before EF uses provider-owned or caller-owned connections, including already-open connections |
| Queries / raw SQL | ✅ normal EF LINQ and relational raw SQL against the selected catalog |
| Tracked writes | ✅ insert/update/delete without `RETURNING`; affected-row optimistic-concurrency checks |
| Transactions | ✅ commit/rollback through DuckDB/DuckLake; explicit transactions can set snapshot author/message/extra information through `SetCommitMessageAsync(...)` |
| Initial schema | ✅ `EnsureCreated`; unsupported physical constraints and indexes are omitted |
| Bulk insert | ✅ DuckDB appender after provider-controlled connection initialization |
| Upsert | ✅ staged appender batch plus `MERGE INTO` |
| Read-only / named secret | ✅ dedicated profile options; credentials remain in the connection initializer |
| Dynamic unknown-shape SQL | ✅ `SqlQueryDynamicRawAsync` / `SqlQueryDynamicAsync` stream raw DuckDB.NET values with runtime column metadata; known DML that needs an affected-row count uses `ExecuteSqlRawAsync` because DuckDB.NET readers currently report `RecordsAffected == -1` |
| Maintenance | ✅ typed snapshot, expiry, cleanup, orphan deletion, flush, merge, and rewrite operations; destructive lifecycle calls default to dry-run where DuckLake supports it |
| Historical LINQ | ✅ `DbSet.AsOfSnapshot(long)` / `DbSet.AsOfTimestamp(DateTimeOffset)` for an explicit table root, plus catalog-wide read-only context profiles for coherent joins |
| Additional catalogs | ✅ local catalogs through `AlsoAttach(...)` and secret-backed catalogs through `AlsoAttachNamedSecret(...)`; catalog-qualified dynamic/raw SQL is supported, while non-primary EF entity mapping remains roadmap |
| Physical PK/FK/unique/check/index | ⛔ not supported by DuckLake; EF metadata remains logical only |
| Sequences/store-generated values/SQL defaults | ⛔ store generation rejected; client-assigned or client-generated values required (literal defaults may exist in DDL but cannot be read back) |
| EF migrations | ⛔ explicitly rejected; no safe EF history/locking contract without enforced uniqueness and `RETURNING` |
| `EnsureDeleted` | ⛔ explicitly rejected to avoid deleting shared/remote backing stores implicitly |
| Database-first scaffolding | ✅ local metadata through the `ducklake:/path/metadata.ducklake` design-time source; catalog-scoped, read-only, and keyless-first |
| SaveChanges batching | ⛔ profile rejects current batching; use `BulkInsert` or `Upsert` |
| Provider tiered storage | ⛔ incompatible; DuckLake owns Parquet layout and file lifecycle |

See [DUCKLAKE.md](DUCKLAKE.md) for configuration, security, model rules, and operational guidance.

---

## 2. DuckDB limitations ⛔ (engine — not planned here)

### Concurrency & transactions
| Limitation | Evidence |
|---|---|
| Native DuckDB file is single-writer and embedded | `access_mode=READ_ONLY` permits multiple native-file readers; DuckLake concurrency instead depends on its metadata catalog, as documented in [DUCKLAKE.md](DUCKLAKE.md#concurrency-and-read-scaling) |
| No savepoints (no nested-transaction partial rollback) | `DuckDBRelationalTransaction.SupportsSavepoints => false` |
| No retrying execution strategy / `EnableRetryOnFailure` | not provided (embedded model) |

### Migrations (`ALTER TABLE` surface)
DuckDB rejects most in-place constraint changes. The provider fails clearly by default; applications can opt
into a target-model table rebuild with `EnableMigrationTableRebuilds()`.
| Operation | Provider behaviour |
|---|---|
| Foreign key in `CREATE TABLE` | ✅ emitted and enforced; unsupported cascade actions become `NO ACTION` with a migration warning |
| Add / drop foreign key | clear `NotSupportedException`; ✅ opt-in table rebuild |
| Update / delete a referenced row | DuckDB rejects the operation while dependent rows exist, including non-key updates |
| Add / drop primary, unique, or check constraint | clear engine error by default; ✅ opt-in table rebuild |
| Add column with constraint / default / required | `Adding columns with constraints not yet supported` |
| Add / alter computed (generated) column | `Adding generated columns after table creation is not supported yet` |
| Add / drop / alter check constraint | `No support for that ALTER TABLE option yet!` |
| Add / drop unique constraint | `No support for that ALTER TABLE option yet!` |
| Drop primary key / drop PK column | `No support for that ALTER TABLE option yet!` |
| Rename table (incl. with PK / JSON column) | `No support for that ALTER TABLE option yet!` |
| Move table / sequence to another schema | `Not implemented Error: T_AlterObjectSchemaStmt` |
| Rename sequence | `Schema element not supported yet!` |

### Indexes
Basic indexes work (single/composite column, unique, expression). The following are DuckDB engine limitations:
| Operation | DuckDB behaviour (probed) |
|---|---|
| Descending index / per-column sort direction | `CREATE INDEX ... (col DESC)` is accepted but the direction is discarded — a persisted index reads back as `CREATE INDEX ... (col)`, so direction cannot be round-tripped |
| Partial / filtered index (`WHERE`) | `Not implemented Error: Creating partial indexes is not supported currently` |
| Rename index | `Not implemented Error: Schema element not supported yet!` |

### Types & schema
| Limitation | Evidence |
|---|---|
| `STORED` generated columns (only `VIRTUAL` exist) | DuckDB: `Can not create a STORED generated column!` (probed) |
| Computed columns are not reverse-engineered | DuckDB does not populate `information_schema.columns.is_generated` / `generation_expression`; the expression leaks into `column_default` with no distinguishing flag (probed) |
| Multidimensional arrays | `NotSupportedException("Multidimensional arrays are not supported")` |

### Query translation
| Limitation | Evidence |
|---|---|
| Correlated columns in `LIMIT` / `OFFSET` | `DuckDBQuerySqlGenerator` |
| `WITH ORDINALITY` | worked around via `generate_subscripts` |
| `array_contains` searching for `NULL` elements | `DuckDBQueryableMethodTranslatingExpressionVisitor` |
| `DateTimeOffset` with non-zero offset | skipped spec tests ("DateTimeOffset with non-zero offset") |

---

## 3. Roadmap 🛠️ (not yet implemented — the "TBD" backlog)

The large majority of skipped spec tests carry the generic `DuckDBSkipReasons.Tbd` ("TBD") reason. These
are **not** confirmed engine limitations — they are areas that have not yet been implemented or
investigated. Known clusters:

- **Migrations:** JSON-column migrations and owned↔JSON conversions, complex-type-to-JSON mapping,
  sequence `RESTART`/`INCREMENT BY` alters, multiline comments, multi-op table rename, raw `SqlOperation`.
  (Descending / filtered / rename indexes are confirmed engine limitations — see §2 Indexes.)
- **Query/spec backlog:** assorted ad-hoc query scenarios, some grouping / set-operation edge cases,
  precompiled-query pregeneration, and other inherited relational-spec coverage.
- **Next-major API ergonomics:** move `UseAutoIncrement()` into the conventional
  `Microsoft.EntityFrameworkCore` namespace. It remains in `DuckDB.EFCoreProvider.Extensions` for the current
  major to avoid creating ambiguous extension resolution or a misleading binary-compatibility shim in a minor
  release.

> **Triage status.** The "can't" bucket above (§2) is evidence-backed and stable. Reclassifying every
> individual `"TBD"` skip into roadmap-vs-limitation is an ongoing effort; until a skip is moved to a
> specific reason it should be read as **roadmap/uninvestigated**, not as a confirmed limitation.

### Skip-reason taxonomy

New skips should use a specific reason from `DuckDBSkipReasons` so this map stays accurate:

| Constant | Meaning |
|---|---|
| `NotSupportedByDuckDB` | confirmed DuckDB engine limitation (§2) |
| `NotYetImplemented` | provider roadmap (§3) |
| `Investigating` | failure under investigation; not yet classified |
| `Tbd` | legacy/generic backlog marker — prefer one of the above for new skips |

---

## 4. Provider vs. DuckDB feature gaps 🔌

These are things **DuckDB can do but the provider does not expose through the EF Core model / LINQ layer**.
They are distinct from §2 (which is what DuckDB itself cannot do). Most remain usable via **raw SQL**
(`FromSql`, `SqlQuery`, `ExecuteSql`) — you lose LINQ composition and type safety, not the capability.

### Data ingestion / external sources
| DuckDB feature | Provider | Notes |
|---|---|---|
| `read_parquet` | ✅ exposed | `[FromParquet]` / `FromParquet(...)` |
| `read_csv` / `read_json` | ✅ exposed | `[FromCsv]` / `[FromJsonFile]` and fluent equivalents |
| `COPY` export to Parquet | ✅ exposed | `ExportToParquet` / `ExportToParquetAsync` over translated queries |
| Other `COPY` import/export formats | ❌ not exposed | raw SQL only |
| `httpfs` / S3 / GCS / remote URLs | ✅ configurable | `LoadExtension("httpfs")` plus `ConfigureConnection(...)` for secrets |
| DuckLake `ATTACH` (additional catalogs) | ✅ partially exposed | `AlsoAttach(...)` adds local catalogs and `AlsoAttachNamedSecret(...)` adds catalogs configured by caller-created `TYPE ducklake` secrets; catalog-qualified dynamic/raw SQL works, non-primary EF entity mapping is roadmap |
| Other DuckDB `ATTACH` targets | ❌ not exposed | raw SQL only |

### Analytical SQL constructs
| DuckDB feature | Provider |
|---|---|
| `PIVOT` / `UNPIVOT` | ❌ raw SQL only |
| `QUALIFY` | ❌ raw SQL only |
| `ASOF` joins | ❌ raw SQL only |
| `SAMPLE` | ❌ raw SQL only |
| `GROUPING SETS` / `ROLLUP` / `CUBE` | ❌ raw SQL only |
| `SUMMARIZE` | ❌ raw SQL only |
| Window functions | ➖ only EF Core's generic support |

### Types
| DuckDB type | Provider |
|---|---|
| `STRUCT` | ➖ persisted as JSON, not a native struct column |
| `MAP` | ❌ no mapping |
| `UNION` | ❌ no mapping |
| `LIST` / `ARRAY` (1-D) | ✅ arrays / `List<T>` |
| Multidimensional arrays | ⛔ unsupported (`NotSupportedException`) — see §2 |
| `HUGEINT` / Int128 | ❌ no mapping |
| native `ENUM` | ❌ CLR enums map to the underlying numeric/string, not a DuckDB `ENUM` |
| `BIT` | ❌ no mapping |
| `INTERVAL` | ➖ partial (via `TimeSpan` converters), no dedicated mapping |

Raw ADO.NET and dynamic-result reads use DuckDB.NET's broader value surface, including `BigInteger` for
`HUGEINT` and recursively nested lists/dictionaries for supported composite types. That raw-reader support does
not make those types persistable EF entity properties. See [TYPE-MAPPINGS.md](TYPE-MAPPINGS.md).

### Extensions
| Extension | Provider |
|---|---|
| `spatial` | ✅ auto-loaded (NetTopologySuite); native `GEOMETRY` storage — WKT is only the DuckDB.NET wire format (reads via `ST_AsWKT`, writes via `ST_GeomFromText`) |
| `json` | ✅ built-in |
| full-text search (`fts`) | ❌ not exposed |
| database scanners (Postgres / MySQL / SQLite `ATTACH`) | ❌ not exposed |
| `httpfs`, `azure`, `excel`, others | ✅ generic loading via `LoadExtension(...)`; feature-specific LINQ APIs vary |

### Write performance
| DuckDB capability | Provider |
|---|---|
| `Appender` (high-throughput bulk load) | ✅ exposed via `DbContext.BulkInsert(...)` (raw fast path; bypasses change tracking / generated values). Native DuckDB `SaveChanges` uses `RETURNING`; DuckLake uses non-returning commands plus affected-row checks. |

### Functions
Only a subset of DuckDB's large function library is LINQ-translatable. In addition to common string / math /
date / regex operations, the provider exposes `SplitPart`, `StandardDeviationSample`, `ArgMax`, and `ArgMin`
through `EF.Functions`. Other DuckDB-specific functions (`list_*`, `map_*`, many helpers) require raw SQL.

---

## 5. Adoption summary

| Workload | Fit |
|---|---|
| Analytical / reporting / dashboards (read-heavy) | ✅ Good fit |
| Embedded / edge / desktop local store | ✅ Good fit |
| ETL / Parquet querying | ✅ Strong fit |
| Shared lakehouse catalog / object-storage analytics through DuckLake | ✅ Good fit when the application owns logical uniqueness and schema deployment |
| Using DuckDB as a full analytical engine (PIVOT/ASOF/SUMMARIZE, CSV/JSON/remote ingestion, native nested types, bulk-load) | ➖ Largely via raw SQL only — see §4 |
| Multi-instance / high-concurrency OLTP system-of-record | ⛔ Not suitable (engine) |
