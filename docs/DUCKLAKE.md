# DuckLake backend profile

DuckLake support ships in the main `DuckDB.EFCoreProvider` package. It is a first-class backend profile,
not a second provider or a connection callback: the provider loads the `ducklake` extension, runs secret
initialization, attaches the catalog, and selects it every time it opens a connection.

## Local catalog

```csharp
using DuckDB.EFCoreProvider.Extensions;

builder.Services.AddDbContext<AnalyticsContext>(options =>
    options.UseDuckLake(
        "catalog/analytics.ducklake",
        duckLake => duckLake
            .CatalogName("analytics")
            .DataPath("data/analytics")));
```

The convenience entry point uses an in-memory DuckDB host connection. The data persists in the DuckLake
metadata catalog and data path, not in that host connection.

For an existing catalog, `DataPath(...)` is optional because DuckLake reads the persisted data path from its
metadata. Set `CreateIfNotExists(false)` when a deployment must fail rather than create an empty catalog.

## Production profile with a named secret

Keep credentials out of EF options, models, logs, and generated SQL. Create a DuckDB secret in
`ConfigureConnection(...)`, then store only its name in the DuckLake profile (`UseDefaultSecret()` is also
available for DuckDB's unnamed secret):

```csharp
options.UseDuckLake(
    duckLake => duckLake
        .UseNamedSecret("application_lake")
        .CatalogName("analytics")
        .CreateIfNotExists(false),
    duckDB => duckDB
        .LoadExtension("httpfs")
        .ConfigureConnection(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = BuildCreateSecretFromEnvironment();
            command.ExecuteNonQuery();
        }));
```

The callback runs after extensions load and before `ATTACH`. Resolve credentials inside the callback. Do not
put a PostgreSQL connection string or object-store key directly in the model or options profile.
`UseLocalMetadata(...)` rejects URI-style metadata sources; remote metadata must use a named or default secret.

Additional catalog options:

| Option | Behaviour |
|---|---|
| `.ReadOnly()` | Attaches with `READ_ONLY`; creation and automatic catalog migration are disabled. |
| `.AutomaticMigration()` | Allows DuckLake to upgrade an older metadata schema while attaching. Enable deliberately during a controlled deployment. |
| `.DataPath(path, overrideForCurrentConnection: true)` | Uses a different data path for every connection created by this profile without changing the path stored in DuckLake metadata. Other clients continue to use the persisted path, so this is not a catalog-relocation mechanism. |
| `.CreateIfNotExists(false)` | Fails attachment if the catalog does not exist. Recommended after provisioning. |

Before its first command, the provider initializes DuckLake even when the underlying provider-owned or
caller-owned connection is already open. It also recognizes and selects a DuckLake catalog already attached
under the configured name, allowing one caller-owned open connection to be reused across context instances.
Opening the raw connection does not itself attach DuckLake; initialization occurs when EF next uses it.

## Supported EF workflows

- LINQ queries and raw SQL against the selected DuckLake catalog.
- `SaveChanges` inserts, updates, and deletes using client-assigned values.
- optimistic concurrency detection from affected-row counts, without `RETURNING`.
- transactions supported by DuckDB/DuckLake.
- `Database.EnsureCreated()` for an empty catalog. Unsupported physical key, foreign-key, unique, check, and
  index definitions are omitted from the generated DuckLake DDL while remaining logical EF model metadata.
- `BulkInsert`/`BulkInsertAsync` through the DuckDB appender.
- `Upsert`/`UpsertAsync` through DuckLake-compatible `MERGE INTO`.
- read-only profiles and named-secret profiles.

## Model rules and limitations

DuckLake is a lakehouse backend, not native DuckDB storage. Its physical contract changes several EF
assumptions:

- Primary keys, foreign keys, unique constraints, indexes, and check constraints are not physically enforced.
  EF still uses configured keys for identity resolution, relationships, update predicates, and `MERGE`, but the
  application is responsible for uniqueness and referential integrity. Concurrent writers can create duplicate
  logical keys because DuckLake has no unique constraint to arbitrate a race.
- Sequences, auto-increment values, generated columns, and SQL default expressions are rejected at model
  validation. Use client-assigned `Guid` values, configure numeric keys with `ValueGeneratedNever()`, or supply
  an explicit client-side generator with `HasValueGenerator(...)`. DuckLake can store literal defaults, but a
  tracked property must also use `ValueGeneratedNever()` and receive an application value because EF cannot
  read the default back without `RETURNING`.
- DuckLake rejects `INSERT/UPDATE/DELETE ... RETURNING`. Store-generated values that EF must read back are
  therefore unsupported. The provider uses one non-returning statement per tracked change and checks the
  affected-row count for optimistic concurrency.
- `EnableBulkInsertBatching`, `EnableBulkUpdateBatching`, and `EnableBulkDeleteBatching` are incompatible with
  the DuckLake profile. Use `BulkInsert` for ingestion and `Upsert` for set-based merge workloads.
- Provider tiered storage cannot be combined with DuckLake. DuckLake already owns Parquet layout, snapshots,
  compaction, and data-file lifecycle.

## Schema lifecycle

`EnsureCreated()` is supported for initial provisioning and is idempotent for a non-empty catalog. EF Core
migrations are deliberately rejected: the current migrations history and lock contract depends on an enforced
unique key and `RETURNING`, neither of which DuckLake provides. Apply reviewed DuckLake schema-evolution SQL
through a controlled deployment process instead.

`Database.CanConnect()` and `CanConnectAsync()` use a non-creating, read-only attachment probe. They return
`false` for a missing catalog even when the configured profile allows `EnsureCreated()` to create it, so health
checks do not provision or mutate storage.

`EnsureDeleted()` is also disabled. A DuckLake catalog may reference remote or shared metadata and object
storage, so deleting the in-memory DuckDB host would be misleading and deleting the backing stores would be
dangerous. Destroy metadata and data storage explicitly with their native administration tools.

Database-first scaffolding does not currently accept a DuckLake profile through `dotnet ef dbcontext scaffold`.
Create the EF model explicitly or scaffold a compatible native DuckDB copy, then review keys and value-generation
settings before using the model with DuckLake.

## Operational notes

- `INSTALL ducklake`/`LOAD ducklake` runs through the provider extension pipeline. Production images should
  pre-install/cache the extension or allow access to the configured DuckDB extension repository.
- Automatic catalog migration is off by default. A read-only connection cannot migrate an older catalog.
- The profile name and paths are safely quoted. Catalog and secret names are restricted to ASCII identifiers;
  credentials stay inside the secret callback.
- Test schema and write behaviour against the same DuckDB/DuckLake extension versions used in production.

Provider contributors can run `scripts/test-ducklake-external.sh` to exercise the named-secret profile against
PostgreSQL metadata and MinIO S3-compatible storage. The same isolated integration lane runs on Linux in CI.
The functional suite also migrates an official upstream DuckLake v0.3 catalog fixture to verify the real persisted
`AUTOMATIC_MIGRATION` path.

See the runnable [`samples/DuckLake`](../samples/DuckLake) application for local `EnsureCreated`, tracked writes,
bulk insert, `MERGE` upsert, and LINQ analytics.
