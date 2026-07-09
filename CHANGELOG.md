# Changelog

All notable changes to `DuckDB.EFCoreProvider` are documented here. The package follows [semantic versioning](VERSIONING.md); the same notes ship in the NuGet package's release notes.

## 1.2.4

- **Blob reads allocate roughly half as much.** Materializing a `byte[]` column no longer double-buffers the driver's stream through a growing `MemoryStream` plus `ToArray()`; DuckDB.NET's blob stream is seekable with a known length, so the provider reads once into an exact-size array. On 2,000 rows × 4 KB blobs, allocations fell from 16.49 MB to 8.51 MB (~2.1× payload → ~1.1×) and Gen0 collections halved. Also adds test coverage for eager-loading (`Include`) across two parquet-backed sets. No public API changes.

## 1.2.3

- **Further allocation cleanup in provider hot paths.** SaveChanges batching no longer allocates throwaway LINQ iterators when deciding whether consecutive insert/update/delete commands can merge into one statement; on a 4,000-row batched insert this cut allocations by ~12% (19.74 MB → 17.42 MB). Query SQL generation drops a small per-table allocation in the file-source lookup, and remote-archive path detection now uses a source-generated regex. No public API changes.

## 1.2.2

- **Performance and allocation cleanup.** Provider-owned write hot paths allocate less during batched writes. `BulkInsert` now uses typed appender delegates for common CLR property types, SaveChanges batching reuses column-modification views more aggressively, and `Upsert` now stages each batch through DuckDB's appender into a temporary table before a set-based `INSERT ... ON CONFLICT` merge. In the local 1,000-row allocation benchmark, `UpsertBatch` improved from 45.706 ms / 2203.38 KB to 24.455 ms / 1046.66 KB. No public API changes.

## 1.2.1

- **Tiered storage hardening.** Cold views now require both an archive watermark and at least one archive file, so missing or empty archives no longer hide hot rows. Archive cleanup is key-aware and only deletes hot rows when the same primary key is present in cold storage, preserving late/backdated rows inserted before an existing watermark. Generated tiered SQL now consistently honours schema-qualified hot tables for views, archive copy/delete, and child joins. Local purge now skips malformed partition directories instead of failing the purge. No public API changes.

## 1.2.0

- **Tiered storage — cold archive on object storage.** Point `archivePath` at an `s3://` URL (also `gcs://`/`gs://`, `r2://`, `azure://`) and DuckDB reads and writes the cold Parquet there directly via its `httpfs` extension, while hot data stays in the local `.duckdb` file and the union views span both. Load `httpfs` and configure credentials with an EF Core `DbConnectionInterceptor`; the provider now opens its own connections through EF Core, so that interceptor runs for the archive operations too. Partitioned writes, incremental month-by-month appends, idempotent re-runs after a crash, and the no-dup/no-gap invariant all behave identically on object storage (verified against MinIO), and range-scoped reads still prune partitions and row-groups so only the needed byte ranges are fetched. Retention on object storage is delegated to the bucket: `PurgeArchiveOlderThan` throws `NotSupportedException` for a remote archive (object stores can't delete files through DuckDB) — enforce it with a lifecycle rule on the hive-partitioned prefix instead. No public API changes. See [docs/TIERED-STORAGE.md](docs/TIERED-STORAGE.md#6-cold-storage-on-s3-and-other-object-stores).

## 1.1.0

- Add **tiered storage**: keep recent ("hot") rows in the writable DuckDB file and offload older ("cold") rows to hive-partitioned Parquet, then report across the whole history with LINQ. Tiering works over a **relational aggregate** — a root and its declared children move together, governed by the root's date. The **hot side stays ordinary EF Core** (regular entities, relationships, `SaveChanges`, `Include`); the **cold/reporting side** uses a keyless read-model type per table mapped to a generated union view. Configure with `modelBuilder.ToTieredStore<TRoot>(r => r.Date, archivePath, granularity).WithReadModel<TRootReport>().Including<TChild>(r => r.Children, c => c.WithReadModel<TChildReport>())`. Create the control table and views with `EnsureCreated()` (automatic) or `context.Database.EnsureTieredStoresCreated()` (migrations). Offload with `context.Database.ArchiveTierAsync<TRoot>(cutoff)` (also synchronous `ArchiveTier<TRoot>`) and enforce retention with `PurgeArchiveOlderThan<TRoot>(olderThan)`. The offload is idempotent and crash-safe: each table's `COPY` overwrites only the partitions it writes; the views filter the hot side to `>= watermark` (roots) or "root is hot" (children), so reads never double-count or drop a row even before the leaf→root delete runs; a re-run self-heals leftover hot rows. The union views tolerate schema evolution (a column added after archiving reads back as `NULL`); the child hot/cold guard can be relaxed for deep aggregates with `.WithoutHotChildFilter()`. Reserved partition column names (`year`/`month`/`day`), a child without a single-column foreign key to its declared parent, a read-model column missing on its source, and overlapping aggregate archive paths are all rejected at model validation. See [docs/TIERED-STORAGE.md](docs/TIERED-STORAGE.md) and the runnable [`samples/TieredStorage`](samples/TieredStorage). No changes to existing APIs.

## 1.0.22

- Build & packaging: ship [SourceLink](https://github.com/dotnet/sourcelink) metadata, deterministic CI builds, and a `.snupkg` symbol package so consumers get source-mapped stack traces and step-into debugging; the publish workflow now pushes the symbol package alongside the main package. Enable NuGet package validation against the previous release to guard the public API. Documentation: correct the spatial docs and shipped IntelliSense — geometry is stored in DuckDB's native `GEOMETRY` column type (WKT is only the driver wire format), not WKT-in-`VARCHAR`. No code or public API changes. (`DuckDB.EFCoreProvider.NTS` updated to 1.0.6.)

## DuckDB.EFCoreProvider.NTS 1.0.6

- Build & packaging: ship SourceLink metadata, deterministic CI builds, and a `.snupkg` symbol package; enable NuGet package validation against the previous release. Documentation: correct the geometry storage description in the shipped IntelliSense — geometry is stored in DuckDB's native `GEOMETRY` column type (WKT is only the driver wire format), not WKT-in-`VARCHAR`. No behavioural or public API changes. Tracks core `DuckDB.EFCoreProvider` 1.0.22.

## DuckDB.EFCoreProvider.NTS 1.0.5

- Fill in the shipped IntelliSense XML docs: add summary/param/returns tags to the public entry points (`UseNetTopologySuite`, `AddEntityFrameworkDuckDBNetTopologySuite`) and the standard internal-API disclaimer to internal translator/plugin classes, matching the core package's earlier documentation pass. No behavioural or public API changes. `DuckDB.EFCoreProvider` core package is unchanged at 1.0.21.

## 1.0.21

- Update Entity Framework Core dependencies from 10.0.8 to 10.0.9 (and `DuckDB.EFCoreProvider.NTS` to 1.0.4). No code or public API changes.

## 1.0.20

- Rename the package, assembly, and root namespace from `DuckDB.EFCoreDriver` to `DuckDB.EFCoreProvider` (and `DuckDB.EFCoreDriver.NTS` to `DuckDB.EFCoreProvider.NTS`, version 1.0.3). This is a drop-in rename: update your package references and replace `using DuckDB.EFCoreDriver...` with `using DuckDB.EFCoreProvider...`. No behavioural or public API changes beyond the names.

## 1.0.19

- Documentation: remove decorative emoji from the README. No code or public API changes.

## 1.0.18

- The migrations lock now waits a bounded time (default 5 minutes, configurable with `UseDuckDB(o => o.MigrationLockTimeout(...))`, `Timeout.InfiniteTimeSpan` to wait forever) instead of retrying indefinitely. A lock row left behind by a crashed migrator previously hung `database update` / `Migrate()` forever; it now fails with a `TimeoutException` reporting how long the lock has been held and how to clear it (`DELETE FROM "__EFMigrationsLock"`).
- Fix a latent crash in migration-lock contention: when another migrator actually held the lock, the acquisition loop threw a `NullReferenceException` instead of waiting.

## 1.0.17

- Polish the shipped IntelliSense documentation: fill previously empty param/returns/exception tags on the public entry points (`UseDuckDB`, `AddDuckDB`, `EF.Functions` row-value comparisons, SQL expression constructors) and fix typos in doc comments and exception messages. No behavioural changes.

## 1.0.16

- Ship the IntelliSense XML documentation file in the package, so the public API docs appear in consumers' IDEs (also in `DuckDB.EFCoreProvider.NTS` 1.0.2).
- Add explanatory messages to previously message-less exceptions, most notably the `NotSupportedException` thrown when generating idempotent migration scripts (a DuckDB engine limitation, as with SQLite).
- Remove three empty placeholder classes (`DuckDBTableExtensions`, `DuckDBTableBuilderExtensions`, `DuckDBEntityTypeMappingFragmentExtensions`) that had no members and no function.
- Documentation: correct the [migrations guide](docs/MIGRATIONS.md), which wrongly recommended `dotnet ef migrations script --idempotent`; idempotent scripts are not supported. No behavioural changes.

## 1.0.15

- Documentation: standardise prose on British English spelling. No code or public API changes.

## 1.0.14

- Documentation: add a runnable [Quickstart sample](samples/Quickstart) (`dotnet run --project samples/Quickstart`), a Troubleshooting/FAQ section and a configuration & connection-string reference in the README, a [migrations guide](docs/MIGRATIONS.md), and this root changelog. No code or public API changes.

## 1.0.13

- Add `UseDuckDB(o => o.FileSearchPath("/data"))`: configures DuckDB's `file_search_path` (one or more comma-separated directories that relative file paths — e.g. in `[FromParquet]` — are resolved against) when a connection opens. Documentation refresh.

## 1.0.12

- Version bump; no functional changes since 1.0.11.

## 1.0.11

- Internal: load the SQL-generation reserved-word set lazily and resiliently instead of in a static constructor that opens a connection (deferred I/O; graceful degradation instead of a `TypeInitializationException`). Reduce repeated XML-doc boilerplate and split the query translating visitor and query SQL generator JSON handling into partial classes. No public API or behaviour changes.

## 1.0.10

- Build hygiene: enforce code style (`EnforceCodeStyleInBuild`) and treat warnings as errors across the repository. No public API or behaviour changes.
- Internal: name the batch script-size estimation constants, note `SharedTypeExtensions` provenance, and DRY the functional-test fixtures onto a shared base.

## 1.0.9

- Robustness: `Exists()` now determines a file database's existence from the file's presence, so a database whose file exists but is held open by another writer process is no longer misreported as non-existent.
- Scaffolding: emit a warning when a primary/foreign-key column reported as nullable by DuckDB is scaffolded as non-nullable.
- Internal cleanup and additional test coverage (database existence, decimal precision/scale, array round-trips); no public API changes.

## 1.0.8

- Add `UseDuckDB(o => o.MemoryLimit("4GB"))`: configures DuckDB's `memory_limit` (the buffer-manager cap) when a connection opens. When not set, DuckDB's default of 80% of physical RAM is left unchanged. Accepts DuckDB size syntax (e.g. `"4GB"`, `"512MB"`, `"75%"`).

## 1.0.7

- Add `context.Upsert(...)` / `UpsertAsync(...)`: high-throughput insert-or-update backed by batched DuckDB `INSERT ... ON CONFLICT (key) DO UPDATE`. Inserts new rows and updates existing ones by primary key in one round-trip per batch (~8× faster than read-then-insert-or-update). Raw fast path (no change tracking / store-generated keys / shadow or computed columns); all mapped non-key columns are overwritten, key-only entities use `ON CONFLICT DO NOTHING`.

## 1.0.6

- Fix `DateTime.Date` translation to return a `DateTime` (midnight) instead of a `DateOnly`, so projecting or grouping by `.Date` (e.g. chart/panel aggregation) no longer fails with "No coercion operator is defined between types 'System.DateOnly' and 'System.DateTime'".

## 1.0.5

- Add opt-in `SaveChanges` insert batching (`EnableBulkInsertBatching`): merges consecutive inserts into a single multi-row `INSERT ... VALUES (..),(..)` statement (~10× faster) while keeping change tracking and store-generated keys.
- Add opt-in `SaveChanges` update batching (`EnableBulkUpdateBatching`): merges eligible (primary-key, no computed columns) updates into a single `UPDATE ... FROM (VALUES ..)` statement (~8× faster); updates with concurrency tokens or computed columns fall back to the per-row path.
- Add opt-in `SaveChanges` delete batching (`EnableBulkDeleteBatching`): merges eligible (primary-key) deletes into a single `DELETE ... WHERE key IN (..)` / `DELETE ... USING (VALUES ..)` statement (~14× faster), especially effective for orphan cleanup and child-collection replacement; deletes with concurrency tokens fall back to the per-row path.
- All three are disabled by default and independent; each merged run is atomic. Tune merge size with `MaxBatchSize`.

## 1.0.3 and earlier

- Initial public versions: EF Core 10 provider integration, LINQ query translation, migrations, JSON/array/decimal/temporal type mapping, file-source querying (`read_parquet`/`read_csv`/`read_json`), auto-increment keys, spatial support, and the EF Core relational conformance test coverage. See the package release notes for the per-issue list.
