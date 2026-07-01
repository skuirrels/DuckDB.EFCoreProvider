# Quickstart sample

A single-file console app that exercises the four things you will use most:

1. **`SaveChanges`** with a DuckDB-generated key (read back via `RETURNING`).
2. **`BulkInsert`** — the appender-backed fast path (~1M rows/s).
3. **LINQ analytics** — a `GroupBy` aggregate run by DuckDB's columnar engine.
4. **`Upsert`** — insert-or-update by primary key in one round-trip per batch.

## Run it

From the repository root:

```bash
dotnet run --project samples/Quickstart
```

It creates `quickstart.duckdb` in the working directory (recreated on each run) and prints the results of each step.

## Using the provider in your own app

This sample references the provider by **project path** because it lives inside the repository. In your own application, reference the **package** instead:

```bash
dotnet add package DuckDB.EFCoreProvider
```

Then the code is identical — `using DuckDB.EFCoreProvider.Extensions;` and `optionsBuilder.UseDuckDB("Data Source=app.duckdb")`. See the root [`README.md`](../../README.md) for the full feature walkthrough.
