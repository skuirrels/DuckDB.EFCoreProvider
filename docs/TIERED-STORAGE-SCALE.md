# Tiered-storage catalogue scale acceptance

The provider includes a neutral BenchmarkDotNet fixture for measuring catalogue and generated-view behavior before
setting production thresholds. It does not encode application concepts such as owners, tenants, holds, or retention
policy.

Run the fixture with representative declared-partition cardinality and monthly history:

```bash
dotnet run -c Release \
  --project test/DuckDB.EFCoreProvider.Benchmarks \
  --filter '*TieredCatalogueScaleBenchmarks*'
```

`TieredCatalogueScaleBenchmarks` exposes these independent technical dimensions:

- leading exact-partition cardinality;
- lifecycle periods;
- primary graph node count from one through four (root, child, grandchild, leaf);
- exact file fan-out per leading-partition/lifecycle/node combination;
- exact retained-scope cardinality and one- or two-partition prefix width; and
- an optional shared-descendant preset with two root bindings to one descendant table.

The defaults are a deliberately small `16 partitions x 12 periods x 2 nodes x 1 file shard` smoke fixture with no
retained scopes and no shared descendant. Configure representative values explicitly for acceptance; defaults are
not a capacity claim.

It reports exact catalogue file count and generated view SQL size, and measures:

- restart plus view regeneration;
- scoped query bind/plan time using `EXPLAIN`, plus first execution time;
- first global query bind/execute time;
- lifecycle plus leading-partition pruning; and
- retention-plan time and managed allocations against the exact active catalogue.

Override the dimensions with comma-separated positive integers so the acceptance run matches a proposed production
inventory:

```bash
DUCKDB_TIER_SCALE_PARTITIONS=500 \
DUCKDB_TIER_SCALE_PERIODS=84 \
DUCKDB_TIER_SCALE_NODES=4 \
DUCKDB_TIER_SCALE_FILE_FANOUT=3 \
DUCKDB_TIER_SCALE_RETAINED_SCOPES=250 \
DUCKDB_TIER_SCALE_SCOPE_PREFIX_WIDTH=2 \
dotnet run -c Release \
  --project test/DuckDB.EFCoreProvider.Benchmarks \
  --filter '*TieredCatalogueScaleBenchmarks*'
```

BenchmarkDotNet reports elapsed time and managed memory. Setup also runs DuckDB `EXPLAIN ANALYZE` and reports the
scoped query's aggregate `Total Files Read` evidence; no threshold is guessed before measuring the target inventory.
The scope prefix width is limited to the two declared neutral partitions (`ScopeKey`, `FanOutKey`). Scope cardinality
must not exceed the distinct prefixes available at the selected width. Multiple comma-separated values are allowed
for every integer dimension, but BenchmarkDotNet evaluates their Cartesian product, so controlled acceptance runs
normally supply one representative value per dimension.

Run the shared-descendant preset separately; it fixes the primary graph at two nodes and adds a second root binding:

```bash
DUCKDB_TIER_SCALE_NODES=2 \
DUCKDB_TIER_SCALE_SHARED_DESCENDANT=true \
dotnet run -c Release \
  --project test/DuckDB.EFCoreProvider.Benchmarks \
  --filter '*TieredCatalogueScaleBenchmarks*'
```

Capture the global-query plan alongside the run. Use the consuming application's expected partition counts, periods,
nodes, exact files, shared-binding shape, and retained technical scopes for production acceptance; the fixture does
not assign owner, tenant, hold, or approval meaning to any dimension.

## Remote exact-catalogue acceptance

The same fixture has an environment-configurable S3 mode. A remote archive makes the provider regenerate views from
the exact recorded file catalogue rather than local globs, so SQL size, bind/plan time, restart time, pruning, global
queries, catalogue memory, and retention planning all exercise the production remote path.

The disposable MinIO lane also routes every S3 request through a counting proxy and reports aggregate LIST, HEAD, and
non-LIST GET requests for the complete BenchmarkDotNet run:

```bash
DUCKDB_TIER_SCALE_PARTITIONS=500 \
DUCKDB_TIER_SCALE_PERIODS=84 \
./scripts/bench-tiered-storage-s3.sh
```

The script defaults to the small smoke fixture, creates a unique remote prefix, and destroys the MinIO volume
afterwards. Override all applicable technical dimensions with the proposed inventory. The fixture reports the exact
resulting file and node counts rather than inferring them from the requested inputs.

To target another disposable S3-compatible endpoint or real S3 directly, set:

```bash
DUCKDB_TIER_SCALE_S3_BUCKET=disposable-bucket
DUCKDB_TIER_SCALE_S3_PREFIX=provider-scale-run
DUCKDB_TIER_SCALE_S3_REGION=eu-west-1
DUCKDB_TIER_SCALE_S3_ENDPOINT=127.0.0.1:9000 # omit for AWS
DUCKDB_TIER_SCALE_S3_KEY=access-key             # omit both key and secret for credential_chain
DUCKDB_TIER_SCALE_S3_SECRET=secret-key
DUCKDB_TIER_SCALE_S3_SSL=false                  # defaults to true when endpoint is omitted
```

For a non-proxied endpoint, collect LIST/HEAD/GET counts from the endpoint or cloud telemetry for the same invocation.
Request counts vary by DuckDB/httpfs and object-store version, so the fixture records observations without embedding
an invented pass/fail threshold. Optimise only after the representative inventory identifies a concrete limit.
