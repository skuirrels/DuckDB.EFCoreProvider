# DuckLake sample

This sample uses the main `DuckDB.EFCoreProvider` package and the `UseDuckLake(...)` convenience entry point.
It creates a local DuckLake, writes through tracked `SaveChanges`, the appender-backed `BulkInsert`, and
`MERGE`-backed `Upsert`, then runs a LINQ aggregation.

```bash
dotnet run --project samples/DuckLake
```

Output is written under `ducklake-sample/` in the current directory. See
[`docs/DUCKLAKE.md`](../../docs/DUCKLAKE.md) for production named-secret configuration and the backend
limitations.
