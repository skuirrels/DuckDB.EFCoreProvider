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

A PostgreSQL-backed profile secret has this shape; create the referenced PostgreSQL and object-storage secrets on
the same connection:

```sql
CREATE SECRET application_lake (
    TYPE ducklake,
    METADATA_PATH '',
    DATA_PATH 's3://bucket/lake/',
    METADATA_PARAMETERS MAP {'TYPE': 'postgres', 'SECRET': 'application_postgres'});
```

Other metadata backends use different parameters. `UseNamedSecret(...)` requires a `TYPE ducklake` profile but does
not assume PostgreSQL or copy any secret contents into EF options.

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
- streaming unknown-shape SQL through `SqlQueryDynamicRawAsync` / `SqlQueryDynamicAsync`.
- typed snapshot and physical-file maintenance through `Database.DuckLake()`.
- catalog-wide historical LINQ profiles through `AsOfSnapshot(...)` and `AsOfTimestamp(...)`.
- additional local or named-secret catalogs, read-only by default, for catalog-qualified dynamic/raw SQL through
  `AlsoAttach(...)` and `AlsoAttachNamedSecret(...)`.

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

Database-first scaffolding accepts a local DuckLake metadata file through a `ducklake:` source:

```bash
dotnet ef dbcontext scaffold \
  "ducklake:/absolute/path/metadata.ducklake" \
  DuckDB.EFCoreProvider
```

The scaffolder attaches the catalog read-only, filters metadata by the exact selected `table_catalog`, and applies
the normal `--schema` and `--table` filters. DuckLake does not expose physical primary or foreign keys, so generated
entities are keyless until reviewed and given logical keys explicitly. The scaffolder does not exclude names by a
`ducklake_` prefix because that could hide valid user tables; exact catalog selection prevents tables from other
attachments from entering the model. Named-secret and remote profiles require a caller-initialized open
`DuckDBConnection` so credentials remain outside command-line arguments.

## Operational notes

- `INSTALL ducklake`/`LOAD ducklake` runs through the provider extension pipeline. Production images should
  pre-install/cache the extension or allow access to the configured DuckDB extension repository.
- Automatic catalog migration is off by default. A read-only connection cannot migrate an older catalog.
- The profile name and paths are safely quoted. Catalog and secret names are restricted to ASCII identifiers;
  credentials stay inside the secret callback.
- Test schema and write behaviour against the same DuckDB/DuckLake extension versions used in production.

### Concurrency and read scaling

DuckLake concurrency is governed by the metadata catalog as well as by EF Core:

| Metadata catalog | Intended concurrency model |
|---|---|
| DuckDB file | One client. Use it for local, single-client workloads. |
| SQLite | Multiple local clients, with SQLite locking and retry limitations. |
| PostgreSQL | Multiple local or remote clients. Use this profile for multi-user deployments. |

`ReadOnly()` controls the permissions of one DuckLake attachment; it does not create or manage a replica. Scale
reads with separate read-only `DbContext` instances/connections attached to a metadata catalog that supports the
required client concurrency. A `DbContext` is not thread-safe and must not run parallel operations. Use one
context per concurrent operation, for example through `IDbContextFactory<TContext>`.

DuckLake can coordinate multiple writers when the metadata catalog supports them, but it does not physically
enforce the logical primary/unique keys in the EF model. Applications that permit concurrent writes must provide
their own uniqueness and workflow guarantees. The provider does not add process-local semaphores, distributed
leases, scheduling, or authorization policy.

## Historical queries

Use a dedicated context profile to attach the entire DuckLake catalog at one historical snapshot. Normal EF LINQ
then runs against a consistent, read-only view, including joins across tables in that catalog:

```csharp
var historicalOptions = new DbContextOptionsBuilder<AnalyticsContext>()
    .UseDuckLake(
        "metadata.ducklake",
        lake => lake.AsOfSnapshot(snapshotId))
    .Options;

await using var historical = new AnalyticsContext(historicalOptions);
var rows = await historical.Events.Where(e => e.RecordedAt < cutoff).ToListAsync();
```

`AsOfTimestamp(DateTimeOffset)` selects the latest snapshot at or before that time. Both historical modes force
`READ_ONLY`, disable catalog creation and automatic metadata migration, and require a separate `DbContext`. Use
`context.Database.DuckLake().GetSnapshotsAsync()` to discover the provider-native 64-bit snapshot identifiers and
timestamps. The provider intentionally applies time travel catalog-wide rather than rewriting individual table
expressions, so multi-table LINQ queries cannot accidentally mix snapshot versions.

For a deliberately table-scoped read, start directly from a `DbSet`:

```csharp
var rows = await context.Events
    .AsOfSnapshot(snapshotId)
    .Where(e => e.RecordedAt < cutoff)
    .ToListAsync();
```

`DbSet.AsOfSnapshot(...)` and `DbSet.AsOfTimestamp(...)` emit DuckLake's native `AT (...)` table clause and
remain LINQ-composable. The pin applies only to that root. Another root in a join, an included navigation, or a
split query remains current unless it has its own historical root, so use the catalog-wide profile whenever a
multi-table query needs one coherent point in time. Table-scoped and catalog-wide pins cannot be combined.

## Maintenance

`Database.DuckLake()` exposes typed, catalog-scoped wrappers around DuckLake's technical maintenance functions:

```csharp
var lake = context.Database.DuckLake();
var snapshots = await lake.GetSnapshotsAsync(cancellationToken);

// Destructive lifecycle operations default to discovery-only dry runs.
var expiryCandidates = await lake.ExpireSnapshotsAsync(cutoff, cancellationToken: cancellationToken);
var files = await lake.CleanupOldFilesAsync(cutoff, cancellationToken: cancellationToken);

await lake.FlushInlinedDataAsync(
    new DuckLakeFlushOptions { SchemaName = "main", TableName = "events" },
    cancellationToken);
await lake.MergeAdjacentFilesAsync(
    new DuckLakeMergeOptions { TableName = "events", MaximumCompactedFiles = 4 },
    cancellationToken);
```

To make a write snapshot self-describing, set its metadata inside the same explicit transaction as the writes:

```csharp
await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
await context.SaveChangesAsync(cancellationToken);
await context.Database.DuckLake().SetCommitMessageAsync(
    author,
    commitMessage,
    extraInfo,
    cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

The provider requires an active writable transaction and passes the caller-supplied strings to DuckLake. It does
not derive authors, messages, or application identifiers from tracked entities or ambient state.

Snapshot identifiers remain 64-bit values and `rows_flushed` remains a `BigInteger`, matching DuckLake and
DuckDB.NET rather than narrowing values. Timestamps and all function arguments are parameterized. The facade
does not choose retention cutoffs, schedule jobs, authorize callers, or add distributed locks; the application
passes those decisions in explicitly. Mutation is rejected for a read-only profile.

## Additional catalogs

Attach an existing local sharing/reference catalog read-only on the same connection:

```csharp
options.UseDuckLake(
    "analytics.ducklake",
    lake => lake
        .CatalogName("analytics")
        .AlsoAttach("reference", "reference.ducklake"));
```

For remote metadata, create another `TYPE ducklake` secret in the same connection initializer and store only its
name in the additional profile:

```csharp
options.UseDuckLake(
    lake => lake
        .UseNamedSecret("analytics_profile")
        .CatalogName("analytics")
        .AlsoAttachNamedSecret("reference", "reference_profile"),
    duckDB => duckDB.ConfigureConnection(CreateCatalogSecrets));
```

The primary catalog remains selected for EF LINQ and tracked entities. Additional catalogs are available to
catalog-qualified dynamic/raw SQL such as `reference.main.ports`. Mapping an entity to a non-primary catalog and
cross-catalog LINQ translation are intentionally not implied by `AlsoAttach`; that requires a separate model-level
contract. Additional attachments default to `READ_ONLY`, use safely delimited aliases, reject duplicates, and are
recreated on provider-owned read-only connections. A caller-owned connection may reuse an existing local alias only
when its metadata path and read-only/writable mode exactly match the configured attachment. Named-secret attachment
identity cannot be recovered from `duckdb_databases()`, so an existing alias is rejected and a fresh connection must
let the provider attach it. Initialization fails before catalog selection when these checks do not pass.

Provider contributors can run `scripts/test-ducklake-external.sh` to exercise the named-secret profile against
PostgreSQL metadata and MinIO S3-compatible storage. The same isolated integration lane runs on Linux in CI.
The functional suite also migrates an official upstream DuckLake v0.3 catalog fixture to verify the real persisted
`AUTOMATIC_MIGRATION` path.

See the runnable [`samples/DuckLake`](../samples/DuckLake) application for local `EnsureCreated`, tracked writes,
bulk insert, `MERGE` upsert, and LINQ analytics.
