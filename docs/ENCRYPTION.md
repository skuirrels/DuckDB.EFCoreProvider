# Encryption at rest

The provider can store a context's data in an encrypted DuckDB database file. DuckDB encrypts the database and
its write-ahead log with AES-256-GCM; the provider adds the EF Core wiring, key handling, and the operational
defaults that keep plaintext from reaching disk through other paths.

```csharp
services.AddDbContext<AppContext>(options => options
    .UseEncryptedDuckDB(
        "/var/lib/app/secure.duckdb",
        () => keyVault.GetSecret("app-db-key")));
```

Everything else is ordinary EF Core: LINQ, `SaveChanges`, `EnsureCreated`, migrations, bulk insert, and upsert
behave exactly as they do against a plain file database.

## How it works

DuckDB accepts an encryption key only as an `ATTACH` parameter. There is no connection-string setting, no secret
type, and no prepared-statement form for it, so an encrypted file cannot be opened as a connection's data source:

```
Catalog Error: Cannot open encrypted database "/var/lib/app/secure.duckdb" without a key
```

The provider therefore opens a shared in-memory DuckDB host database, attaches the encrypted file to it, and
selects it as the connection's default catalog:

```sql
SET temp_file_encryption = true;
ATTACH '/var/lib/app/secure.duckdb' AS "secure" (ENCRYPTION_KEY '…');
USE "secure";
```

Because the encrypted database is the default catalog, entity tables, `__EFMigrationsHistory`, and
`__EFMigrationsLock` are all created inside the encrypted file. This mirrors the [DuckLake](DUCKLAKE.md) profile's
attached-catalog model.

Consequences worth knowing:

- **The connection string's data source must be in-memory.** The provider normalizes an unset or `:memory:` data
  source to `:memory:?cache=shared`, so every connection a context opens reaches the same attachment. A file data
  source is rejected at configuration time, because it would create a second, unencrypted database.
- **The host instance is reference-counted.** It exists while at least one connection is open and is released when
  the last one closes, so the key provider is called again the next time the context opens a connection.
- **Attachment is idempotent and safe to race.** Contexts that open concurrently all reach the same attachment;
  the provider confirms that the attached database is the configured file — resolving symbolic links in every
  path segment, including a symlinked database file — rather than one another connection attached first under
  the same alias, and that the joining context's key matches the one the attachment was made with.
- **Contexts in one process share the host instance** while their connections overlap. Each encrypted database
  needs its own catalog alias in that case; see [Several encrypted databases](#several-encrypted-databases).

## Configuration

Two equivalent entry points:

```csharp
// Top-level: the provider supplies the in-memory host connection string.
options.UseEncryptedDuckDB(path, keyProvider);

// Inside an existing UseDuckDB call, alongside other provider options.
options.UseDuckDB(
    "Data Source=:memory:?cache=shared",
    duckdb => duckdb
        .UseEncryptedDatabase(path, keyProvider)
        .EnableBulkInsertBatching()
        .MemoryLimit("4GB"));
```

The optional configuration action covers the attachment itself:

| Option | What it does | Default |
|---|---|---|
| `.CatalogName("vault")` | Alias the encrypted database is attached under and used as the default catalog. | derived from the file name (`secure.duckdb` → `secure`) |
| `.ReadOnly()` | Attach read-only. Requires an existing file: DuckDB will not create one. | writable |
| `.EncryptTemporaryFiles(false)` | Stop enabling DuckDB's `temp_file_encryption`. | enabled |

```csharp
options.UseEncryptedDuckDB(
    "/var/lib/app/secure.duckdb",
    () => keyVault.GetSecret("app-db-key"),
    encrypted => encrypted.CatalogName("vault").ReadOnly());
```

## Key handling

The key is supplied as a `Func<string>` that the provider invokes when it attaches the database — and again when
it finds the database already attached, to verify this context's key before reusing the shared attachment — so it
can come from a key vault, KMS, or an environment variable resolved at connect time rather than sitting in
configuration. The options object stores the callback, never the key.

- The `ATTACH` statement is executed directly on the DuckDB connection, outside EF Core's command pipeline, so it
  is not visible to EF logging, `DiagnosticSource`, interceptors, or command-plan capture.
- Because DuckDB quotes the failing statement in some error messages, an attachment failure whose message contains
  the key is replaced with a redacted `InvalidOperationException` before it is logged or thrown.
- A provider that returns an empty string fails the attachment with a clear error instead of opening the database
  unencrypted.
- Contexts sharing a live attachment do not get a free pass: DuckDB cannot re-check a key against an attached
  database, so the provider records a SHA-256 fingerprint of the attaching key (per canonical file path, in
  process memory only) and proves each joining context's key against it. A context whose key is wrong or was
  rotated away fails with an explicit error instead of inheriting another context's access. An attachment made
  outside the provider — by your own `ATTACH` SQL — has no fingerprint and is trusted as caller-managed.
- Do not enable DuckDB's `log_query_path` on these connections: it records statement text, including the key.

A wrong key fails on attach:

```
Invalid Input Error: Wrong encryption key used to open the database file
```

## What is and is not encrypted

Encrypted:

- The database file, including table data, indexes, and catalog metadata such as table and column names.
- The write-ahead log (`secure.duckdb.wal`).
- Temporary files DuckDB spills to disk, because the provider enables `temp_file_encryption`. This matters:
  DuckDB leaves that setting off by default, and a spilling aggregate or sort over an encrypted database writes
  row data to the temporary directory in the clear. The setting is global to the DuckDB instance, so it also
  applies to other contexts sharing the host instance.

Not encrypted:

- Parquet, CSV, and JSON files written by `ExportToParquet`, `COPY TO`, or [tiered storage](TIERED-STORAGE.md).
  Cold tiers need their own protection — an encrypted volume, or S3/Azure server-side encryption.
- [DuckLake](DUCKLAKE.md) catalogs, whose data lives outside the DuckDB file. `UseDuckLake` and
  `UseEncryptedDatabase` cannot be combined; encrypt DuckLake storage through the metadata backend and object
  store instead.
- Quack profiles, where database files are owned by the server. Configure encryption at rest there.
- Process memory, backups you make with other tools, and anything the application itself writes elsewhere.

Encryption protects data at rest. It does not defend against an attacker who can read the process's memory or
reach the key store.

## Working with the file outside the provider

Any DuckDB client can read the file with the same key:

```sql
ATTACH '/var/lib/app/secure.duckdb' AS secure (ENCRYPTION_KEY 'your key');
USE secure;
```

Verify a database really is encrypted:

```sql
SELECT database_name, encrypted, cipher FROM duckdb_databases();
-- secure | true | GCM
```

## Encrypting an existing database

DuckDB cannot encrypt a file in place. Copy it into a new encrypted database:

```sql
ATTACH 'app.duckdb' AS plain (READ_ONLY);
ATTACH 'secure.duckdb' AS secure (ENCRYPTION_KEY 'your key');
COPY FROM DATABASE plain TO secure;
```

Then point the context at `secure.duckdb` with `UseEncryptedDuckDB`, and securely delete the plaintext original
and any `.wal` file beside it.

## Rotating the key

The same copy moves a database to a new key:

```sql
ATTACH 'secure.duckdb' AS current (ENCRYPTION_KEY 'old key', READ_ONLY);
ATTACH 'secure-rotated.duckdb' AS rotated (ENCRYPTION_KEY 'new key');
COPY FROM DATABASE current TO rotated;
```

Run it with the application stopped, swap the files, then update the key provider's secret. Because the provider
resolves the key on each attachment rather than caching it, a restart is enough to pick up the new one.

## Operations

- **Backups** are ordinary file copies: the copy stays encrypted and still requires the key. Copy the `.wal` file
  alongside the database, or checkpoint first so there is nothing left to copy.
- **`EnsureCreated`** creates the file on first attachment. Existence is reported from the presence of the file, so
  probing never creates the database it is asking about.
- **`EnsureDeleted`** detaches the database from the host instance before deleting the file and any leftover
  `.wal`, so nothing keeps the file open.
- **Migrations** run normally. The history and lock tables live inside the encrypted file.
- **Caller-opened connections** are supported: the provider attaches the database even when the application opens
  the underlying `DbConnection` itself before EF does, and the caller's `ConfigureConnection` callback still runs
  exactly once for that connection.
- **Diagnostics**: attachments are logged as `DuckDBEventId.EncryptedDatabaseAttachmentStarting`, `…Completed`,
  and `…Failed`, with the catalog alias as the target. The key is not part of the event data.

## Limitations

- A caller-supplied `DbConnection` cannot be combined with `UseEncryptedDatabase`: the provider must own the
  connection to attach the database with its key.
- The access mode belongs to the attachment, not the connection. If a writable and a read-only context share a
  host instance and a catalog alias, the second one fails with an explicit error rather than silently taking the
  first one's mode. For the same reason `CreateReadOnlyConnection()` is refused for an encrypted database: a
  clone on the shared host instance could neither re-attach a live writable database read-only nor constrain the
  writer. Configure a separate context with `ReadOnly()` instead.
- DuckDB accepts a key string of any length and derives the AES key from it directly. It exposes no salt, work
  factor, or other password-based derivation parameters, so supply a high-entropy secret from a key manager
  rather than a password chosen by a person.
- DuckDB added database encryption in 1.4; this provider ships against DuckDB 1.5.5.

## Several encrypted databases

Contexts whose connections overlap share one DuckDB host instance, so each encrypted database needs a distinct
catalog alias. The default alias comes from the file name, which is usually enough; give explicit names when two
databases share one:

```csharp
options.UseEncryptedDuckDB(tenantPath, keyProvider, encrypted => encrypted.CatalogName($"tenant_{tenantId}"));
```

Reusing an alias for a different file is rejected when the attachment is attempted:

```
Catalog alias 'tenant' is already attached to a different database file.
```
