# DuckDB 2.0 readiness analysis

Date: 15 October 2026

Scope: what DuckDB 2.0 "Cyanoptera" changes for DuckDB.EFCoreProvider, measured against the
current baseline (EF Core 10.0.x, Skuirrels DuckDB.NET 1.5.6, native DuckDB 1.5.6, DuckLake 1.0).

## Verdict

- The provider's own SQL generation is largely 2.0-compatible. With the 2.0 native library swapped
  under the unchanged 1.5.6 driver, every query, update, bulk, upsert, tiered-storage and transaction
  test that does not need an out-of-tree extension passed.
- **The blocker is the driver, not the provider.** DuckDB.NET 1.5.6 (upstream and the Skuirrels fork)
  throws `ArgumentException: Unrecognised type SqlNull (36)` for any untyped `NULL` result column under
  2.0, and cannot read `TIMESTAMPTZ_NS` (42) or `VARIANT` (41). No 2.0 driver package exists yet.
- The real provider work is concentrated in migrations and scaffolding: 2.0 now accepts several
  `ALTER TABLE` forms the provider currently reports as engine limitations, and it exposes generated
  column metadata the scaffolder deliberately ignores today.
- Operationally, the default storage format moves to v2.0.0: a file created **or modified** by 2.0 can no
  longer be opened by any 1.x build. The provider has no option to pin a storage version.
- DuckLake, spatial, encryption (httpfs/OpenSSL) and Quack paths are **not yet verified** against 2.0
  (see [Evidence](#evidence)).

## Release status

| Item | State on 15 October 2026 |
|---|---|
| Release branch | `v2.0-cyanoptera`, cut 2 September 2026 (feature freeze); head `9a97642349`, 15 October |
| Tags / RC | No `v2.0.0` or `v2.0.0-rc*` tag. Staged builds are alphas only, latest `v2.0.0-alpha46057` (`b15358c371`, 9 October) |
| Planned dates | 2.0.0 on 21 October 2026, 2.0.1 on 18 November 2026 ([release calendar](https://duckdb.org/release_calendar.html)) |
| 1.x support | 1.4 LTS ends 17 November 2026; 1.5 ends 1 March 2027 |
| LTS status of 2.0 | Not stated. The "every other version" LTS policy implies it, but this is unconfirmed |
| DuckDB.NET | Upstream and Skuirrels fork both at 1.5.6; no 2.0, RC or preview package on NuGet. Upstream `nightly-builds` tracks `v2.0-cyanoptera`; the fork does not |
| DuckLake | No tag paired with 2.0. DuckDB 2.0 pins ducklake commit `80e6155e`, which creates spec `1.1-dev1` catalogs |
| `main` branch | Already 2.1 development; `v2.0-cyanoptera` is merged into it regularly |

Primary sources: [2.0 highlights](https://duckdb.org/2026/08/17/duckdb-20-highlights),
[try the 2.0 alpha](https://duckdb.org/2026/09/02/try-duckdb-20-alpha),
[preview docs](https://duckdb.org/docs/preview/), [migration guide](https://duckdb.org/docs/preview/operations_manual/migration).

## Evidence

All results below come from three sources:

1. **Source diff** of `v1.5.6` against `origin/v2.0-cyanoptera` in a local `duckdb/duckdb` clone: C API
   headers, `settings.hpp`, `storage_info.cpp`, extension pins and the commit log.
2. **SQL probes** run on the DuckDB 1.5.6 CLI and on a Release build of `v2.0-cyanoptera@9a97642349`
   (built locally with `json;icu;parquet` statically linked). The probes covered 70 provider-relevant
   statements.
3. **Full functional suite** (net10.0, 25,892 tests) run twice from identical build output: once with
   the packaged 1.5.6 `libduckdb.dylib`, and once with the 2.0 build's `libduckdb.dylib` swapped in. A
   DllNotFound check confirmed that the swapped library was the one loaded.

| Run | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Native 1.5.6 | 21,753 | 1 | 4,138 |
| Native 2.0 (`9a97642349`) | 21,509 | 245 | 4,138 |

The single failure on 1.5.6 (`StructDesignTimeWorkflowTests`) is a harness artefact of running from a
copied output directory, and it fails identically on 2.0.

The 2.0 failures break down as follows (165 distinct tests, both async variants counted once):

| Cause | Tests | Classification |
|---|---:|---|
| `spatial` extension not downloadable for a local dev build (HTTP 404) | 80 | Environment; **not verified** |
| `ducklake` extension not downloadable (HTTP 404) | 38 | Environment; **not verified** |
| Encrypted writes need httpfs/OpenSSL, which could not be autoinstalled | 21 | Environment; **not verified** |
| Migration tests asserting 1.5 engine errors that 2.0 no longer raises (14) or now words differently (8) | 22 | **Provider work** (see M1 to M4) |
| `First()` without `OrderBy` returns a different row (`LIMIT 1` without `ORDER BY`) | 3 | Test assumption; result is unspecified |
| Harness artefact (same as 1.5.6) | 1 | Environment |

The extension-dependent results need re-running on the official alpha or final binaries, which publish
matching `spatial`, `ducklake` and `httpfs` builds.

## Impact matrix

Severity: **Blocker** prevents shipping on 2.0. **High** means wrong behaviour or a failed user
workload. **Medium** is a capability gap or a stale limitation. **Low** is hygiene.

### Driver (DuckDB.NET), owned by the Skuirrels fork

| # | Change in 2.0 | Effect measured | Severity |
|---|---|---|---|
| D1 | A bare `NULL` keeps type `SQLNULL` (36) instead of becoming `INTEGER` ([#23017](https://github.com/duckdb/duckdb/pull/23017)) | `SELECT NULL AS n` throws `ArgumentException: Unrecognised type SqlNull (36)` in the 1.5.6 reader. EF-generated SQL passed the suite, but raw SQL, `SqlQueryDynamic*`, Quack replay and user views are exposed | **Blocker** |
| D2 | New `DUCKDB_TYPE_TIMESTAMP_TZ_NS = 42`; Parquet nanosecond UTC columns now read as this type | `Unrecognised type 42`. Affects `FromParquet` over such files and any `TIMESTAMPTZ_NS` column | **Blocker** for Parquet users |
| D3 | `VARIANT` (41) is a core type (already in 1.5) | `Unrecognised type 41` on both versions; not a regression, but still unsupported | Medium |
| D4 | Destroying an unfinished pending or streaming result aborts the statement; autocommit writes roll back; inside a transaction a writing statement invalidates it; other calls get `DUCKDB_ERROR_RESOURCE_IN_USE` (44) | The provider uses materialised results (`UseStreamingMode` is never set), so it is unaffected today. Applies to any future streaming path and to user code on a provider connection | Medium |
| D5 | `duckdb_appender_flush` requires a connection with no open result | `BulkInsert`/`Upsert` passed. A user `DataReader` left open on the same connection will now fail the flush | Medium |
| D6 | Late streaming errors are raised from `ExecuteReader` (upstream commit `3162cae7`) | Streaming only; see D4 | Low |
| D7 | New error types `DATA_CORRUPTION` (43), `RESOURCE_IN_USE` (44) | Should map to distinct `DuckDBErrorType` values | Low |
| D8 | New `duckdb_v2.h` C API (`duckdb_v2_*`, stable ABI, error codes); `duckdb.h` v1 API retained with no removals | No forced migration for 2.0. A strategic option for a later driver major | Low |
| D9 | Nightly artefacts renamed to `duckdb-shared-libs-<platform>.tar.gz`; old zips 404 | The fork's `Bindings.csproj` cannot build against 2.0 until upstream commit `a981b799` is ported | **Blocker** for building a fork preview |

### Provider: SQL semantics

| # | Change in 2.0 | Provider exposure | Severity |
|---|---|---|---|
| S1 | `//`, `%` and DECIMAL/INTERVAL division by zero now raise `Invalid Input Error: Division by zero` ([#25004](https://github.com/duckdb/duckdb/pull/25004)). `/` on doubles still returns `inf` | Matches .NET integer semantics (`DivideByZeroException`), so it is an improvement. Queries that previously yielded `NULL` now fail; provider-internal `%` uses literal non-zero divisors. Release notes must call this out | Medium |
| S2 | Any error, including parser and binder errors, invalidates the open transaction (`default_transaction_invalidation_policy = ALL_ERRORS_INVALIDATE_TRANSACTION`) ([#22674](https://github.com/duckdb/duckdb/pull/22674)) | `DuckDBDatabaseCreator` and the migrations lock use catch-and-continue probes. These run outside user transactions today, but must not be moved inside one | Medium |
| S3 | `x -> x + 1` lambdas are a binder error; `lambda x: x + 1` is required; the flag goes in 2.1 ([#22682](https://github.com/duckdb/duckdb/pull/22682)) | The provider emits no lambdas (JSON `->>` is unaffected and passed). User raw SQL and `HasComputedColumnSql` text are exposed | Low (provider), High (users) |
| S4 | `~`/`!~` now match partially (PostgreSQL semantics) ([#23432](https://github.com/duckdb/duckdb/pull/23432)) | `Regex.IsMatch` translates to `regexp_matches`, so it is unaffected | Low |
| S5 | `UPDATE ... FROM` updates each target row once and reports distinct rows; `MERGE` rejects double-hit targets ([#24058](https://github.com/duckdb/duckdb/pull/24058)) | Batched `UPDATE ... FROM (VALUES ...)` uses unique keys, so counts are unchanged. The Upsert cardinality guard already rejects duplicates | Low |
| S6 | `INSERT ... ON CONFLICT` with duplicate input keys applies one arbitrary update per key | The provider already resolves duplicates to the last occurrence before staging (#70) | Low |
| S7 | The PEG parser replaces the PostgreSQL-derived parser; `legacy_parser` is an opt-in extension ([#22194](https://github.com/duckdb/duckdb/pull/22194)) | The full query/update/DDL suite parsed cleanly. Some error texts and positions changed (see M4) | Low |
| S8 | Unordered `LIMIT 1` correlated subqueries return a different row than 1.5.6 | Three Northwind tests assert insertion order for `First()` without `OrderBy`; the SQL has no `ORDER BY`, so either result is valid | Low |
| S9 | Reserved-identifier set grew: 19 new keywords and 228 new function names (`tuple`, `map`, `struct`, `columns`, `generated` and others) | `DuckDBIdentifierMetadata` was generated from 1.5.5. New `column_name` keywords used as unquoted identifiers could fail to parse | Low |
| S10 | `PRAGMA name=value` warns; `preserve_identifier_case` is now an enum; settings renamed under `debug_` | The provider issues only `SET threads`, `memory_limit`, `checkpoint_threshold`, `file_search_path` and `temp_file_encryption`, all still valid | Low |

### Provider: migrations and scaffolding

Probed on both engines:

| Operation | 1.5.6 | 2.0 | Provider today |
|---|---|---|---|
| `ADD COLUMN ... NOT NULL DEFAULT ...` | `Parser Error: Adding columns with constraints not yet supported` | **Works** | Emits SQL; tests assert the 1.5 error |
| `ADD COLUMN ... UNIQUE` | same error | **Works** | as above |
| `ADD CONSTRAINT ... UNIQUE (...)` | `No support for that ALTER TABLE option yet!` | **Works** | Emits SQL; the scaffolder does not read unique constraints, so the base test then fails |
| `DROP CONSTRAINT` | not supported | not supported | Rebuild opt-in |
| `ADD CONSTRAINT ... CHECK` | not supported | not supported; new text `No support for adding CHECK constraints with ALTER TABLE` | Rebuild opt-in |
| `ADD CONSTRAINT ... FOREIGN KEY` | not supported | not supported; new text `No support for adding FOREIGN_KEY constraints with ALTER TABLE` | Rebuild opt-in |
| `ALTER TABLE ... SET SCHEMA` | `Not implemented Error: T_AlterObjectSchemaStmt` | `Parser Error: syntax error at or near "SCHEMA"` | Still unsupported |
| `ALTER INDEX ... RENAME` | `Schema element not supported yet!` | `Parser Error` (no grammar rule) | Still unsupported |
| `ALTER SEQUENCE ... RENAME` | `Schema element not supported yet!` | `Renaming sequences is not yet supported` | Still unsupported |
| `ALTER SEQUENCE ... INCREMENT BY` / `RESTART` | not supported | not supported; `RESTART` is a parser error. New `setval('seq', n)` | `setval` enables restart support |
| Partial index; `DESC` index direction retained | no; no | no; no | Unchanged limitations |
| `STORED` generated columns; adding generated columns | no; no | no; no | Unchanged limitations |
| `information_schema.columns.is_generated` / `generation_expression` | `NULL` | `ALWAYS` / expression populated; same in `duckdb_columns()` | Scaffolder documents this as unresolvable |
| `SAVEPOINT` | no | no | Unchanged (`SupportsSavepoints => false`) |
| Updating or deleting a referenced row (FK) | `still referenced` error | **unchanged** | Dual-role FK workarounds remain required |
| Triggers, deferred `PRIMARY KEY`/`UNIQUE` | no | new in 2.0 | No EF mapping yet |

| # | Work item | Severity |
|---|---|---|
| M1 | Gate the "adding columns with constraints" and `ADD UNIQUE` limitations on an engine capability, and update the 14 `MigrationsDuckDBTest` overrides that now succeed on 2.0 (required, defaulted, `defaultValueSql` and primitive-collection columns, `Add_json_columns_to_existing_table`, `Alter_column_make_non_computed`, `Add_unique_constraint*`) to run the base behaviour | High |
| M2 | Read UNIQUE constraints in `DuckDBDatabaseModelFactory`; they are invisible to scaffolding and break `Add_unique_constraint*` on 2.0 | High |
| M3 | Reverse-engineer computed columns from `is_generated`/`generation_expression` on 2.0 (VIRTUAL only) | Medium |
| M4 | Replace exact-match engine-error assertions (`T_AlterObjectSchemaStmt`, `Schema element not supported yet!`, `No support for that ALTER TABLE option yet!`, `Adding generated columns after table creation`) in 8 tests with capability-based expectations. Adding a computed column now fails later, with `Can not create a STORED generated column!` | Medium |
| M5 | Use `setval` for `AlterSequenceOperation` restarts on 2.0 | Low |

### Storage, DuckLake and extensions

| # | Change in 2.0 | Provider exposure | Severity |
|---|---|---|---|
| X1 | The default storage version becomes `v2.0.0` (69); a file created or modified by 2.0 cannot be opened by 1.x. `storage_compatibility_version` reports `latest` instead of `v0.10.2` ([#22875](https://github.com/duckdb/duckdb/pull/22875)) | The provider exposes no `STORAGE_VERSION` on `ATTACH` (native, encrypted or `AlsoAttach`), so mixed 1.x/2.0 fleets and rollbacks break silently | **High** |
| X2 | DuckLake pairs with spec `1.1-dev1`. Existing 1.0 catalogs stay at 1.0 unless attached with `AUTOMATIC_MIGRATION TRUE` or `DUCKLAKE_VERSION`; downgrade is refused | `DuckLakeAttachCommandBuilder` emits neither option, so a 2.0 client would create 1.1 catalogs that 1.x clients cannot use. `DuckLakeCatalogMigrationTests` assumptions need re-checking | **High**, not verified |
| X3 | Encrypted writes need OpenSSL from `httpfs`; `force_mbedtls_unsafe` is renamed `debug_force_mbedtls_unsafe` (the error text still names the old setting) | Unchanged logic from 1.5.6, but offline or air-gapped installs must ship `httpfs`. Not verified | Medium |
| X4 | `quack` reaches 1.0 and is stable | The provider labels Quack "experimental in DuckDB 1.5.x"; docs and the opt-in gate can be revisited | Medium |
| X5 | `autoload_known_extensions` and `autoinstall_known_extensions` default to true | No change for the provider's explicit `INSTALL`/`LOAD` | Low |
| X6 | `GEOMETRY` is core; `ST_AsGeoJSON`/`ST_GeomFromGeoJSON` are core functions. The `spatial` extension is still pinned (`9926b58`) | NTS translations still load `spatial`; re-verify once extension builds exist | Medium, not verified |

### New 2.0 capabilities worth adopting later

- `json_set`, `json_insert`, `json_replace`, `json_remove`: native partial JSON updates for EF's
  `ExecuteUpdate` on JSON columns.
- `setval`: sequence restarts (M5).
- Generated-column metadata: computed-column scaffolding (M3).
- `ALTER TABLE ADD UNIQUE` and `ADD COLUMN` with constraints: fewer table rebuilds.
- `TIMESTAMPTZ_NS`: a `DateTimeOffset` nanosecond store type, once the driver supports it.
- `max_execution_time`, `checkpoint_on_detach`, `fsync_mode`, `operator_memory_limit`: candidates for
  typed options next to `MemoryLimit(...)` and `Threads(...)`.
- Deferred keys and triggers: no EF mapping planned; document only.

## Implementation status (`feature/duckdb-2.0`)

Phase 2 and the provider-side Phase 3 items are implemented. DuckDB 1.5.6 behaviour is unchanged: every new path is
selected by a capability that stays off on 1.5.

| Item | Implementation |
|---|---|
| Capability resolution | `DuckDBNativeEngine` reads `duckdb_library_version()` once; `DuckDBEngineCapabilities` turns DuckDB 2.0 or later into `SupportsAddColumnConstraints`, `SupportsAddUniqueConstraint` and `SupportsSequenceSetValue` for native profiles only. DuckLake and Quack never enable them |
| M1 | `MigrationsDuckDBTest` runs the base behaviour for the 14 add-column and unique-constraint tests when the capabilities are on. With `EnableMigrationTableRebuilds()`, `ADD UNIQUE` stays in place on 2.0 and is absorbed when the same table is rebuilt for another change. DuckDB 2.0 still rejects `ADD UNIQUE` and `NOT NULL` column additions on a table referenced by another table's foreign key (`Dependency Error: Cannot alter entry`); the generator fails those operations early, naming the foreign key, and only when the capabilities are on |
| M2 | `DuckDBDatabaseModelFactory` reads `UNIQUE` rows from `duckdb_constraints()` on both engines in one database-wide query. DuckDB reports generated names (`t_a_b_key`), never the declared one, so the name is left unset and EF's default alternate-key name is scaffolded |
| M3 | Generated columns scaffold as `VIRTUAL` computed columns when `information_schema.columns.is_generated = 'ALWAYS'`; on 1.5 the flag is `NULL` and the expression stays a default |
| M4 | The 8 engine-message assertions choose the 1.5 or 2.0 text from the capabilities, comparing only the first line because the 2.0 parser appends a `LINE 1:` block. Move and rename-sequence tests accept either documented text |
| M5 | `RestartSequenceOperation` renders `SELECT setval('"seq"', n, false);`, falling back to the model's start value when the operation has none |
| X1 | `StorageCompatibilityVersion("v1.5.0")` is validated once, in the builder, and stored in DuckDB's case-sensitive lower-case form. A native connection fails if the shared database instance it joins was opened with another `storage_compatibility_version`. Encrypted databases reject versions below v1.5.0, which DuckDB would write with its legacy encryption scheme; new encrypted files already default to v1.5.0 storage. Native files receive `storage_compatibility_version` in the connection string, because a later `SET` does not affect the main file; encrypted attachments render `STORAGE_VERSION`. It is rejected with Quack, with a caller-supplied connection that lacks the setting, and with DuckLake, whose extension always attaches its DuckDB metadata catalog with `STORAGE_VERSION 'latest'` |
| X2 | `DuckLakeVersion("1.0")` renders `DUCKLAKE_VERSION` for the primary catalog and every writable `AlsoAttach` catalog; read-only and historical profiles drop or reject it. `AutomaticMigration(...)` already existed and is unchanged. The DuckLake build paired with 1.5.6 rejects `DUCKLAKE_VERSION`, so the option is opt-in |
| S8 | The three Northwind tests order the correlated subquery by `OrderID` in both the database query and the in-memory expectation |
| S9 | `DuckDBIdentifierMetadata` is the union of the 1.5.5 list and the 2.0 `duckdb_keywords()`/`duckdb_functions()` names: 230 additions, no removals |

Focused coverage lives in `EngineVersionCapabilityTests` (capability matrix, SQL shape, end-to-end restart, unique and
scaffolding behaviour on the loaded engine) and `StorageVersionOptionsTests` (validation, attach rendering and the
`storage_version` tag of created files).

Verification on 15 October 2026 (net10.0, EF Core 10 line, identical build output for the two engines):

| Run | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Production gate (`scripts/test-suite.sh all`), native 1.5.6 | 723 | 0 | 464 |
| Full project, native 1.5.6 | 21,816 | 0 | 4,137 |
| Production gate filter, native 2.0 (`9a97642349`) | 720 | 3 | 464 |
| Full project, native 2.0 (`9a97642349`) | 21,598 | 218 | 4,137 |

All 218 failures on 2.0 are environmental: 155 `spatial`, 40 `ducklake` (both HTTP 404 for the local build), 22
encrypted writes that need httpfs/OpenSSL, and the single copied-output harness artefact. No migration, scaffolding
or Northwind failures remain. The `DuckLakeVersion` end-to-end path and the EF11 package line are not verified.

## Recommended plan

Each phase keeps 1.5.x behaviour intact until the 2.0 driver ships, and branches on
`IDuckDBEngineCapabilities` rather than on version checks, in line with `AGENTS.md`.

### Phase 1: driver preview (Skuirrels DuckDB.NET), before or on 21 October

1. Port upstream `a981b799` (new nightly artefact names) and `3162cae7` (streaming error timing) into the fork.
2. Handle `SqlNull` (36) as a `DBNull`-only column, and add `TimestampTzNs` (42), mapped to
   `DateTimeOffset`, plus `Variant` (41).
3. Map `DATA_CORRUPTION` and `RESOURCE_IN_USE` to `DuckDBErrorType`.
4. Publish `Skuirrels.DuckDB.NET.Data.Full 2.0.0-preview.1` against `v2.0.0-alpha*`, then `2.0.0` on release.

### Phase 2: provider capability changes, on a `feature/duckdb-2.0` branch

1. Add engine capabilities for `ALTER TABLE ADD COLUMN` with constraints and defaults, `ADD UNIQUE`,
   generated-column metadata and `setval`, resolved from the native engine version at the composition
   boundary (M1, M5).
2. Scaffold unique constraints and computed columns (M2, M3).
3. Add `StorageCompatibilityVersion(...)` for native, encrypted and additional attachments, plus DuckLake
   `DuckLakeVersion(...)` and `AutomaticMigration(...)`, validated at options time (X1, X2).
4. Add `TIMESTAMPTZ_NS` to `DuckDBTypeMappingSource` once D2 lands.
5. Regenerate `DuckDBIdentifierMetadata` from 2.0 `duckdb_keywords()` and `duckdb_functions()` (S9).

### Phase 3: tests and CI

1. Add a CI matrix lane that runs the production gate against the 2.0 native library, alongside 1.5.6,
   until 1.5 support ends on 1 March 2027.
2. Replace exact engine-message assertions with capability-driven expectations (M4).
3. Add `OrderBy` to, or skip with a specific reason, the three Northwind `First()` tests (S8).
4. Re-run spatial, DuckLake (local and PostgreSQL/MinIO), encryption and Quack suites on the official
   alpha or final binaries, which publish matching extensions.
5. Add focused tests for division-by-zero, `SQLNULL` projection and transaction invalidation on 2.0.

### Phase 4: documentation and release

1. Update `CAPABILITY-MAP.md` §2 for the limitations that 2.0 lifts, and the Quack experimental wording.
2. Document the storage-format one-way upgrade, DuckLake spec migration and `httpfs` requirement for
   encryption in an upgrade section of the README.
3. Decide the support window: the provider's next minor targets 2.0, while 1.5.x stays on the current
   driver line until March 2027.

## Open questions

- Will the fork follow upstream DuckDB.NET's 2.0 line, or keep its own preview cadence?
- Should the provider pin `STORAGE_VERSION 'v1.5.0'` by default for existing files during a transition
  window, or always write `v2.0.0`?
- Will DuckLake ship spec 1.1 final with DuckDB 2.0.0, or stay on `1.1-dev1`? This decides whether
  `AutomaticMigration` should default to off.
- Is 2.0 an LTS release? This decides how long the provider supports both lines.

## Reproducing

```bash
git -C ../duckdb fetch origin v2.0-cyanoptera
cmake -S ../duckdb -B build-v2 -DCMAKE_BUILD_TYPE=Release -DBUILD_EXTENSIONS="json;icu;parquet"
cmake --build build-v2 --target duckdb shell -j 12
```

Copy `test/DuckDB.EFCoreProvider.FunctionalTests/bin/Debug/net10.0` to a scratch folder and replace
`runtimes/osx/native/libduckdb.dylib` with `build-v2/src/libduckdb.dylib`. Then run
`dotnet test DuckDB.EFCoreProvider.FunctionalTests.dll` from that folder.
