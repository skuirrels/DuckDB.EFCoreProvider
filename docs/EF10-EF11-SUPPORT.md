# Shared EF Core 10 and 11 development

The core and NTS packages build from one source tree. New provider functionality belongs in the shared
implementation and shared tests; use target-specific code only for EF API or behavior differences.

| Target | EF Core packages and dotnet-ef | Test framework |
| --- | --- | --- |
| net10.0 | 10.0.10 | xUnit v2 |
| net11.0 | 11.0.0-preview.7.26381.103 | xUnit v3, VSTest runner |

EF11 is preview support. Rebuild and revalidate against subsequent previews and the stable release.
The package families are separate:

| EF line | Core package | NTS package | Versions (core / NTS) |
| --- | --- | --- | --- |
| EF10 | `DuckDB.EFCoreProvider` | `DuckDB.EFCoreProvider.NTS` | `1.26.0` / `1.1.0` |
| EF11 | `DuckDB.EFCoreProvider.EF11` | `DuckDB.EFCoreProvider.EF11.NTS` | `1.26.0-preview.1` / `1.1.0-preview.1` |

Adding EF11 does not change the provider major version. Version 2 is reserved for the anticipated
DuckDB 2 transition. The EF10 package remains stable while the EF11 package depends on preview EF.

## Developing features

Install the .NET 11 SDK (`11.0.100-preview.7.26381.103` is the CI version) and the .NET 10 runtime.
The .NET 11 SDK builds both targets. `Directory.Build.props` defines the frameworks;
`Directory.Packages.props` selects aligned EF packages for each framework. No source copying or second
implementation branch is needed. Shared provider capabilities, typed expressions and immutable SQL plans
remain independent of the EF version.

```bash
scripts/restore-tools.sh
dotnet build DuckDB.EFCoreProvider.slnx
scripts/test-suite.sh all
scripts/test-suite.sh compatibility
scripts/test-suite.sh full-project
scripts/pack.sh all
python3 scripts/test-package-consumers.py artifacts

# Select one test target; the gate still builds both targets.
DUCKDB_TEST_FRAMEWORK=net10.0 scripts/test-suite.sh all
DUCKDB_TEST_FRAMEWORK=net11.0 scripts/test-suite.sh all

dotnet test test/DuckDB.EFCoreProvider.FunctionalTests -f net11.0 --filter FullyQualifiedName~NativeArrayCompatibilityTests
dotnet run --project samples/Quickstart -f net10.0
dotnet run --project samples/Quickstart -f net11.0
```

To consume a local source build, add `artifacts/ef10` or `artifacts/ef11` as a local NuGet source and
reference the desired package family from the table. A `net11.0` application can reference either family:
choose the EF10 package to retain EF10, or the EF11 package to opt into EF11. Reference only one family;
assembly names, namespaces and `UseDuckDB(...)` stay unchanged.

Use `scripts/pack.sh 10` or `scripts/pack.sh 11` to build just one family. Ordinary builds and tests still
compile both targets. Direct packaging requires `-p:DuckDBEFCoreMajorVersion=10` or `11`, so an unqualified
pack command cannot accidentally combine incompatible EF dependencies in one package.

Sample and benchmark run commands must select a framework with `-f`. The S3 benchmark runner defaults
to EF10; set `DUCKDB_BENCHMARK_FRAMEWORK=net11.0` to measure EF11 separately. Historical performance
numbers describe the original EF10 runs and are not measurements of EF11.

The production gate covers writes and migrations. The compatibility gate adds STRUCTs, native arrays,
owned value loading, binary JSON, aggregate query translation, raw-SQL precompilation, ordinary SQL
pregeneration, API parameter naming, file sources, ADO.NET substitution, bulk writes, DuckLake,
Quack and spatial coverage. The raw full project also exercises upstream specification backlog and is
not interchangeable with the production gate. External PostgreSQL/MinIO and tiered-storage CI jobs run
for both targets. Local runs can select `DUCKDB_TEST_FRAMEWORK` with the same integration scripts. By default the
PostgreSQL/MinIO runner recreates its disposable backend for each target, so EF11 starts with an empty catalog.

Use ordinary `[Fact]` and `[Theory]` attributes for new shared tests. The shared harness adapts upstream
fixture namespace/signature changes; EF10-only overrides refer to tests removed or consolidated in EF11.
Both versions use VSTest so existing filters and loggers continue to work. The additional Microsoft
engineering NuGet feed is restricted to EF11's test-only `Microsoft.DotNet.XUnitV3Extensions` dependency.

## Design-time tools

The root tool manifest retains EF10. `.config/ef11/dotnet-tools.json` pins EF11. Run EF10 commands from
the repository root; run EF11 commands from `.config/ef11`, passing absolute `--project` and
`--startup-project` paths. The design-time workflow test chooses the matching tool and framework,
generates a migration and compiled model, builds them, and executes a consumer against DuckDB.

## Compatibility details

- Precompiled `FromSqlRaw` and interpolated `FromSql` query roots use EF's runtime command resolver for
  argument arrays, including changed values, nulls and LINQ composition. Ordinary queries retain SQL
  pregeneration. This avoids trying to expand runtime arguments during precompilation. A separate
  precompiler limitation remains for `FromSql` placed inside another query's lambda: its argument array
  may not be registered in the generated query context; that nested form is not covered by this fix.
- Package identity selects EF. The EF10 family contains only `net10.0` assets, which NuGet can also select
  for `net11.0` applications without changing their EF major. The EF11 family contains only `net11.0` assets.
  A .NET framework upgrade therefore does not implicitly opt an EF10 consumer into EF11.
- EF10 dependencies permit `10.0.x` servicing patches starting at `10.0.10`. EF11 dependencies pin the
  tested preview exactly. Incompatible explicit EF references produce NuGet dependency diagnostics.
- EF11 uses new JSON mapping bases and expression/metadata contracts. Conditional adaptations stay in
  existing integration boundaries. EF10 retains its package/API baselines; the new EF11 package family
  will establish its own baseline after first publication. No cross-target mapping suppressions are needed.
- Native DuckDB array converters declare their existing null-preserving behavior on EF11 to avoid its
  JSON-string collection materializer. Native array migration defaults use typed empty arrays instead
  of EF11's generic JSON default. Tests cover null, empty, nullable and converted elements, projections,
  predicates and updates. Standard read-only and observable collection types materialize from native lists.
- Binary properties inside JSON use EF's base64 reader/writer. Float and decimal `AVG` expressions
  expose DuckDB's double result type before converting to the requested CLR type, preserving SQL casts.
  Widening numeric character conversions preserve the CLR character value, including EF11's parameterized
  character-collection joins, instead of interpreting the character as a text number. Characters mapped
  through numeric value converters retain ordinary SQL casts; `unicode` applies only to text-backed values.
- Owned values stored inline (shared-table references and owned JSON) load with the owner and respect
  explicit auto-include settings. Separate-table collection loading keeps the existing behavior.
  View-only mappings are not classified as shared tables when their table names are both absent;
  separately mapped views retain explicit owned-reference loading.
  EF11 additionally fixes upstream issue
  [#37525](https://github.com/dotnet/efcore/issues/37525) for assigning all-default values to optional
  owned dependents sharing columns in a TPH hierarchy; this upstream fix is not backported to EF10.
- Partial writes inside owned JSON documents are rejected before SQL generation. The previous write
  path could replace the entire JSON column with a scalar or SQL NULL. Use whole-column JSON writes
  for these updates; shared regression tests verify rejected writes leave the document intact.
- `FromSql` over roots containing complex JSON properties is unavailable on this EF11 preview because of
  upstream [#34627](https://github.com/dotnet/efcore/issues/34627). EF10 retains its existing test coverage.
- EF11's missing-migrations exception remains enabled. The DuckLake capability test explicitly ignores
  it to reach the existing unsupported-migrations guard. Other EF11 behavior changes, including
  explicit `PrimitiveCollection` configuration and split-query concurrency exceptions, remain EF-owned.

## Validation

Local validation uses .NET SDK `11.0.100-preview.7.26381.103` and compares EF10 with the unchanged
`2d0f106` checkout. Counts below are passed / skipped; every listed gate has zero failures.

| Gate | EF10 | EF11 preview 7 |
| --- | ---: | ---: |
| Production write gate (`all`) | 722 / 465 | 765 / 461 |
| Shared compatibility | 1,504 / 141 | 1,508 / 141 |
| PostgreSQL + MinIO DuckLake integration | 53 / 0 | 53 / 0 |
| Tiered storage S3 failure suite | 9 / 3 | 9 / 3 |
| Local tiered storage | 136 / 11 | 136 / 11 |

The solution build succeeds with zero warnings and errors. Core and NTS packaging validates the separate
package families and the existing EF10 API baselines. Isolated consumers reference only the packed NTS
package, resolve the matching core/EF dependencies, and exercise native-array insert/read/update checks
on .NET10/EF10, .NET11/EF10 and .NET11/EF11. Matching `dotnet-ef` migration and compiled-model tests are included in the gates.

After the release-review fixes and package-family split, the production and shared compatibility gates,
solution build, and package/API validation were rerun. New regression coverage verifies numeric and nullable character
converters, text character codes, and explicit loading from separately mapped views on both targets.
The external-backend and raw full-project counts recorded here describe the earlier validation run.

Package CI verifies asset layout, dependency bounds and matching NTS-to-core references, then restores
consumers with an isolated NuGet cache. It also checks that ambiguous pack commands, mismatched target
frameworks and incompatible explicit EF dependency versions produce diagnostics. Publishing selection
was checked for both families, manual selection and mismatched release tags.

External backend validation uses an isolated copy of the same source so shared test databases cannot
interfere with the raw specification run. Docker services and disposable volumes are removed afterward.
Repository-wide formatting still reports pre-existing diagnostics; introduced formatting differences and
`git diff --check` are checked separately.

### Raw specification backlog

The unfiltered EF10 project is green with its existing skips. EF11 still has specification gaps; keep
those failures visible when developing further EF11 functionality. Passing the production and compatibility
gates does not claim complete upstream conformance.

The unchanged EF10 baseline had three failures: `PrecompiledQueryDuckDBTest.FromSqlRaw`,
`PrecompiledQueryDuckDBTest.FromSql_with_FormattableString_parameters`, and
`DuckDBApiConsistencyTest.Public_api_bool_parameters_should_not_be_prefixed` (the existing
`isRootNullable` parameter). All three are now fixed on both targets. Raw-SQL precompiled queries use
runtime argument expansion; the metadata fix renames only a private nested helper's parameter to
`rootNullable`, preserving public constructor parameter names and behavior. Shared regression tests
also check argument rebinding, null values and LINQ composition.

Additional EF11 specification coverage exposes these remaining areas:

| Area | Remaining work |
| --- | --- |
| Numeric and temporal translation | Numeric `Parse` methods and additional `DateTimeOffset` members/constructors. |
| Binary values | Native `BLOB[]` materialization in the driver, constant byte-array equality, and byte-array `Any`. Binary values within complex JSON are covered and pass. |
| Owned navigation projections | Nested separately stored owned values in the new no-tracking projection cases. |
| Grouping | New whole-entity grouping/projection cases return different results and need translator work. |
| Tracking | The new many-to-many/composite-reference update scenario encounters duplicate tracked join entities. |
| NativeAOT compiled models | The new indexed owned-entity scenario requires additional type-mapping generation support. Ordinary migration/compiled-model workflows pass. |
| Specification coverage | New upstream test-base families, including runtime migrations and additional JSON/inheritance coverage, do not yet have provider fixtures. |

| Raw full-project run | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Unchanged EF10 baseline (`2d0f106`) | 21,739 | 4,138 | 3 |
| Shared source, EF10 | 21,750 | 4,138 | 0 |
| Shared source, EF11 preview 7 | 22,099 | 4,113 | 34 |

EF10 has zero failures. EF11 retains 34 failures in the additional coverage listed above; all three
former baseline failures now pass on both targets. The new EF11 `FromSql`/complex-JSON case is
explicitly skipped for upstream #34627; the remaining gaps stay visible as failures.

Fixtures with intentionally distinct warning configurations disable EF's service-provider cache so they
do not add entries to its process-wide cache. Their warning configuration still checks the actual
keyless-seeding and backend capability errors; production warning behavior is unchanged.
