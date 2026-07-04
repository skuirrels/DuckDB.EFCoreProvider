# DuckDB.EFCoreProvider — Capability Map & Limitations

This document is the published capability matrix for the provider. It serves two purposes:

1. **Limitations matrix** — what is supported, and what is not, for adoption decisions.
2. **Skip triage** — the EF Core relational specification suite contributes thousands of inherited tests;
   many are skipped. This map sorts the *reasons* into two buckets so the skip list is a capability map
   rather than an opaque "TBD".

**Targets:** EF Core 10.0.x · .NET 10 · DuckDB.NET 1.5.x. Last reviewed: 2026-07-04.

---

## Legend

- ✅ **Supported** — implemented and covered by tests.
- ⛔ **DuckDB limitation** — cannot be supported because of the DuckDB engine. Some are flagged by DuckDB
  itself as "not yet supported" and *may* lift as DuckDB evolves; they are still outside this provider's
  control.
- 🛠️ **Roadmap** — could be supported by the provider but is not implemented yet (the bulk of the `"TBD"`
  skips). Not a confirmed limitation.

---

## 1. Supported ✅

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
| File sources | `[FromParquet]`/`[FromCsv]`/`[FromJsonFile]` (and fluent `FromParquet`/`FromCsv`/`FromJsonFile`) → `read_parquet`/`read_csv`/`read_json` |
| Bulk insert | `DbContext.BulkInsert(...)` / `BulkInsertAsync(...)` via the DuckDB `Appender` (raw fast path — see §4) |
| Spatial (NetTopologySuite) | `UseNetTopologySuite()`; native DuckDB `GEOMETRY` columns (WKT is only the driver wire format) |
| Raw SQL | EF Core relational raw-SQL APIs |
| Database-first scaffolding | `dotnet ef dbcontext scaffold` (tables, columns, keys, indexes, sequences, FKs) |

---

## 2. DuckDB limitations ⛔ (engine — not planned here)

### Concurrency & transactions
| Limitation | Evidence |
|---|---|
| Single-writer, embedded — no concurrent multi-process/multi-instance writers | `access_mode=READ_ONLY` connection model |
| No savepoints (no nested-transaction partial rollback) | `DuckDBRelationalTransaction.SupportsSavepoints => false` |
| No retrying execution strategy / `EnableRetryOnFailure` | not provided (embedded model) |

### Migrations (`ALTER TABLE` surface)
DuckDB rejects most in-place schema changes. The provider surfaces the DuckDB error rather than faking it.
| Operation | DuckDB error |
|---|---|
| Add / drop foreign key | unsupported (operation ignored / not emitted) |
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
| `COPY` (import/export) | ❌ not exposed | raw SQL only |
| `httpfs` / S3 / remote URLs | ❌ not exposed | extension not loaded |
| `ATTACH` (multi-database / cross-DB queries) | ❌ not exposed | one connection/database per context |

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

### Extensions
| Extension | Provider |
|---|---|
| `spatial` | ✅ auto-loaded (NetTopologySuite); native `GEOMETRY` storage — WKT is only the DuckDB.NET wire format (reads via `ST_AsWKT`, writes via `ST_GeomFromText`) |
| `json` | ✅ built-in |
| full-text search (`fts`) | ❌ not exposed |
| database scanners (Postgres / MySQL / SQLite `ATTACH`) | ❌ not exposed |
| `excel`, `httpfs`, others | ❌ not exposed |

### Write performance
| DuckDB capability | Provider |
|---|---|
| `Appender` (high-throughput bulk load) | ✅ exposed via `DbContext.BulkInsert(...)` (raw fast path; bypasses change tracking / generated values). `SaveChanges` still uses `INSERT … RETURNING`. |

### Functions
Only a subset of DuckDB's large function library is LINQ-translatable (some string / math / date / regex).
DuckDB-specific functions (`list_*`, `map_*`, many `regexp_*` / date helpers) require raw SQL.

---

## 5. Adoption summary

| Workload | Fit |
|---|---|
| Analytical / reporting / dashboards (read-heavy) | ✅ Good fit |
| Embedded / edge / desktop local store | ✅ Good fit |
| ETL / Parquet querying | ✅ Strong fit |
| Using DuckDB as a full analytical engine (PIVOT/ASOF/SUMMARIZE, CSV/JSON/remote ingestion, native nested types, bulk-load) | ➖ Largely via raw SQL only — see §4 |
| Multi-instance / high-concurrency OLTP system-of-record | ⛔ Not suitable (engine) |
