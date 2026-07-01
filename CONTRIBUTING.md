# Agent Instructions

This repository is `DuckDB.EFCoreProvider`, an Entity Framework Core provider for DuckDB built on DuckDB.NET.

## Compatibility

- Support EF Core 10.0.x on .NET 10.
- Do not add compatibility work for EF Core versions older than 10.
- Do not claim EF Core 11+ support until the provider is retargeted, rebuilt, and passes the EF Core 11 relational specification tests.
- Keep Microsoft EF Core package versions aligned on the same patch version across provider and test projects.

## Engineering Standards

- Treat this as a production provider, not a demo or proof of concept.
- Preserve EF Core provider contracts for SQL generation, updates, migrations, transactions, value generation, concurrency, and model validation.
- Prefer provider-local implementations that follow EF Core relational provider patterns instead of ad hoc SQL string handling.
- Quote identifiers and literals through existing provider helpers; do not concatenate untrusted schema, table, column, or sequence names directly into SQL.
- Keep DuckDB behaviour explicit. If DuckDB does not support a relational feature, fail clearly or document the provider limitation.
- Do not broaden support claims without corresponding tests.

## Write Provider Scope

Write support must cover:

- `SaveChanges` inserts, updates, and deletes.
- Generated keys and store-generated values.
- `RETURNING` behaviour where required by EF Core.
- Optimistic concurrency checks.
- Transactions and rollback behaviour.
- Migrations for tables, columns, constraints, generated values, and history table behaviour.
- Clear validation for unsupported value-generation or schema patterns.

## Testing

Use the repository test-suite script for repeatable gates:

```bash
scripts/test-suite.sh write-critical
scripts/test-suite.sh write-broad
scripts/test-suite.sh migrations
scripts/test-suite.sh updates
scripts/test-suite.sh all
scripts/test-suite.sh full-project
```

Minimum expectations:

- Run `scripts/test-suite.sh write-critical` for focused write-provider changes.
- Run `scripts/test-suite.sh all` before considering production write-provider work complete.
- Run `scripts/test-suite.sh full-project` when changing shared provider infrastructure, query translation, type mapping, migrations, or EF Core version dependencies.
- Add focused tests for every bug fix and every new supported behaviour.
- Keep skipped tests intentional and provider-specific; do not hide failures by broad skip changes.

## Documentation

- Keep `README.md` simple to start with, then include detailed examples for real usage.
- Document compatibility as EF Core 10.0.x and .NET 10 unless the project is deliberately retargeted.
- Document limitations honestly, especially OLAP/embedded DuckDB constraints and EF Core provider gaps.
- Doc index: `README.md` (overview/usage), `samples/Quickstart` (runnable sample), `CHANGELOG.md` (release history), `docs/MIGRATIONS.md` (EF migrations), `docs/CAPABILITY-MAP.md` (feature matrix/limits), `docs/PERFORMANCE.md` (benchmarks), `SECURITY.md`, `VERSIONING.md`. Keep version references and the changelog in sync with the package version on each content change.

## Workflow

- Inspect current git status before editing.
- Do not revert unrelated user changes.
- Keep changes scoped to the requested behaviour.
- Prefer small, reviewable commits when committing is requested.
- Do not stage, commit, push, or open a pull request unless explicitly asked.
