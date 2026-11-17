# Engineering guidance

## Architecture

- Keep provider capabilities in the central immutable `IDuckDBEngineCapabilities` model. Translate backend options into capabilities at the composition boundary; consumers must branch on capabilities rather than profile checks such as `IsDuckLake`.
- Separate decisions from rendering. Planners resolve EF metadata, validate supported shapes, and produce immutable plans; SQL generators only render those plans through `ISqlGenerationHelper`.
- Represent query features as typed `SqlExpression` nodes before final SQL generation. Do not infer feature semantics from strings in the renderer.
- Keep public extension methods thin. Put orchestration and policy in focused provider services, with one clear owner for each decision.
- Validate unsupported model/configuration combinations as early as possible, preferably during model validation rather than during command execution.
- Route identifiers, parameter names, and literals through EF/provider helpers. Do not concatenate user- or model-derived SQL fragments directly.
- Prefer incremental, behaviour-preserving refactors. Preserve public API and package compatibility unless a breaking change is explicitly approved.
- Avoid speculative abstractions. Introduce an interface when it represents a genuine service boundary or has multiple consumers, not merely to wrap one pure helper.

## Testing requirements

- Characterise existing behaviour before moving responsibilities between components.
- Add focused tests for planners and capability matrices, plus SQL-shape regression coverage where rendering is affected.
- Exercise both native DuckDB and DuckLake profiles whenever capability-dependent code changes.
- Keep end-to-end SaveChanges/query/migration tests for affected paths; unit tests alone are not sufficient for provider behaviour.
- Run the targeted test class while iterating, then the full solution build and the repository's production test gate before completing the change.
- Treat formatting, package/API validation, and a final diff review as part of regression prevention.

## Review guidance

- Treat the engine limits below as accepted. Do not report them as findings, open work or regressions in reviews or readiness passes; report only a change that makes one worse, hides it, or handles it less clearly than documented. The evidence and the provider's handling of each are in [section 2 of the capability map](docs/CAPABILITY-MAP.md#2-duckdb-limitations--engine--not-planned-here) and, for STRUCT foreign keys, the README's STRUCT section.
  - Foreign key actions are limited to `NO ACTION`/`RESTRICT`; other cascade actions are emitted as `NO ACTION` with a migration warning.
  - `ALTER TABLE ADD/DROP CONSTRAINT` for foreign keys is rejected by DuckDB; `EnableMigrationTableRebuilds()` is the supported path, and DuckDB may still reject rebuilding a referenced table.
  - Native DuckDB rejects changing a row's outbound foreign key while an inbound dependant references the row; the provider reports it as an atomic `DbUpdateException`.
  - A native DuckDB file has a single writer, and DuckDB has no savepoints.
  - DuckLake has no physical keys, foreign keys, unique or check constraints, or indexes.
  - STRUCT foreign keys are query-only and require file-backed entities.
- Re-check this list when the DuckDB or DuckLake engine version changes, and remove any limit the engine has lifted.

## Release requirements

- Set every GitHub Release title to exactly the version tag in `v{major}.{minor}.{patch}` form, for example `v1.14.2`.
- Do not put product names, summaries, generated prose, dependency names, or change descriptions in the release title. Put those details in the release notes.
- Before completing a release, verify that the repository's Releases sidebar displays the full version without truncation.
