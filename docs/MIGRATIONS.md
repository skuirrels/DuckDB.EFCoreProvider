# Migrations with DuckDB.EFCoreProvider

The provider implements EF Core migrations, so the standard `dotnet ef` workflow applies. This guide covers the workflow and the DuckDB-specific details that differ from a server database.

## Prerequisites

Install the EF Core tools and the design package once:

```bash
dotnet tool install --global dotnet-ef          # or: dotnet tool update --global dotnet-ef
dotnet add package Microsoft.EntityFrameworkCore.Design
```

`Microsoft.EntityFrameworkCore.Design` is a design-time dependency — it is not shipped in your published app.

## The workflow

```bash
# 1. Create a migration from the current model.
dotnet ef migrations add InitialCreate

# 2. Apply pending migrations to the database.
dotnet ef database update

# 3. After changing your model, add another migration and apply it.
dotnet ef migrations add AddSensorIndex
dotnet ef database update
```

To preview or hand off the SQL instead of applying it directly:

```bash
dotnet ef migrations script --output migrate.sql
```

> ⚠️ **Idempotent scripts are not supported.** `dotnet ef migrations script --idempotent` throws, because DuckDB has no procedural `IF` blocks to guard each migration (the SQLite provider has the same limitation). Generate plain scripts for a known starting point, or use `dotnet ef database update` / `Database.Migrate()`, which consult the history table themselves.

## Generated keys

Use `UseAutoIncrement()` for DuckDB-backed generated integer keys. The provider emits a sequence in the migration and uses `RETURNING` so EF Core receives the generated value after `SaveChanges`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
    => modelBuilder.Entity<Blog>().Property(b => b.Id).UseAutoIncrement();
```

This scaffolds a `CREATE SEQUENCE` plus a column default in the generated migration — no extra migration code required.

## What is supported

- Creating and dropping tables, columns, indexes, sequences, and schemas.
- Primary keys, foreign keys, unique constraints, and check constraints.
- Column comments and table comments.
- Generated/computed columns and store-generated defaults.
- The `__EFMigrationsHistory` history table and plain migration scripts (`dotnet ef migrations script`).

## DuckDB-specific limitations

These are **engine** limitations (DuckDB's `ALTER TABLE` and index surface), not provider gaps. The provider surfaces them clearly rather than silently producing wrong SQL.

DuckDB enforces foreign keys but supports only `NO ACTION` / `RESTRICT`, not database `CASCADE`, `SET NULL`,
or `SET DEFAULT`. The provider emits an enforced `NO ACTION` constraint and logs a migration warning when an
EF model requests an unsupported action. EF can still cascade tracked, loaded dependants client-side; deleting
a principal without loading its dependants is rejected by the database.

DuckDB also currently rejects an update or delete of a referenced row while any foreign-key row points to it,
even when an update changes only a non-key column. This is an engine limitation rather than a provider ordering
issue. Design write-heavy aggregates so referenced principals are stable, or update the dependent rows as part
of an application-controlled maintenance operation.

| Area | Detail |
|---|---|
| **Index direction** | DuckDB does not retain a per-column `ASC`/`DESC` direction; `CREATE INDEX ... (col DESC)` persists as `(col)`. Descending indexes are not generated. |
| **Filtered / partial indexes** | Not supported by DuckDB; `HasFilter(...)` is not emitted. |
| **Renaming an index** | Not supported; model a rename as drop-and-create. |
| **Some `ALTER COLUMN` shapes** | DuckDB's in-place column alteration is narrower than SQL Server's; certain type/nullability changes may require a table rebuild. |

### Opt-in table rebuilds

Foreign keys declared as part of `CREATE TABLE` are emitted normally. DuckDB cannot add or drop constraints
in place, so those migration operations fail clearly by default. Enable explicit rebuilds when the operational
trade-off is acceptable:

```csharp
options.UseDuckDB(
    "Data Source=app.duckdb",
    duckdb => duckdb.EnableMigrationTableRebuilds());
```

For primary-key, foreign-key, unique, and check-constraint changes, the provider then copies the table to a
temporary backup, recreates it from the target EF model, copies compatible non-computed columns back, drops
the backup, and recreates indexes. Run with a single writer, ensure free disk space roughly equal to the table,
and test rollback/recovery against a production-sized copy before enabling this in production. Column-shape
changes and constraint rebuilds must be placed in separate migrations; the provider rejects that mixed shape
rather than risking an ambiguous copy. Rebuilding a table referenced by foreign keys from other tables may also
be rejected by DuckDB, so rebuild dependants first or use an application-managed maintenance migration.

For the complete capability matrix and the engine-limitation rationale, see [CAPABILITY-MAP.md](CAPABILITY-MAP.md).

## Tips

- **Single writer.** DuckDB is single-writer and embedded. Run `database update` when no other process holds the database file open, or you will hit a lock error.
- **The migrations lock times out rather than waiting forever.** Migrators coordinate through a row in the `__EFMigrationsLock` table. If a migrator crashes while holding it, the row is left behind; rather than hanging indefinitely, later migrators wait up to **5 minutes** (configurable with `UseDuckDB(o => o.MigrationLockTimeout(...))`, or `Timeout.InfiniteTimeSpan` to wait forever) and then fail with a `TimeoutException` that reports how long the lock has been held and how to clear it: `DELETE FROM "__EFMigrationsLock"`.
- **Indexes for ad-hoc/Dynamic LINQ filters.** DuckDB's zone-map (min/max) pruning already accelerates range and equality scans on naturally ordered columns without any index. Add ART indexes (`HasIndex(...)`) for selective equality predicates on high-cardinality columns that are queried frequently; broad analytical scans rarely benefit. See [CAPABILITY-MAP.md](CAPABILITY-MAP.md) and [PERFORMANCE.md](PERFORMANCE.md).
- **Bulk loads bypass migrations history.** `BulkInsert` and `Upsert` write data only; they never touch schema or the migrations history table.
