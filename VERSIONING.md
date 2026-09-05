# Versioning & Breaking-Change Policy

`DuckDB.EFCoreProvider` follows [Semantic Versioning 2.0.0](https://semver.org/) (`MAJOR.MINOR.PATCH`).

## What each part means

- **MAJOR** — incremented for a breaking change to the supported public API, or when the provider
  drops an existing Entity Framework Core target (see *EF Core alignment* below).
- **MINOR** — new, backward-compatible functionality (additional translations, type mappings, options,
  newly supported EF Core features).
- **PATCH** — backward-compatible bug fixes and documentation/build changes.

## What counts as "public API"

The supported surface is the API a host application uses directly:

- the `UseDuckDB(...)` configuration extension methods and their options;
- public extension methods under `DuckDB.EFCoreProvider.Extensions` (e.g. `UseAutoIncrement()`, `FromParquet`);
- the `[FromParquet]` attribute and other public metadata/builder APIs;
- the spatial entry point `UseNetTopologySuite()`.

**Not covered by SemVer** (may change in any release, including PATCH):

- Everything in an `Internal` namespace, and any API marked with the EF Core internal-API warnings
  (`EF1001`, `EF9100`). These exist to satisfy EF Core provider contracts and follow EF Core's own
  "internal API" stability policy — i.e. none.
- Generated SQL text. SQL shape may change between any versions as translations improve; only observable
  query *results* are treated as behaviour.

## EF Core alignment

- Develop features once in shared source and run tests against both EF versions. EF-specific API adapters
  stay at their existing integration points.
- Package identity selects the EF line. `DuckDB.EFCoreProvider` and `DuckDB.EFCoreProvider.NTS` target EF10;
  `DuckDB.EFCoreProvider.EF11` and `DuckDB.EFCoreProvider.EF11.NTS` target EF11. Assembly names, namespaces
  and public entry points remain unchanged. Reference only one family in an application.
- Core `1.26.0` / NTS `1.1.0` are the EF10 release versions. EF11 initially uses core `1.26.0-preview.1` /
  NTS `1.1.0-preview.1`. Adding EF11 does not consume provider major version 2, which is reserved for
  the anticipated DuckDB 2 transition. Existing public-API breaking-change rules still apply.
- The EF10 packages contain only `net10.0` assets and work in compatible newer .NET applications,
  including `net11.0`, while retaining EF10. The EF11 packages contain only `net11.0` assets.
- EF10 dependencies allow servicing versions from `10.0.10` up to, but excluding, `10.1.0`. EF11 dependencies
  are pinned to `11.0.0-preview.7.26381.103` until a later preview or stable release is rebuilt and validated.
- Microsoft EF packages remain aligned within each target. Stable EF10 packages have no prerelease
  dependencies; EF11 packages remain prereleases while they depend on preview EF packages.
- `scripts/pack.sh 10`, `scripts/pack.sh 11`, or `scripts/pack.sh all` select the package family explicitly.
  Direct `dotnet pack` requires `-p:DuckDBEFCoreMajorVersion=10` or `11`; ambiguous multi-EF packages are rejected.
- EF10 API validation retains its published baselines. EF11 starts a new package identity without an EF10
  baseline or cross-target JSON mapping suppressions; establish its own baseline after its first release.
- Every target must pass the production and compatibility gates. Package CI additionally tests
  .NET10/EF10, .NET11/EF10 and .NET11/EF11 consumers and diagnoses incompatible EF dependency combinations.
- Publishing selects exactly one family. EF10 releases use `v<core-version>` tags; EF11 uses
  `ef11-v<core-version>` tags. Manual publishing requires an explicit EF major selection. Release tags are
  checked against the selected project's evaluated package version before packing. A combined GitHub release
  uses the EF10 version tag and publishes the EF11 family by manual dispatch at that same immutable tag.

## Deprecation

Where practical, public API that is being removed will first be marked `[Obsolete]` in a MINOR release with
a migration note, and removed no earlier than the next MAJOR release.

## Documented limitations are not bugs

Behaviours listed in [`docs/CAPABILITY-MAP.md`](docs/CAPABILITY-MAP.md) as DuckDB engine limitations are
intentional and are not treated as defects. Changing them depends on DuckDB itself, not the provider.

## Release notes

Each release documents notable changes in the package release notes / repository history. Review them
before upgrading, especially across MINOR bumps that may change generated SQL.
