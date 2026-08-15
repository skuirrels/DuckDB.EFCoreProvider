# Experimental Quack profile

The provider can opt a `DbContext` into DuckDB's experimental Quack client/server protocol. Existing `UseDuckDB`
and `UseDuckLake` contexts do not load Quack, create a transport adapter, or enter a remote branch.

Quack remains experimental in DuckDB 1.5.x. Its protocol and extension surface may change before DuckDB 2.0, so
deploy this profile behind an application feature flag and pin the DuckDB and Quack versions together.

## Start a provider-managed server

The server is anchored to the same in-process connection used by the context. Keep the returned handle alive for
as long as the listener should run:

```csharp
await using var server = await serverContext.Database.StartQuackServerAsync(
    new DuckDBQuackServerOptions
    {
        Uri = "quack:localhost:9494",
        Token = configuration["Quack:Token"]
    });
```

Loopback is the safe default. A non-loopback hostname requires `AllowOtherHostname = true`; terminate TLS and apply
authentication and authorization policy at the server or its reverse proxy. Do not log the returned
`AuthenticationToken`.

`ExtensionLoadMode = LoadOnly` avoids runtime downloads. `ExtensionPath` can pin an explicit extension binary for
offline deployments. A locally built unsigned extension also requires `allow_unsigned_extensions=true` on the
test connection; do not enable unsigned extensions in production. When building the current PR against DuckDB 1.5.5,
load `httpfs` on the server connection before Quack so DuckDB provides its writable crypto module.

## Configure a remote DbContext

```csharp
services.AddDbContext<AnalyticsContext>(options =>
    options.UseQuack(
        "quack:analytics.internal:9494",
        configuration["Quack:Token"]!,
        quack => quack.EnableHttpConnectionCaching()));
```

For a plain-HTTP loopback development server, opt in with `quack.DisableSsl()`. The token is held in the EF options
needed to create the connection, but it is excluded from EF log fragments, debug information, and service-provider
cache keys.

The profile executes each generated EF command as one remote command through Quack's stateful
`quack_query_by_name` function. This is important for updates, `RETURNING`, and transaction correctness; merely mapping
EF tables to the attached catalog does not provide those semantics in Quack 1.5.x. A provider transaction sends
`BEGIN`, `COMMIT`, and `ROLLBACK` to the same remote session.

Quack exposes one result stream for each submitted command. SaveChanges therefore folds compatible inserts, updates,
or deletes into one set-based statement and splits incompatible command shapes before execution so generated values
and concurrency results remain aligned with their EF entries.

Supported and live-tested in the opt-in integration gate:

- LINQ queries and projections;
- tracked inserts, updates, deletes, and affected-row concurrency results;
- remote schema provisioning through `EnsureCreated` (database deletion remains server-owned);
- physical foreign-key enforcement and fresh-client import when using duckdb-quack PR #248 (`f5c04bb`) or later;
- sequence-generated keys, literal/SQL/UUID defaults, computed columns, and generated-value propagation;
- optional SaveChanges insert/update/delete batching;
- relationships, `Include`, split queries, raw/interpolated SQL, and optimistic concurrency;
- set-based `ExecuteUpdate` and `ExecuteDelete`;
- explicit transactions and rollback;
- provider command-plan replay with `ReplayQuackCommandAsync`;
- typed remote `BulkInsert`;
- server-side `Upsert`, including insert-or-update classification on the server;
- identity, server list, active-session, latency, and protocol-log diagnostics.

## Bulk insert and upsert

`BulkInsert` preserves the appender fast path. The client appends into a local temporary table, then Quack sends the
typed chunks to the remote target in one set-based insert.

`Upsert` does not require the client to know whether a row is new. It renders provider-converted values into a uniquely
named server staging table and executes `INSERT ... ON CONFLICT` on the server. The server therefore owns conflict
detection and the insert-versus-update decision. Staging tables are created only when the input has rows and are dropped
after the operation; the primary upsert error is preserved if cleanup also fails.

## Command-plan replay

```csharp
var plan = sourceContext.Database.GetDuckDBCommandPlan(
    sourceContext.Events.Where(value => value.Timestamp >= cutoff));

await using var result = await remoteContext.Database.ReplayQuackCommandAsync(plan);
await foreach (var row in result.ReadRowsAsync())
{
    // Runtime-shaped, remotely executed row.
}
```

Quack's current wire protocol has no independent bind-parameter message. The provider renders captured parameter
values as DuckDB literals after EF value conversion and replaces parameter tokens only outside quoted text and SQL
comments. Unsupported CLR parameter types fail closed instead of falling back to string conversion.

## Health and diagnostics

```csharp
var snapshot = await context.Database.GetQuackDiagnosticsAsync();
```

The snapshot includes `whoami()`, server and active-connection rows, measured round-trip latency, and recent Quack
protocol log entries when logging is enabled. Diagnostic rows are represented as name/value maps so beta extension
columns can evolve without silently mis-mapping a positional record. Provider lifecycle events are exposed through
`DuckDBEventId.QuackServer*` and `DuckDBEventId.QuackDiagnostics*`.

## Current boundaries

- Remote `EnsureCreated` provisions an empty server database through the stateful Quack session. `EnsureDeleted` and
  EF migrations remain disabled because database lifetime and migration coordination are server responsibilities.
- Quack 1.5.x binds function and sequence defaults before the incoming catalog alias exists. The provider works around
  this with a disposable empty catalog and matching sequence names, then atomically replaces it with the remote catalog.
  The bootstrap reads sequence names from `duckdb_sequences()`; custom authorization policies must permit that metadata
  query.
- Fresh-client import of a catalog containing physical foreign keys requires
  [duckdb-quack PR #248](https://github.com/duckdb/duckdb-quack/pull/248) (`f5c04bb`) or a later build until that fix is
  released. The provider integration gate verifies both fresh attachment and continued server-side FK enforcement
  against that commit. With an older stock extension, expose tables without physical foreign keys; EF relationships,
  navigations, fix-up, `Include`, and split queries remain supported.
- `ConfigureConnection(Action<DuckDBConnection>)` cannot be used by a `UseQuack` client. Configure secrets and
  server-side extensions on the server.
- A caller-supplied `DbConnection` cannot be combined with `UseQuack`; the provider must own the in-memory transport
  connection so remote execution cannot silently fall back to a local database.
- Provider-managed tiered-storage models are not supported by `UseQuack`; provision and query equivalent remote
  objects directly on the server instead.
- A Quack profile cannot manufacture a separately enforced read-only connection. Apply a server authorization
  function and use a separately configured client context for read-only access.
- Quack is not replication, CDC, failover, or replica routing. It centralizes requests to one DuckDB server.

These restrictions are capability-driven and affect only `UseQuack` contexts.
