# DuckLake gap follow-ups

This document tracks work intentionally left out of the generic provider changes for read scaling, thread
configuration, named-secret attachments, dynamic SQL, and snapshot commit metadata.

## Consumer handoff assessment

| Observation | Provider assessment |
|---|---|
| DuckLake read scaling was not discoverable | Accepted as a documentation gap. Independent read-only contexts/connections can attach the same catalog when the metadata backend supports concurrent clients. The provider does not prescribe tenant-session serialization, share a `DbContext` across threads, or create replicas. |
| `Threads(...)` should accompany `MemoryLimit(...)` | Accepted with a correction: DuckDB defines both as database-instance settings, not isolated per-session quotas. The provider applies the configured value whenever it opens a connection and documents that shared instances must use compatible values. |
| Additional catalogs only accepted local metadata | Accepted as a generic capability gap. `AlsoAttachNamedSecret(...)` now stores only a caller-created `TYPE ducklake` secret name, so the provider remains independent of PostgreSQL, object-storage vendors, and credential policy. |
| Dynamic DML should expose affected rows | Not implementable truthfully on the current DuckDB.NET reader contract. `RecordsAffected` is `-1`; SQL parsing, result-column guessing, or re-execution would leak policy or change semantics. Known DML should use `ExecuteSqlRawAsync` until upstream exposes the count. |
| Snapshot commit messages and named-secret setup were hard to discover | Accepted. The provider now exposes explicit transaction-scoped commit metadata and documents the named-secret shape while leaving author, message, credential creation, and logging policy with the caller. |

These verdicts are capability boundaries, not instructions for a consuming application's concurrency, tenancy,
authorization, or logging design.

## 1. Expose affected-row counts on the dynamic result path

### Current constraint

`DuckDBDynamicQueryResult` streams a `DbDataReader`, and the current DuckDB.NET reader reports
`RecordsAffected == -1`. The provider cannot produce a trustworthy value without parsing SQL, guessing from
result-column names, or executing the command again.

### Upstream dependency

- Add a DuckDB.NET reader/native-result API that exposes the row-change count when DuckDB supplies one.
- Define how the API distinguishes `0` affected rows from an unavailable count.
- Preserve `-1` or an equivalent unavailable state for statements that do not have a meaningful count.

### Provider work after upstream support exists

- Add a nullable affected-row value to `DuckDBDynamicQueryResult`.
- Populate it without buffering rows or executing the SQL more than once.
- Test `INSERT`, `UPDATE`, `DELETE`, zero-row DML, result-producing statements, multi-statement SQL, and
  cancellation/failure paths.
- Update the README, type-mapping contract, capability map, and package XML documentation.

Until then, consumers that know they are executing DML and require the count should use `ExecuteSqlRawAsync`.

## 2. Extend the external PostgreSQL and MinIO DuckLake lane

The existing PostgreSQL/MinIO lane passed against the 1.14.0 code and official DuckDB.NET 1.5.3 dependency graph on
2026-07-22. It proves the primary named-secret profile, but it does not yet combine remote metadata with an
additional named-secret catalog or simultaneous reader contexts.

Remaining work:

- Extend the disposable PostgreSQL/MinIO fixture with a second catalog and secret.
- Verify an additional named-secret catalog through `AlsoAttachNamedSecret(...)`.
- Exercise independent read-only contexts/connections against the same catalog.
- Confirm that no credential values appear in EF options, exceptions, logs, snapshots, or test artifacts.
- Keep `scripts/test-ducklake-external.sh` as the single local and CI entry point for the expanded matrix.

Release decision:

- Decide whether this lane is a required PR check for DuckLake connection changes or a release-only gate.

## 3. Keep stable package eligibility verified

Version 1.14.0 was released against the official `DuckDB.NET.Data.Full` version `1.5.3`. Version 1.14.1 uses the
stable `Skuirrels.DuckDB.NET.Data.Full` version `1.5.5` performance fork. The fork retains the official DuckDB.NET
assembly and namespace identities, so it replaces rather than accompanies the official packages.

Before release:

- Verify that the resolved graph contains only `Skuirrels.DuckDB.NET.Data.Full` and
  `Skuirrels.DuckDB.NET.Bindings.Full` version `1.5.5`, with no official DuckDB.NET data or bindings packages.
- Run the full solution tests, DuckLake tests, package-validation checks, and a clean-consumer smoke test.
- Build the stable `.nupkg` and `.snupkg`, then inspect the nuspec, dependency graph, README, XML documentation,
  native assets, and package identity.
- Update the performance and capability documentation where it names a specific DuckDB.NET version.

Do not install the fork alongside the official packages, suppress NuGet's stable-to-prerelease dependency check,
or label a prerelease dependency graph as stable.
