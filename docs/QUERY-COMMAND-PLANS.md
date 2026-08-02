# Query command plans for compilers and tooling

DuckDB.EFCoreProvider can translate a composed EF Core query into one immutable DuckDB command
without opening the context connection or executing the query. This is intended for query
workbenches, explain tools, external language compilers, and other infrastructure that needs the
provider's exact SQL and parameter contract without depending on EF Core internals.

This is a **server-command snapshot**. It is not a DuckDB optimizer plan, a security sandbox, or a
representation of EF's client-side result shaper.

## Version support

| Provider version | Capability |
|---|---|
| 1.16.0 | `IQueryable<T>`, `Count`, and `Any` extraction; exact named-command replay; structured store-type inspection |
| 1.17.0 | Adds `LongCount`, `Min`, `Max`, `Sum`, and `Average` extraction |

Use 1.17.0 or newer when a tool needs the complete terminal-aggregate surface described here.

## Extract and replay a query

```csharp
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;

var cutoff = DateTime.UtcNow.AddDays(-30);
var query = context.Events
    .AsNoTracking()
    .Where(e => e.Timestamp >= cutoff)
    .OrderByDescending(e => e.Revenue)
    .Select(e => new { e.Country, e.Revenue });

DuckDBCommandPlan plan = context.Database.GetDuckDBCommandPlan(query);

Console.WriteLine(plan.CommandText);
foreach (var parameter in plan.Parameters)
{
    Console.WriteLine($"{parameter.Name}: {parameter.DbType} = {parameter.Value}");
}

await using var result = await context.Database.SqlQueryDynamicCommandAsync(
    plan,
    cancellationToken);

await foreach (var row in result.ReadRowsAsync(cancellationToken))
{
    // ReadOnlyMemory<object?> aligned with result.Columns by ordinal.
}
```

`DuckDBCommandPlan` owns the captured command text and parameter values. Mutable parameter values
are snapshotted, and replay creates new ADO.NET parameters rather than mutating caller-owned ones.
The query must belong to the same `DbContext` as the `DatabaseFacade` used for extraction.

Only single-command query shapes are accepted. Split queries and other multi-command shapes fail
explicitly instead of returning a partial plan.

## Terminal operations

Build predicates and selectors into the query first, then select the matching terminal extractor:

```csharp
var filtered = context.Events.Where(e => e.Active);

var count = context.Database.GetDuckDBCountCommandPlan(filtered);
var longCount = context.Database.GetDuckDBLongCountCommandPlan(filtered);
var any = context.Database.GetDuckDBAnyCommandPlan(filtered);

var amounts = filtered.Select(e => (decimal?)e.Revenue);
var min = context.Database.GetDuckDBMinCommandPlan(amounts);
var max = context.Database.GetDuckDBMaxCommandPlan(amounts);
var sum = context.Database.GetDuckDBSumCommandPlan(amounts);
var average = context.Database.GetDuckDBAverageCommandPlan(amounts);
```

`Sum` and `Average` require a projection to `int`, `long`, `float`, `double`, `decimal`, or a
nullable equivalent. Unsupported projections fail before EF query compilation.

The plan contains the database command only. Replaying `Min`, `Max`, or `Average` over an empty
input can therefore return DuckDB `NULL` where executing the corresponding non-nullable EF terminal
operator would apply client-side empty-sequence behavior. Execute the original LINQ operator when
the EF result-shaping contract is required.

## Replay an existing named command

Tools that already have exact DuckDB SQL and named ADO.NET parameters can use the same dynamic
result path without converting the SQL to composite-format placeholders:

```csharp
var minimum = new DuckDBParameter("$minimum", 10);

await using var result = await context.Database.SqlQueryDynamicCommandAsync(
    "SELECT * FROM events WHERE id >= $minimum",
    [minimum],
    cancellationToken);
```

The SQL is passed through unchanged, including literal `STRUCT` and `MAP` braces. Parameter names
may include or omit DuckDB's `$` prefix, and supplied `DbParameter` instances are copied.

## Inspect catalog store types

Dynamic model generators should ask the provider which CLR/EF contract a DuckDB store type has:

```csharp
var mapping = context.Database.GetDuckDBStoreTypeMapping("DECIMAL(12,2)");

Console.WriteLine(mapping.StoreType);   // canonical/faceted store type
Console.WriteLine(mapping.Support);     // ScalarProperty, ComplexProperty,
                                        // RawReaderOnly, or Unsupported
Console.WriteLine(mapping.ClrType);
Console.WriteLine(mapping.ElementType); // populated for supported collections
```

Do not infer that a type returned by DuckDB.NET can be used as an EF entity property. The four
support classifications intentionally separate those contracts. `STRUCT` uses EF complex-property
mapping; generating an appropriate dynamic complex CLR type remains the tool's responsibility.
`MAP`, `UNION`, `ENUM`, fixed-size arrays, `HUGEINT`/`UHUGEINT`, `VARINT`, `BIT`, and `INTERVAL`
remain outside the scalar EF-property surface and should be omitted or handled through a raw-reader
path.

See [TYPE-MAPPINGS.md](TYPE-MAPPINGS.md) for the complete mapping distinction.

## Consumer responsibilities

Translation does not authorize or execute a query. A consumer that accepts authored source or
replays generated SQL still owns:

- its source-language sandbox and allow-list;
- schema/model generation and identifier policy;
- authorization, tenant/catalog attachment, and credential isolation;
- validation that a returned command is an allowed read-only shape;
- execution time, memory, concurrency, and row limits;
- logging, audit, diagnostics, and result serialization.

LakeHold's optional C# LINQ Workbench planner is one consumer of these APIs. The provider remains
application-agnostic: it supplies translation, parameter, replay, and type-mapping mechanics while
LakeHold owns the compiler isolation and lakehouse policy.

## Deliberate boundaries

- Command plans are not serializable DTOs. A tool defines its own process or wire contract and
  serializes only the values it can replay safely.
- Extraction does not open the context connection, but EF still compiles the query against the
  configured model and provider services.
- `SqlQueryDynamicCommandAsync` streams result sets. DuckDB.NET currently reports
  `DbDataReader.RecordsAffected` as `-1`; use `ExecuteSqlRawAsync` when a known DML command needs an
  affected-row count.
- Query forms with no dedicated extractor, such as `First`, `Single`, `All`, or scalar `Contains`,
  are not implied by the existing terminal APIs. Compose an equivalent supported query where its
  semantics are correct, or execute the original EF operator.
