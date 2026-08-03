# Native DuckDB concurrency with EF Core

This guide describes a supported native-file deployment with many concurrent readers and one continuous bulk
writer. It applies to a single application process using `UseDuckDB("Data Source=analytics.duckdb")`. It does not
describe DuckLake concurrency; see [DUCKLAKE.md](DUCKLAKE.md#concurrency-and-read-scaling) for that backend.

## Supported topology

DuckDB is embedded in the application process. One process can open multiple connections to the same database
instance and can read and write concurrently. Keep the writable native file owned by that one process.

```text
HTTP readers                              ingestion producers
    |                                             |
    | one DbContext per operation                 v
    +-------------------------> analytics.duckdb <- durable/replayable source
                                      ^                 |
                                      |                 v
                                      +-------- one background writer
                                                one DbContext per batch
```

This topology is a good fit when:

- reads are analytical or reporting-oriented;
- ingestion is append-heavy and can be committed in batches;
- one application process owns the writable file; and
- the application can bound expensive concurrent queries when the host becomes saturated.

It is not a multi-instance architecture. Do not let multiple web replicas, worker processes, command-line tools,
or scheduled jobs open the same native file for writing. Use DuckLake with a concurrency-capable metadata catalog
or a client-server database when independent processes must write concurrently.

See DuckDB's [concurrency documentation](https://duckdb.org/docs/stable/connect/concurrency) for the engine's
single-process and file-locking model.

## Choose a context lifetime by unit of work

A `DbContext` is not thread-safe. Never share one instance between concurrent operations. The number of users is
not the number of contexts to retain: create a context for a database unit of work and dispose it when that work
finishes.

`AddDbContext` is sufficient when every HTTP request performs one sequential unit of work. Use
`AddDbContextFactory` when the application also has a background writer, creates multiple independent units of
work inside one dependency-injection scope, or explicitly runs independent operations concurrently. The factory
does not add database concurrency; it provides safe ownership of separate context instances.

For the mixed reader/writer topology, one factory keeps the lifetime rule explicit:

```csharp
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddDbContextFactory<AnalyticsContext>(options =>
    options.UseDuckDB(
        "Data Source=analytics.duckdb",
        duckdb => duckdb
            .MemoryLimit("4GB")
            .Threads(4)));
```

Use the same connection string and compatible global settings for every context that opens this file.

> **Reader does not mean `Access Mode=READ_ONLY` in this topology.** Reader contexts are normal connections that
> application code uses only for queries. Engine-level `Access Mode=READ_ONLY` is for opening an existing file when
> no process has it open for writing, including multi-process read-only deployments. Do not mix that connection
> profile with the live read-write instance.

Microsoft's [DbContext lifetime guidance](https://learn.microsoft.com/ef/core/dbcontext-configuration/) describes
the unit-of-work lifetime and thread-safety requirements.

## Reader pattern

Create and dispose one context inside each request or independent query operation. Use `AsNoTracking()` when the
result will not be modified through that context, and project only the columns the response needs.

```csharp
public sealed class ShipmentQueries(
    IDbContextFactory<AnalyticsContext> contextFactory)
{
    public async Task<IReadOnlyList<ShipmentSummary>> GetRecentAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        await using var context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Shipments
            .AsNoTracking()
            .Where(shipment => shipment.CustomerId == customerId)
            .OrderByDescending(shipment => shipment.CreatedAt)
            .Select(shipment => new ShipmentSummary(
                shipment.Id,
                shipment.Reference,
                shipment.CreatedAt))
            .Take(100)
            .ToListAsync(cancellationToken);
    }
}
```

Fifty active users do not necessarily produce fifty simultaneous database queries. If fifty expensive queries do
arrive together, fifty contexts may submit work, but they still share the same DuckDB CPU, memory, and I/O
resources. Bound expensive read operations in the consuming application when load tests show that admitting more
work increases tail latency. The provider intentionally does not own request scheduling or admission control.

## Continuous writer pattern

Use one background consumer as the write owner. Producers place rows or messages in a durable queue, broker, or
other replayable source; the consumer reads them in bounded batches. Create a new context and transaction for each
batch rather than keeping one context, change tracker, or transaction alive indefinitely.

The source and batching policy belong to the consuming application. Its batch-read operation should wait until
either the maximum batch size is reached or a maximum delay has elapsed after the first item arrives. It must not
permanently acknowledge or checkpoint a batch until the DuckDB transaction has committed.

The following application-owned contract makes those requirements explicit. A broker delivery, database-backed
queue, or replayable source can implement it:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DuckDB.EFCoreProvider.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Register an application-specific durable IMeasurementBatchSource first.
builder.Services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior =
        BackgroundServiceExceptionBehavior.StopHost);
builder.Services.AddHostedService<MeasurementWriter>();

public sealed record MeasurementBatch(
    string DeliveryToken,
    IReadOnlyList<Measurement> Items);

public interface IMeasurementBatchSource
{
    // Returns after maximumSize items have arrived or maximumWait has elapsed
    // after the first item. A null result means that no item became available.
    ValueTask<MeasurementBatch?> ReadAsync(
        int maximumSize,
        TimeSpan maximumWait,
        CancellationToken cancellationToken);

    // Permanently acknowledges the delivery. Call only after the database commit.
    ValueTask AcknowledgeAsync(
        MeasurementBatch batch,
        CancellationToken cancellationToken);

    // Makes an uncommitted or unacknowledged delivery available for redelivery.
    ValueTask ReleaseAsync(
        MeasurementBatch batch,
        CancellationToken cancellationToken);
}

public sealed class MeasurementWriter(
    IMeasurementBatchSource source,
    IDbContextFactory<AnalyticsContext> contextFactory,
    ILogger<MeasurementWriter> logger)
    : BackgroundService
{
    private const int MaximumBatchSize = 10_000;
    private static readonly TimeSpan MaximumBatchWait = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var batch = await source.ReadAsync(
                MaximumBatchSize,
                MaximumBatchWait,
                stoppingToken);

            if (batch is null)
            {
                continue;
            }

            try
            {
                await using var context =
                    await contextFactory.CreateDbContextAsync(stoppingToken);
                await using var transaction =
                    await context.Database.BeginTransactionAsync(stoppingToken);

                var inserted = await context.BulkInsertAsync(batch.Items, stoppingToken);
                await transaction.CommitAsync(stoppingToken);

                // A cancellation or failure here intentionally leaves the batch
                // available for redelivery. See the idempotency note below.
                await source.AcknowledgeAsync(batch, stoppingToken);
                logger.LogDebug("Committed {RowCount} measurements", inserted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await TryReleaseAsync(batch);
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Measurement batch {DeliveryToken} failed; the host will stop",
                    batch.DeliveryToken);

                await TryReleaseAsync(batch);
                throw;
            }
        }
    }

    private async Task TryReleaseAsync(MeasurementBatch batch)
    {
        try
        {
            using var releaseCts = new CancellationTokenSource(ReleaseTimeout);
            await source.ReleaseAsync(batch, releaseCts.Token);
        }
        catch (Exception releaseException)
        {
            // Preserve the original ingestion failure. A durable source should
            // also make an expired delivery lease available for redelivery.
            logger.LogWarning(
                releaseException,
                "Could not release measurement batch {DeliveryToken}",
                batch.DeliveryToken);
        }
    }
}
```

This sample chooses fail-fast behaviour: an unhandled `BackgroundService` exception
[stops the entire .NET host by default](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/6.0/hosting-exception-handling).
The batch is released first so that a durable source can redeliver it after the host restarts. An application that
chooses in-process retry or dead-letter handling should implement that policy in the source or a separate
ingestion service and should bound retries and backoff. Host shutdown is handled separately: the active transaction
is disposed and rolled back, the delivery is released with a short independent timeout, and cancellation continues.

There is an unavoidable acknowledgement window when DuckDB and the source cannot share one transaction: the
database commit can succeed and the acknowledgement can fail. Therefore, use a stable source event identifier and
make ingestion idempotent. For example, record source event identifiers in an application-owned inbox table in the
same DuckDB transaction, or bulk-load into a staging table and merge into the destination with duplicate handling.
A direct appender insert into a uniquely constrained destination detects a duplicate but does not by itself turn
redelivery into a successful no-op.

An in-memory `Channel<T>` is not a durable ingestion source: accepted items disappear on process failure, and an
item removed before commit is lost unless the producer can replay it. Use one only when the input is disposable or
the upstream producer retains ownership until the database commit is confirmed. With
`BoundedChannelFullMode.Wait`, producers must use `WriteAsync` to receive
[backpressure](https://learn.microsoft.com/dotnet/core/extensions/channels#full-mode-behavior). `TryWrite` returns
`false` when the channel is full, and the producer must handle that result.

`BulkInsertAsync` is the provider's appender-backed raw fast path. It bypasses change tracking, optimistic
concurrency checks, EF command interceptors, and store-generated values. The target table must exist and every
mapped column must have a supplied value. Use `SaveChangesAsync` instead when a batch needs the full EF update
pipeline.

Choose source capacity, maximum batch size, and maximum batch wait from measured latency, memory, and throughput
requirements. A maximum wait lets nearby low-volume arrivals coalesce without leaving the oldest item waiting
indefinitely; isolated items can still form a one-row batch. Do not loop over individual rows and call
`SaveChangesAsync` for high-volume ingestion.

## Read/write visibility and conflicts

DuckDB uses MVCC and optimistic concurrency control within the process:

- concurrent readers operate on consistent transaction snapshots;
- a new reader transaction can observe writer batches after they commit;
- appends from the single designated writer do not conflict with readers;
- updates or deletes from another context can conflict with overlapping writes;
- stale application state can still cause last-writer-wins updates unless the model uses an EF concurrency token;
  and
- migrations, table rebuilds, and other schema maintenance must run outside normal reader/writer traffic.

The provider does not add a retrying execution strategy. If the application permits additional writers, it must
identify retryable conflict failures and retry the complete transaction only when that is safe for the business
operation. A single application writer is simpler for continuous ingestion.

## How `.Threads(...)` relates to contexts

`.Threads(4)` sets DuckDB's global `threads` setting when a connection opens. It controls the total execution
threads available to the shared DuckDB database instance; it is not a context count, connection-pool size, user
limit, or per-query reservation.

```text
50 reader contexts + 1 writer context
                 |
                 v
       one DuckDB database instance
                 |
                 v
       up to 4 total execution threads
```

Consequences:

- fifty contexts configured with `.Threads(4)` do not create 200 DuckDB execution threads;
- parallelizable reads compete for the shared execution budget and may use fewer than four threads;
- `.Threads(4)` does not create four concurrent writers or split one `BulkInsertAsync` call into four writer
  transactions;
- the setting controls parallel query execution, including parallelizable query work inside SQL-based ingestion
  such as `INSERT ... SELECT`, but an appender-backed `BulkInsertAsync` call remains one application write
  operation;
- `EnableBulkInsertBatching()` changes the SQL shape produced by `SaveChanges`; it does not change the number of
  contexts or allocate a separate set of threads; and
- because `threads` is global, a later connection configured with a different value changes the setting for the
  shared instance. Configure every context consistently.

Omit `.Threads(...)` to retain DuckDB's CPU-based default. Set it when the application must cap DuckDB on a shared
host or when a representative load test demonstrates a better value. Start from the CPU and memory actually
allocated to the process, then test the read workload while ingestion is active. DuckDB recommends substantial
memory per execution thread for analytical workloads; increasing the thread count without sufficient memory can
reduce performance or cause spilling.

Do not use the thread count as admission control. If only a bounded number of expensive queries should enter
DuckDB at once, implement that policy in the consuming application and measure queue time separately from database
execution time.

See DuckDB's [workload-tuning guide](https://duckdb.org/docs/stable/guides/performance/how_to_tune_workloads)
for row-group parallelism, memory, remote I/O, and connection considerations.

## Production checklist

- [ ] One process owns the writable native DuckDB file.
- [ ] Every concurrent operation owns a separate, short-lived `DbContext`.
- [ ] Reader contexts use `AsNoTracking()` where change tracking is unnecessary.
- [ ] One background consumer owns continuous writes.
- [ ] Bulk writes use bounded batches and bounded transactions.
- [ ] The ingestion source is durable or replayable; acknowledgement occurs only after the DuckDB commit.
- [ ] Redelivery is idempotent across the database-commit/source-acknowledgement window.
- [ ] Batch formation has both a maximum size and a maximum wait.
- [ ] Retry, dead-letter, or fail-fast host behaviour is explicit and monitored.
- [ ] All contexts use the same connection string and compatible global settings.
- [ ] `.Threads(...)` and `MemoryLimit(...)` are tested together under concurrent reads and active ingestion.
- [ ] Heavy read admission is bounded when load testing shows resource saturation.
- [ ] Migrations and schema maintenance run with application traffic stopped.
- [ ] The database is stored on reliable local or directly attached storage, not a shared network filesystem.
- [ ] Metrics separate queue time, query latency, write-batch latency, rows per second, memory, spilling, and disk
      utilisation.

Escalate to DuckLake with PostgreSQL metadata or a server database when the system needs multiple application
replicas, independent writer processes, high availability, or sustained high-concurrency transactional behaviour.
